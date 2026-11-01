using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MainMenuEntryChecks
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private static FieldInfo Singleton => typeof(SaveIOService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
    private static object Store(string folder) => typeof(SaveIOService).GetConstructor(Hidden, null, new[] { typeof(string) }, null).Invoke(new object[] { Path.GetFullPath(folder) });
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static object Regression()
    {
        object previous = Singleton.GetValue(null);
        string folder = "output/MineChapterReview/MenuEntryChecks-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Singleton.SetValue(null, Store(folder));
        try
        {
            ChapterContentCatalog.Reset();
            var omnibus = SaveIOService.Instance.LoadResourceData<OmnibusData>("Omnibus_01");
            int first = omnibus.FirstPlayableChapterIndex;
            Check(omnibus.chapters[first].chapterId == "Chapter_1" && omnibus.chapters.Count == 1, "Only the configured mine remains");
            var save = new SaveManager();
            var progress = save.LoadProgress();
            progress.storedItemIds.Add("existing-item");
            progress.storyEvents.Add(new StoryEventRecord { eventKey = "existing-event" });
            progress.guideProgress.committedStats = new[] { 4, 6, 7, 3 };
            progress.guideProgress.allocating = true;
            progress.activePlaceId = "guide";
            progress.currentPosition = new[] { -200f, 0f, 25f };
            progress.finalBattlePending = true;
            save.EnsureJourneyEntry(omnibus);
            var restored = new SaveManager();
            Check(restored.LoadProgress().chapterIndex == first && restored.LoadPlayerPosition() == null, "Old tutorial save enters mine without stale coordinates");
            Check(restored.LoadProgress().storedItemIds.Contains("existing-item") && restored.LoadProgress().storyEvents.Any(e => e.eventKey == "existing-event"), "Inventory and story history survive migration");
            Check(restored.LoadStats().stats.SequenceEqual(new[] { 4, 6, 7, 3 }), "Existing abilities survive migration");
            Check(!restored.LoadProgress().guideProgress.allocating && !restored.LoadProgress().finalBattlePending, "Tutorial allocation and old tutorial finale do not interrupt mine entry");
            restored.SaveProgressAndPlayerPosition(first, 0, 0, new Vector3(-200, 0, 15), Quaternion.identity);
            restored.LoadProgress().activePlaceId = "brook";
            restored.EnsureJourneyEntry(omnibus);
            Check(restored.LoadPlayerPosition().Value.z == 15 && restored.LoadProgress().activePlaceId == "brook", "Existing mine progress resumes unchanged");
            restored.LoadProgress().chapterIndex = 1;
            restored.LoadProgress().chapterId = null;
            SaveIOService.Instance.SaveRunData(restored.CurrentRun, "Progress", restored.LoadProgress());
            restored = new SaveManager();
            restored.EnsureJourneyEntry(omnibus);
            Check(restored.LoadProgress().chapterIndex == first && restored.LoadPlayerPosition().Value.z == 15 &&
                new SaveManager().LoadProgress().chapterId == "Chapter_1", "Legacy mine index migrates once to stable chapter ID without restarting dialogue");
            Check(new OmnibusData { chapters = omnibus.chapters }.FirstPlayableChapterIndex == 0, "Omitted entry setting preserves legacy default");
            return new { passed = true, checks = 8, isolatedSave = folder };
        }
        finally { Singleton.SetValue(null, previous); ChapterContentCatalog.Reset(); }
    }

    public static string Begin()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().isDirty) throw new Exception("Expected saved Edit mode scene.");
        SessionState.SetString("MineMenuReview.PreviousScene", SceneManager.GetActiveScene().path);
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenuScene.unity");
        string folder = "output/MineChapterReview/MainMenuPlay-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Singleton.SetValue(null, Store(folder));
        SessionState.SetBool("MineReview.PreviousOptionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
        SessionState.SetInt("MineReview.PreviousOptions", (int)EditorSettings.enterPlayModeOptions);
        SessionState.SetBool("MineReview.Active", true);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        EditorApplication.isPlaying = true;
        return folder;
    }

    public static object ClickStart()
    {
        if (!Application.isPlaying || SceneManager.GetActiveScene().name != "MainMenuScene") throw new Exception("Expected playing MainMenuScene.");
        Application.runInBackground = true;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        var button = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Single(b => Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == "OnClickStart"));
        button.onClick.Invoke();
        return new { clicked = button.name, scene = SceneManager.GetActiveScene().name, storage = SaveIOService.Instance.StorageRoot };
    }

    public static object VerifyPlay()
    {
        var manager = UnityEngine.Object.FindFirstObjectByType<DecisionManager>();
        Check(SceneManager.GetActiveScene().name == "DecisionScene" && manager != null && manager.enabled, "Main menu start reaches enabled story manager");
        var session = (DecisionSession)typeof(DecisionManager).GetField("session", Hidden).GetValue(manager);
        Check(session != null && session.ChapterIndex == session.Omnibus.FirstPlayableChapterIndex, "No test injection: production session starts at configured chapter");
        Check(GameObject.Find("Place_brook") != null, "Production start instantiates Brook");
        var report = new { passed = true, entry = session.ScenarioPath,
            state = typeof(DecisionManager).GetField("currentState", Hidden).GetValue(manager).ToString(),
            mainMenuButtonInvoked = true, preseededProgress = false, storage = SaveIOService.Instance.StorageRoot };
        File.WriteAllText("output/MineChapterReview/main-menu-start.json", Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
        return report;
    }
}
