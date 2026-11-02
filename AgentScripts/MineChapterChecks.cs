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
    private static object Call(object obj, string name, params object[] args)
    {
        try { return obj.GetType().GetMethod(name, Hidden).Invoke(obj, args); }
        catch (TargetInvocationException e) { throw new Exception(e.InnerException?.ToString() ?? e.ToString()); }
    }
    public static object Run()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required.");
        var checks = new List<string>();
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add(message); }
        var singleton = typeof(SaveIOService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = singleton.GetValue(null);
        string folder = Path.GetFullPath("output/MineChapterReview/Checks-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        singleton.SetValue(null, typeof(SaveIOService).GetConstructor(Hidden, null, new[] { typeof(string) }, null).Invoke(new object[] { folder }));
        var test = new GameObject("MineChapterChecks") { hideFlags = HideFlags.HideAndDontSave }; test.SetActive(false);
        try
        {
            ChapterContentCatalog.Reset();
            var originalEnv = UnityEngine.Object.FindFirstObjectByType<EnvController>(FindObjectsInactive.Include);
            Check(originalEnv != null, "DecisionScene environment available");
            var omnibus = SaveIOService.Instance.LoadResourceData<OmnibusData>("Omnibus_01");
            Check(originalEnv.TryValidateAllContent(omnibus, out var validation), "Mine and prepared boss terrain references validate: " + validation);
            var terrain = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Assets/Decision_YYS/Resources/Chapters/Chapter_01/Terrain.json"));
            Check(terrain["episodes"]["Chapter_1_01"]["TerrainInfo"]["places"].Count() == 4, "One mine chunk contains exactly four places");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decision_YYS/Prafabs/Terrain/MineVillageFirstChunk.prefab");
            Check(prefab.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("ActorSocket_")) == 8, "Eight actor sockets across four place anchors");
            Check(PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant, "Mine prefab reuses the modular source as a variant");
            var rules = MineChapterSettings.Load();
            Check(rules.openings.Length == 4 && rules.openings.Take(3).All(o => o.variants.Length == 2) && rules.openings[3].variants.Length == 1, "Three paired variants and one Nea/Sia first meeting");
            Check(Mathf.Approximately(rules.SuccessChance(0), 0.5f) && Mathf.Approximately(rules.SuccessChance(20), 0.7f), "Success formula: ability 0=50%, 20=70%");
            Check(Mathf.Approximately(rules.StatusMultiplier(0), 1.5f) && Mathf.Approximately(rules.BossAttackMultiplier(10), 1.1f), "Status and boss formula proposals are reproducible");
            Check(MineChapterSettings.ApplyDelta(new[] { 3, 3, 3, 3 }, new[] { -5, -2, 1, -1 }).SequenceEqual(new[] { 0, 1, 4, 2 }), "Labor penalties floor balances at zero");
            Check(MineChapterSettings.Roll(42, 10, "labor") == MineChapterSettings.Roll(42, 10, "labor"), "Reload cannot reroll a committed activity");
            var repository = new EncounterContentRepository();
            var scenes = new Dictionary<string, ScenarioData>();
            foreach (var info in ChapterContentCatalog.Stories)
            {
                string path = (string)info["path"];
                Check(repository.TryLoadCards(path, out var story, out var error), "Load " + (string)info["key"] + ": " + error);
                scenes.Add(path, story);
            }
            Check(scenes.Count == 102, "102 branch nodes load through the production repository");
            var fourth = scenes["Chapter_1/Chapter_1_01/Encounters/nea_sia"];
            Check(fourth.MainStory.Count == 10 && fourth.MainStory[0].speakerId == "Nea", "Fourth-place first meeting keeps all ten speeches and Nea's opening");
            Check(scenes.Values.SelectMany(s => s.MainStory).Any(c => c.options?.Length == 2) && scenes.Values.SelectMany(s => s.MainStory).Any(c => c.options?.Length == 3), "Original two/three-option menus preserved");
            foreach (string key in new[] { "rumor_1", "nea_sia" })
                Check(!ChapterContentCatalog.CanRewrite("Chapter_1/Chapter_1_01/Encounters/" + key), "AI rewriting blocked for " + key);
            var save = new SaveManager(); var progress = save.LoadProgress();
            var manager = test.AddComponent<DecisionManager>(); var stats = test.AddComponent<StatContainer>();
            var entries = new SerializedObject(stats); var values = entries.FindProperty("statEntries"); values.arraySize = 4; entries.ApplyModifiedPropertiesWithoutUndo();
            var session = new DecisionSession(omnibus, save.CurrentRun, save.RunSeed, omnibus.FirstPlayableChapterIndex, 0, 0) { ScenarioPath = "Chapter_1/Chapter_1_01" };
            var mine = progress.guideProgress.mineChapter = new MineChapterProgress();
            Set(manager, "session", session); Set(manager, "saveService", new DecisionSaveService(save));
            Set(manager, "guideProgress", progress.guideProgress); Set(manager, "storyEvents", progress.storyEvents);
            Set(manager, "statContainer", stats); Set(manager, "coinDropController", test.AddComponent<CoinDropController>());
            Set(manager, "presentationController", new DecisionPresentationController(null)); Set(manager, "mine", mine); Set(manager, "mineRules", rules);
            int visit = 1000, transactions = 0;
            foreach (var scene in scenes)
            foreach (var card in scene.Value.MainStory.Where(c => c.choiceActions != null))
            for (int slot = 0; slot < card.choiceActions.Length; slot++)
            {
                var choice = card.choiceActions[slot]; if (choice.abilityCost == null) continue;
                stats.SetStats(new[] { 100, 100, 100, 100 });
                Set(manager, "storyVisit", visit++); Set(manager, "selectedGearIndex", slot); Set(manager, "activePlaceId", "brook"); Set(manager, "activeEncounterPath", scene.Key);
                Call(manager, "ExecuteStoryChoice", card);
                var expected = Enumerable.Range(0, 4).Select(i => 100 - choice.abilityCost[i]).ToArray();
                Check(stats.stats.SequenceEqual(expected), choice.choiceId + " exact authored cost");
                int total = mine.recipients.Sum(n => n.coins.Sum()); Call(manager, "ExecuteStoryChoice", card);
                Check(stats.stats.SequenceEqual(expected) && mine.recipients.Sum(n => n.coins.Sum()) == total, choice.choiceId + " no duplicate payment or NPC credit");
                transactions++;
            }
            var payCard = scenes["Chapter_1/Chapter_1_01/Encounters/brook_pay"].MainStory.Last();
            stats.SetStats(new[] { 9, 9, 9, 9 }); Set(manager, "storyVisit", visit++); Set(manager, "selectedGearIndex", 0);
            int count = progress.storyEvents.Count; Call(manager, "ExecuteStoryChoice", payCard);
            Check(stats.stats[0] == 9 && progress.storyEvents.Count == count, "Insufficient funds do not charge or record a choice");
            mine.activityKey = "labor:resume"; mine.activityRemaining = 7.5f; mine.activityDelta = new[] { 2, -3, 1, -4 }; mine.resultPath = "result"; mine.collectorZ = 12.5f; mine.fatigue = 40; mine.racePresses = 19;
            Call(manager, "SaveGuideCheckpoint", new object[] { null });
            var restored = new SaveManager().LoadProgress().guideProgress.mineChapter;
            Check(restored.activityRemaining == 7.5f && restored.collectorZ == 12.5f && restored.racePresses == 19 && restored.recipients.Sum(n => n.coins.Sum()) == mine.recipients.Sum(n => n.coins.Sum()), "Activity, race, collector and NPC receipts restore together");
            Check(payCard.choiceActions[2].receiverId == "Dor", "Brook's knowledge payment credits Dor, as authored");
            stats.SetStats(new[] { 10, 30, 30, 30 }); Set(manager, "storyVisit", visit++);
            int menus = 0;
            Set(manager, "runCompletionService", new DecisionRunCompletionService(save.CompleteCurrentRun, save.StartNextRun, () => menus++));
            Call(manager, "ExecuteStoryChoice", payCard);
            Check(stats.stats[0] == 0 && menus == 1 && SaveIOService.Instance.LoadOrCreateProfile().currentRun == 2,
                "Paying the last strength coin ends the run once and prepares the next run");
            var report = new { passed = true, count = checks.Count, transactions, checks, isolatedSaveRoot = folder };
            Directory.CreateDirectory("output/MineChapterReview");
            File.WriteAllText("output/MineChapterReview/checks.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            return report;
        }
        finally { UnityEngine.Object.DestroyImmediate(test); singleton.SetValue(null, previous); ChapterContentCatalog.Reset(); }
    }
}
