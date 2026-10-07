using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using JourneyMapKit;

public static class GuideStageOneCheck
{
    static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    static void Drain(IEnumerator routine)
    {
        int steps = 0;
        while (routine.MoveNext())
        {
            if (++steps > 1000) throw new Exception("Coroutine did not complete");
            if (routine.Current is IEnumerator nested) Drain(nested);
        }
    }
    static void Check(bool valid, string message, List<string> checks)
    {
        if (!valid) throw new Exception(message);
        checks.Add(message);
    }

    public static object BindKneel()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/DecisionScene.unity" || scene.isDirty)
            throw new Exception("Expected clean stopped DecisionScene; refusing to save other edits");
        var manager = UnityEngine.Object.FindFirstObjectByType<DecisionManager>(FindObjectsInactive.Include);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/UnityAssets/KnightAsset/KnightAnim/HumanArmature_kneel.anim");
        if (manager == null || clip == null) throw new Exception("Missing manager/clip");
        var serialized = new SerializedObject(manager);
        Undo.RecordObject(manager, "Bind existing Loki kneel clip");
        serialized.FindProperty("guideKneelClip").objectReferenceValue = clip;
        serialized.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed");
        return new { bound = clip.name, scene = scene.path, dirty = scene.isDirty };
    }

    public static object Regression()
    {
        if (Application.isPlaying) throw new Exception("Run edit-mode checks only");
        var checks = new List<string>();
        var singleton = typeof(SaveIOService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
        object previousIO = singleton.GetValue(null);
        string folder = Path.Combine(Application.dataPath, "../Temp/GuideStageOneChecks", Guid.NewGuid().ToString("N"));
        var isolated = typeof(SaveIOService).GetConstructor(Hidden, null, new[] { typeof(string) }, null).Invoke(new object[] { folder });
        singleton.SetValue(null, isolated);
        var testObject = new GameObject("GuideStageOne_IsolatedChecks") { hideFlags = HideFlags.HideAndDontSave };
        testObject.SetActive(false);
        try
        {
            var repo = new EncounterContentRepository(1);
            string root = "Initial/Initial_01/Encounters/guide";
            var paths = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/Decision_YYS/Resources/Story_Json_Data/Initial/Initial_01/Encounters" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith("/Story.json")).ToArray();
            foreach (string path in paths)
            {
                string content = path.Substring("Assets/Decision_YYS/Resources/Story_Json_Data/".Length);
                content = content.Substring(0, content.Length - "/Story.json".Length);
                Check(repo.TryLoadCards(content, out var data, out var error), "Valid Story graph: " + content + " " + error, checks);
            }
            repo.TryLoadCards(root, out var intro, out _);
            Check(intro.MainStory.Count == 9 && intro.MainStory.Last().choiceActions.Select(c => c.choiceId).Distinct().Count() == 4,
                "Loki introduction and four stable identity choices", checks);
            Check(JsonUtility.FromJson<StoryCharacter>(Resources.Load<TextAsset>("Story_Json_Data/Characters/Guide").text).name == "로키", "Separate Loki character profile", checks);
            foreach (string npc in new[] { "duelist", "gambler" })
            {
                string path = "Initial/Initial_01/Encounters/" + npc;
                repo.TryLoadCards(path, out var normal, out _);
                foreach (var flags in new[] { new[] { true, false }, new[] { false, true }, new[] { true, true } })
                {
                    Check(repo.TryLoadCards(path, out var alternate, out _, flags[0], flags[1]) && alternate.MainStory[0].text != normal.MainStory[0].text,
                        npc + " variant " + string.Join("/", flags), checks);
                    Check(alternate.MainStory.Last().choiceActions.Select(c => JsonUtility.ToJson(c)).SequenceEqual(normal.MainStory.Last().choiceActions.Select(c => JsonUtility.ToJson(c))), npc + " variant preserves choice outcomes", checks);
                }
            }
            var draft = new int[4];
            for (int i = 0; i < 20; i++) Check(GuideAllocationRules.TryChange(draft, 0, 1), "Budget add " + i, checks);
            Check(!GuideAllocationRules.TryChange(draft, 1, 1) && !GuideAllocationRules.TryChange(draft, 0, 1), "Budget/capacity upper limits", checks);
            Check(GuideAllocationRules.TryChange(draft, 0, -1) && GuideAllocationRules.Remaining(draft) == 1 && GuideAllocationRules.TryChange(draft, 1, 1), "Remove refunds allocation pool", checks);
            Check(!GuideAllocationRules.TryChange(draft, 2, -1), "Zero cannot be decremented", checks);
            Check(GuideAllocationRules.Outcome(new[] { 0, 0, 10, 10 }) == "death", "Health zero precedes speed retry", checks);
            Check(GuideAllocationRules.Outcome(new[] { 5, 0, 5, 10 }) == "retry", "Speed zero requests redistribution", checks);
            Check(GuideAllocationRules.Outcome(new[] { 10, 10, 0, 0 }) == "ready", "Knowledge/charm zero can continue", checks);
            Check(GuideAllocationRules.Outcome(new[] { 1, 1, 1, 1 }) == "incomplete", "Cannot commit unused budget", checks);

            var save = new SaveManager();
            var stats = testObject.AddComponent<StatContainer>();
            Set(stats, "statEntries", new List<StatContainer.StatEntry> { new StatContainer.StatEntry(), new StatContainer.StatEntry(), new StatContainer.StatEntry(), new StatContainer.StatEntry() });
            stats.SetStats(new[] { 5, 5, 5, 5 });
            var coins = testObject.AddComponent<CoinDropController>();
            coins.InitializeInventory(new[] { 10, 10, 10, 10 });
            var manager = testObject.AddComponent<DecisionManager>();
            var progress = new GuideProgress();
            var journal = new List<StoryEventRecord>();
            var omnibus = new OmnibusData { chapters = new List<ChapterInfo> { new ChapterInfo { chapterId = "Initial", episodeIds = new List<string> { "Initial_01" } } }, lastPlayableChapterId = "Initial" };
            var session = new DecisionSession(omnibus, 1, 7, 0, 0, 0) { ScenarioPath = "Initial/Initial_01" };
            Set(manager, "session", session); Set(manager, "saveService", new DecisionSaveService(save));
            Set(manager, "statContainer", stats); Set(manager, "coinDropController", coins);
            Set(manager, "guideProgress", progress); Set(manager, "storyEvents", journal);
            Set(manager, "presentationController", new DecisionPresentationController(null));
            Set(manager, "activePlaceId", "guide_01"); Set(manager, "activeEncounterPath", root);
            stats.SetStats(new[] { 5, 5, 5, 0 });
            for (int slot = 0; slot < 4; slot++)
            {
                Set(manager, "storyVisit", slot + 1); Set(manager, "selectedGearIndex", slot);
                Check((bool)Call(manager, "ExecuteStoryChoice", intro.MainStory.Last()), "Identity choice " + slot + " resolves branch", checks);
            }
            Check(journal.Select(e => e.choiceId).Distinct().Count() == 4 && journal.Last().displayedText == "꺼져!" && journal.Last().relationshipDelta == -4,
                "Actual choice wording/IDs and charm-zero doubled relationship saved", checks);
            Check(save.LoadProgress().relationships.Find(r => r.id == "Guide").value == -2, "Relationship effects applied once", checks);
            Call(manager, "ExecuteStoryChoice", intro.MainStory.Last());
            Check(journal.Count == 4 && save.LoadProgress().relationships.Find(r => r.id == "Guide").value == -2, "Choice checkpoint is idempotent", checks);
            stats.SetStats(new[] { 5, 5, 5, 5 });
            int beforePrank = journal.Count;
            Dialogue take = intro.MainStory[3];
            Drain((IEnumerator)Call(manager, "RunGuideAction", take));
            Check(coins.RemainingCoins.SequenceEqual(new[] { 10, 10, 5, 10 }) && stats.stats.SequenceEqual(new[] { 5, 5, 5, 5 }), "Knowledge prank removes five coins, not permanent stats", checks);
            Drain((IEnumerator)Call(manager, "RunGuideAction", take));
            Check(journal.Count == beforePrank + 1 && coins.RemainingCoins[2] == 5, "Completed prank cannot execute twice", checks);
            Drain((IEnumerator)Call(manager, "RunGuideAction", intro.MainStory[5]));
            Check(coins.RemainingCoins.SequenceEqual(new[] { 10, 10, 10, 10 }), "Original knowledge coins return exactly", checks);
            repo.TryLoadCards(root + "/Insult", out var insult, out _);
            Set(manager, "activeEncounterPath", root + "/Insult");
            Drain((IEnumerator)Call(manager, "RunGuideAction", insult.MainStory[0]));
            Check(coins.RemainingCoins.SequenceEqual(new[] { 1, 10, 10, 10 }) && progress.effectStage == 2, "Punishment drains/refills and only health stays one", checks);
            int count = journal.Count;
            Drain((IEnumerator)Call(manager, "RunGuideAction", insult.MainStory[0]));
            Check(journal.Count == count && coins.RemainingCoins[0] == 1, "Punishment is idempotent", checks);
            // Simulate quitting midway through the remove-five sequence.
            progress.completedEffects.Remove("guide_01:" + root + ":" + take.eventIdStable);
            progress.activeEffectId = "guide_01:" + root + ":" + take.eventIdStable;
            progress.knowledgeBefore = new[] { 10, 10, 10, 10 }; progress.effectBefore = progress.knowledgeBefore;
            coins.InitializeInventory(new[] { 10, 10, 8, 10 }); Set(manager, "activeEncounterPath", root);
            Drain((IEnumerator)Call(manager, "RunGuideAction", take));
            Check(coins.RemainingCoins[2] == 5, "Interrupted prank resumes toward saved target", checks);

            repo.TryLoadCards(root + "/Redistribute", out var redistribution, out _);
            Set(manager, "activeCards", redistribution); Set(manager, "activeCardIndex", redistribution.MainStory.Count - 1);
            Set(manager, "activeEncounterPath", root + "/Redistribute"); Set(manager, "storyVisit", 100);
            var allocate = redistribution.MainStory.Last();
            Drain((IEnumerator)Call(manager, "RunGuideAction", allocate));
            Check(progress.allocating && coins.RemainingCoins.All(c => c == 0) && stats.stats[0] == 5, "Draft zero is temporarily immortal", checks);
            Call(manager, "HandleEncounterGearSelection", 0); Check(progress.allocationMode == 1, "Gear up selects add", checks);
            Call(manager, "HandleEncounterGearSelection", 3); Check(progress.allocationMode == -1, "Gear down selects remove", checks);
            Call(manager, "ConfirmGuideAllocation"); Check(progress.allocating, "Incomplete confirmation stays open", checks);
            progress.allocation = new[] { 6, 7, 7, 0 }; coins.InitializeInventory(progress.allocation);
            Call(manager, "ConfirmGuideAllocation");
            Check(!progress.allocating && stats.stats.SequenceEqual(new[] { 6, 7, 7, 0 }), "Allocation commits all twenty to stats", checks);
            var checkpoint = new SaveManager().LoadProgress();
            Check(checkpoint.guideProgress.committedStats.SequenceEqual(stats.stats) && checkpoint.storyEvents.Last().statsBefore.SequenceEqual(new[] { 5, 5, 5, 5 }), "Allocation checkpoint retains before/after and reloads stats", checks);
            int eventsBefore = journal.Count;
            Drain((IEnumerator)Call(manager, "RunGuideAction", allocate));
            Check(journal.Count == eventsBefore && !progress.allocating, "Confirmed allocation is not reopened on resume", checks);
            var chapters = new ChapterFlowController(session, stats, null, new DecisionSaveService(save));
            Check(chapters.CompleteChapter(Vector3.zero, Quaternion.identity) && stats.stats.SequenceEqual(new[] { 6, 7, 7, 0 }), "Chapter completion retains allocated stats", checks);
            session.ChapterIndex = 0;
            session.PlayedEncounterHistory.Add(new PlayedEncounterCardRecord { encounterPath = root, card = take });
            Call(manager, "SaveGuideCheckpoint", new object[] { null }); session.PlayedEncounterHistory.Clear();
            Check(save.LoadProgress().currentEncounterHistory.Count == 1, "Episode history clear cannot erase saved journal", checks);
            int completionCalls = 0;
            Set(manager, "runCompletionService", new DecisionRunCompletionService(() => completionCalls++, () => 2, () => completionCalls++));
            Set(manager, "storyVisit", 101); progress.allocation = new[] { 0, 10, 5, 5 }; progress.allocating = true;
            coins.InitializeInventory(progress.allocation); Call(manager, "ConfirmGuideAllocation");
            Check(completionCalls == 2, "Health zero completes run and returns to menu callback", checks);
            var request = SaveIOService.Instance.LoadRunData<LocalStoryRevisionRequest>(1, "LocalStoryRevisionRequest");
            Check(request != null && request.status == "PendingLocalModel" && request.events.Any(e => e.reason == "death"), "Local next-run request saved without external generation", checks);
            return new { passed = checks.Count, checks, isolatedSaveFolder = folder, sceneWasNotSaved = true };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(testObject);
            singleton.SetValue(null, previousIO);
        }
    }
    public static object RestoreIncidentalLayout()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/DecisionScene.unity") throw new Exception("Wrong scene");
        foreach (var rect in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (rect.name != "DuelTestButton" && rect.name != "GambleTestButton") continue;
            var serialized = new SerializedObject(rect);
            serialized.FindProperty("m_AnchorMin").vector2Value = Vector2.zero;
            serialized.FindProperty("m_AnchorMax").vector2Value = Vector2.zero;
            serialized.FindProperty("m_AnchoredPosition").vector2Value = Vector2.zero;
            serialized.FindProperty("m_SizeDelta").vector2Value = Vector2.zero;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        return new { saved = EditorSceneManager.SaveScene(scene), preservedOriginalButtonLayout = true };
    }

    public static object TextFits()
    {
        var storyUI = UnityEngine.Object.FindFirstObjectByType<MainStoryUi>(FindObjectsInactive.Include);
        var text = (TMPro.TextMeshProUGUI)typeof(MainStoryUi).GetField("front_Dialogue_Text", Hidden).GetValue(storyUI);
        string preview = "기어 위: 추가 · 아래: 빼기\n남은 20/20 · 기어 방향을 선택해";
        Vector2 preferred = text.GetPreferredValues(preview, text.rectTransform.rect.width, Mathf.Infinity);
        var option = (TMPro.TextMeshProUGUI)typeof(MainStoryUi).GetField("option_Text", Hidden).GetValue(storyUI);
        Vector2 optionSize = option.GetPreferredValues("체10 민10 지0 매0", option.rectTransform.rect.width, Mathf.Infinity);
        var overflowing = new List<object>();
        foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/Decision_YYS/Resources/Story_Json_Data/Initial/Initial_01/Encounters/guide" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith("/Story.json")) continue;
            var cards = JsonUtility.FromJson<EncounterCardRoot>(AssetDatabase.LoadAssetAtPath<TextAsset>(path).text);
            foreach (var card in cards.MainStory)
            {
                float height = text.GetPreferredValues(card.text, text.rectTransform.rect.width, Mathf.Infinity).y;
                if (height > text.rectTransform.rect.height) overflowing.Add(new { path, card.eventIdStable, card.text, height });
            }
        }
        return new { name = text.name, preferredHeight = preferred.y, availableHeight = text.rectTransform.rect.height,
            fits = preferred.y <= text.rectTransform.rect.height, fontSize = text.fontSize,
            optionPreferredHeight = optionSize.y, optionAvailableHeight = option.rectTransform.rect.height, optionFontSize = option.fontSize,
            optionFits = optionSize.y <= option.rectTransform.rect.height,
            optionWidth = option.rectTransform.rect.width, dialogueWidth = text.rectTransform.rect.width, overflowing };
    }

    public static object Inspect()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var boards = UnityEngine.Object.FindObjectsByType<JourneyBoardReferences>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return new {
            scene = scene.path, dirty = scene.isDirty,
            boards = boards.Select(b => new { name = b.name,
                stacks = b.supplyStacks.Select(s => new { name = s.name, capacity = s.coins.Length,
                    first = s.coins[0].transform.localPosition.ToString("F4"), last = s.coins[s.coins.Length-1].transform.localPosition.ToString("F4"),
                    scale = s.coins[0].transform.localScale.ToString("F4") }).ToArray() }).ToArray(),
            managers = UnityEngine.Object.FindObjectsByType<DecisionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(m => m.name).ToArray(),
            kneel = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/UnityAssets/KnightAsset/KnightAnim/HumanArmature_kneel.anim")?.length,
            errors = UnityEditorInternal.InternalEditorUtility.isApplicationActive
        };
    }
}
