using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class MineChapterChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Hidden).SetValue(obj, value);
    private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Hidden).Invoke(obj, args);
    private static void Check(bool condition, string message, List<string> checks)
    { if (!condition) throw new Exception(message); checks.Add(message); }

    public static object Run()
    {
        if (Application.isPlaying) throw new Exception("Run checks in Edit mode.");
        var checks = new List<string>();
        var singleton = typeof(SaveIOService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = singleton.GetValue(null);
        string folder = Path.GetFullPath("output/MineChapterReview/Checks-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var store = typeof(SaveIOService).GetConstructor(Hidden, null, new[] { typeof(string) }, null).Invoke(new object[] { folder });
        singleton.SetValue(null, store);
        var test = new GameObject("MineChapterChecks") { hideFlags = HideFlags.HideAndDontSave };
        test.SetActive(false);
        try
        {
            ChapterContentCatalog.Reset();
            var omnibus = SaveIOService.Instance.LoadResourceData<OmnibusData>("Omnibus_01");
            var originalEnv = UnityEngine.Object.FindFirstObjectByType<EnvController>(FindObjectsInactive.Include);
            var env = test.AddComponent<EnvController>();
            EditorUtility.CopySerialized(originalEnv, env);
            var environment = new GameObject("IsolatedEnvironment");
            environment.transform.SetParent(test.transform, false);
            Set(env, "environment", environment);
            Call(env, "Awake");
            Check(env.TryValidateAllContent(omnibus, out var error), "All playable terrain and dialogue references load: " + error, checks);
            Check(env.RegisterTerrain("Chapter_1", "Chapter_1_01"), "Provided mine terrain registers", checks);
            Check(Mathf.Approximately(env.PlaceRegistry.GetChunkWorldPosition(0).z, 0) &&
                Mathf.Approximately(env.CurrentTerrainEndZ, 120), "Only the provided 120-unit mine starts at the journey origin", checks);
            var playerObject = new GameObject("IsolatedPlayer");
            playerObject.transform.SetParent(test.transform, false);
            var player = playerObject.AddComponent<Player>();
            player.transform.position = new Vector3(0, 0, 11);
            env.BindPlayer(player);
            var brook = environment.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Place_brook").ToArray();
            Check(brook.Length == 1, "Exactly one authored Brook; no duplicate runtime NPC", checks);
            Check(brook[0].childCount == 1 && Vector3.Dot(brook[0].forward, brook[0].GetChild(0).forward) < -0.99f,
                "Persisted prefab adapts the miner's reversed model-forward convention", checks);
            Check(env.PlaceRegistry.TryGetCharacter("Brook", out var place) &&
                Vector3.Distance(brook[0].position, env.PlaceRegistry.GetWorldPosition(place)) < 0.001f, "Brook mesh and encounter coordinates agree", checks);
            var repository = new EncounterContentRepository();
            Check(repository.TryLoadCards(place.connectStoryCards, out var story, out error), "Brook source loads through production repository: " + error, checks);
            Check(story.MainStory.Count == 5 && story.MainStory.Count(c => c.IsChoice) == 2 &&
                story.MainStory[1].speakerId == "Dor", "Five original speeches, Dor attribution, and eight player options preserved", checks);
            for (int slot = 0; slot < 4; slot++)
            {
                var manager = test.AddComponent<DecisionManager>();
                var stats = test.AddComponent<StatContainer>();
                var serialized = new SerializedObject(stats);
                var entries = serialized.FindProperty("statEntries"); entries.arraySize = 4;
                for (int i = 0; i < 4; i++) entries.GetArrayElementAtIndex(i).FindPropertyRelative("value").intValue = 3;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var save = new SaveManager();
                var progress = save.LoadProgress();
                var session = new DecisionSession(omnibus, save.CurrentRun, save.RunSeed, omnibus.FirstPlayableChapterIndex, 0, 0) { ScenarioPath = "Chapter_1/Chapter_1_01" };
                Set(manager, "session", session); Set(manager, "saveService", new DecisionSaveService(save));
                Set(manager, "guideProgress", progress.guideProgress); Set(manager, "storyEvents", progress.storyEvents);
                Set(manager, "storyVisit", slot + 100); Set(manager, "selectedGearIndex", slot);
                Set(manager, "activePlaceId", "brook"); Set(manager, "activeEncounterPath", place.connectStoryCards);
                Set(manager, "statContainer", stats); Set(manager, "coinDropController", test.AddComponent<CoinDropController>());
                Set(manager, "presentationController", new DecisionPresentationController(null));
                var card = story.MainStory.Last();
                Call(manager, "ExecuteStoryChoice", card);
                Check(stats.stats.Select((v, i) => v == (i == slot ? 2 : 3)).All(x => x), "Payment slot " + slot + " decreases only its selected ability", checks);
                Call(manager, "ExecuteStoryChoice", card);
                Check(stats.stats[slot] == 2 && progress.storyEvents.Count(e => e.eventKey == "choice:" + (slot + 100)) == 1,
                    "Confirmed payment slot " + slot + " cannot charge twice", checks);
                var restored = new SaveManager();
                Check(restored.LoadStats().stats.SequenceEqual(stats.stats) && restored.LoadProgress().storyEvents.Last().statsAfter.SequenceEqual(stats.stats),
                    "Payment slot " + slot + " and journal restore together", checks);
                if (slot == 0)
                {
                    stats.SetStats(new[] { 0, 3, 3, 3 }); Set(manager, "storyVisit", 200);
                    int count = progress.storyEvents.Count;
                    Check((bool)Call(manager, "ExecuteStoryChoice", card) && progress.storyEvents.Count == count && stats.stats[0] == 0,
                        "Insufficient balance blocks payment without negative ability or saved choice", checks);
                    stats.SetStats(new[] { 1, 3, 3, 3 }); Set(manager, "storyVisit", 201);
                    int menu = 0;
                    Set(manager, "runCompletionService", new DecisionRunCompletionService(save.CompleteCurrentRun, save.StartNextRun, () => menu++));
                    Call(manager, "ExecuteStoryChoice", card);
                    Check(stats.stats[0] == 0 && menu == 1 && SaveIOService.Instance.LoadOrCreateProfile().currentRun == 2,
                        "Paying the last strength coin ends the run and prepares the next run", checks);
                }
            }
            Check(!AbilityPayment.CanPay(new[] { 3, 3, 3, 3 }, new[] { -1, 0, 0, 0 }), "Malformed negative cost is rejected", checks);
            Check(ChapterContentCatalog.Chapters.Count() == 1 && ChapterContentCatalog.Stories.Count() == 1 &&
                ChapterContentCatalog.Info("Initial/Initial_01/Encounters/guide") == null,
                "Runtime and generation catalog contains only the provided mine story", checks);
            Check(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decision_YYS/Prafabs/Terrain_Initial_Village_01.prefab") == null &&
                Resources.Load<TextAsset>("Chapters/Chapter_00/Terrain") == null,
                "Retired map and story are outside Unity runtime assets", checks);
            var configured = LocalStoryNotebook.ConfiguredContexts(SaveIOService.Instance, Array.Empty<string>(), false);
            Check(configured.Select(c => c.characterId).OrderBy(x => x).SequenceEqual(new[] { "Brook", "Dor" }),
                "Shipped NPC configuration contains only source-story participants", checks);
            Check(LocalStoryTestInputs.Create(2).events[0].choiceId == story.MainStory[3].choiceActions[0].choiceId &&
                LocalStoryTestInputs.Create(3).events[0].semanticText == story.MainStory[3].options[3],
                "AI comparison inputs derive their facts and choices from the provided story", checks);
            var report = new { passed = true, checks, isolatedSaveRoot = folder };
            Directory.CreateDirectory("output/MineChapterReview");
            File.WriteAllText("output/MineChapterReview/checks.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            return report;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(test);
            singleton.SetValue(null, previous);
            ChapterContentCatalog.Reset();
        }
    }
}
