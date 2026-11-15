using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Loading;
using OneWay.Battle3D;

// Run through Unity Pipeline. Never changes Build Settings, saves or teammate scenes.
public static class VerifyLoadingFlow
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static object Inspect()
    {
        return new { playing = Application.isPlaying,
            scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
                var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.isLoaded };
            }).ToArray(),
            buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray() };
    }

    public static object Configure()
    {
        Require(!Application.isPlaying, "Edit mode required.");
        string path = LoadingScreenController.LoadingScenePath;
        var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            var screen = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<LoadingScreenController>(true)).Single();
            Undo.RecordObject(screen, "Enable actual loading");
            screen.previewOnly = false;
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "LoadingScene save failed.");
            return new { scene = path, screen.previewOnly, screen.fillStartDelay, screen.previewDuration, destination = screen.destination.ToString() };
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    public static object CheckContracts()
    {
        Require((int)LoadingDestination.Battle == 0 && (int)LoadingDestination.Decision == 1 && (int)LoadingDestination.MainMenu == 2,
            "Serialized destinations changed.");
        foreach (LoadingDestination target in Enum.GetValues(typeof(LoadingDestination)))
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(LoadingScreenController.ScenePath(target)) != null, "Destination missing: " + target);
        var catalog = AssetDatabase.LoadAssetAtPath<LoadingTipCatalog>("Assets/Loading/LoadingTips.asset");
        Require(catalog != null && catalog.Pick(LoadingDestination.MainMenu) == "", "Menu must not use Decision tips.");
        Require(catalog.Pick(LoadingDestination.Battle).Length > 0, "Battle tips missing.");
        return new { passed = true, destinations = 3, menuTips = "empty", battleTips = catalog.battleTips.Length };
    }

    public static object PreparePlayMode()
    {
        Require(!Application.isPlaying && SceneManager.sceneCount == 1, "One saved edit-mode scene required.");
        var scene = SceneManager.GetActiveScene();
        Require(!scene.isDirty && !string.IsNullOrEmpty(scene.path), "Save your current scene first.");
        SessionState.SetString("LoadingVerification.RestoreScene", scene.path);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        return new { prepared = true, restoreScene = SessionState.GetString("LoadingVerification.RestoreScene", "") };
    }

    public static object Restore()
    {
        Require(!Application.isPlaying, "Stop Play first.");
        string path = SessionState.GetString("LoadingVerification.RestoreScene", "");
        Require(!string.IsNullOrEmpty(path), "Restore path missing.");
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        SessionState.EraseString("LoadingVerification.RestoreScene");
        return Inspect();
    }

    public static async Task<object> RunTransitions()
    {
        Require(Application.isPlaying, "Play mode required.");
        Require(!LoadingScreenController.IsTransitioning, "A loading request is already running.");
        // Runtime-only: prevent real gameplay and story/save processing in target Awake/Start.
        var instanceField = typeof(GameManager).GetField("instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var previousManager = instanceField.GetValue(null);
        var guard = new GameObject("Loading verification state guard");
        guard.SetActive(false);
        var dummyManager = guard.AddComponent<GameManager>();
        UnityEngine.Object.DontDestroyOnLoad(guard);
        instanceField.SetValue(null, dummyManager);
        var samples = new List<object>();
        int screens = 0;
        int fullBars = 0;
        bool queued = false;
        bool staleCallbackRan = false;
        var data = new Battle3DStartData(2, 2, 3, 4, 5);
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (scene, mode) =>
        {
            if (scene.path == LoadingScreenController.LoadingScenePath) { screens++; return; }
            foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
            if (scene.path == LoadingScreenController.ScenePath(LoadingDestination.Decision))
            {
                queued = Battle3DEntry.OpenThroughLoading(data);
                Require(!LoadingScreenController.Open(LoadingDestination.MainMenu, () => staleCallbackRan = true), "Second redirect was accepted.");
            }
        };
        SceneManager.sceneLoaded += onLoaded;
        var oldBackground = Application.runInBackground;
        Application.runInBackground = true;
        try
        {
            Time.timeScale = 0f; // Loading must work when a previous screen left the game paused.
            Require(LoadingScreenController.Open(LoadingDestination.MainMenu), "Menu request rejected.");
            Require(!LoadingScreenController.Open(LoadingDestination.Battle, () => staleCallbackRan = true), "Duplicate request accepted.");
            var menu = await Observe(LoadingDestination.MainMenu, 30);
            samples.Add(menu);
            Require(Time.timeScale == 1f, "Paused time scale was not reset.");
            Require(Cursor.visible && Cursor.lockState == CursorLockMode.None, "Menu cursor is locked.");
            Require(LoadingScreenController.Open(LoadingDestination.Decision), "Decision request rejected.");
            var battle = await Observe(LoadingDestination.Battle, 45);
            samples.Add(battle);
            Require(queued && !staleCallbackRan, "Queue or duplicate handoff failed.");
            Require(Battle3DEntry.TryTake(out var taken) && ReferenceEquals(taken, data), "Battle data did not survive LoadingScene.");
            Require(!Battle3DEntry.TryTake(out _), "Battle data consumed more than once.");
            Require(screens == 3, "Expected Menu, Decision and Battle loading screens.");
            Require(fullBars == 3, "Every destination must be activated after 100%.");
            return new { passed = true, screens, fullBars, queuedRedirect = queued, duplicateRejected = !staleCallbackRan, samples };
        }
        finally
        {
            SceneManager.sceneLoaded -= onLoaded;
            Application.runInBackground = oldBackground;
            Time.timeScale = 1f;
            instanceField.SetValue(null, previousManager);
            UnityEngine.Object.Destroy(guard);
        }

        async Task<object> Observe(LoadingDestination final, int timeoutSeconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var seen = new HashSet<int>();
            var complete = new HashSet<int>();
            var started = new Dictionary<int, float>();
            bool respectedDelay = true;
            while (DateTime.UtcNow < deadline)
            {
                var screen = UnityEngine.Object.FindFirstObjectByType<LoadingScreenController>();
                if (screen != null)
                {
                    int id = screen.GetInstanceID();
                    if (seen.Add(id)) started[id] = Time.realtimeSinceStartup;
                    Require(!screen.IsPreview, "Requested loading became preview.");
                    Require(string.IsNullOrEmpty(screen.LastError), screen.LastError);
                    if (Time.realtimeSinceStartup - started[id] < 4.5f && screen.DisplayedProgress > 0f)
                        respectedDelay = false;
                    if (screen.DisplayedProgress >= 1f && complete.Add(id)) fullBars++;
                }
                if (!LoadingScreenController.IsTransitioning && screen == null && SceneManager.GetActiveScene().path == LoadingScreenController.ScenePath(final))
                {
                    Require(respectedDelay, "Bar filled before the 5-second delay.");
                    Require(UnityEngine.Object.FindFirstObjectByType<LoadingScreenController>() == null, "Overlay leaked after completion.");
                    return new { destination = final.ToString(), respectedDelay, loadingScreens = seen.Count };
                }
                await Task.Delay(16);
            }
            throw new TimeoutException("Loading did not finish: " + final);
        }
    }

    public static async Task<object> RunBattleExit()
    {
        Require(Application.isPlaying && !LoadingScreenController.IsTransitioning, "Idle Play mode required.");
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>(FindObjectsInactive.Include);
        Require(battle != null, "Run after RunTransitions, which leaves a dormant battle scene.");
        var type = typeof(Battle3DSceneController);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Set(string name, object value) => type.GetField(name, flags).SetValue(battle, value);
        void Tick() => type.GetMethod("Update", flags).Invoke(battle, null);
        int finalCalls = 0;
        Set("battleReady", true);
        Set("returnRequested", false);
        Set("returnAt", Time.realtimeSinceStartupAsDouble + 5d);
        Set("finishFinalBattle", new Func<bool, bool>(won => { finalCalls++; return true; }));
        Tick();
        Require(finalCalls == 0 && !LoadingScreenController.IsTransitioning, "Return delay was skipped.");
        Set("returnAt", Time.realtimeSinceStartupAsDouble - 1d);
        Tick();
        Require(finalCalls == 1 && !LoadingScreenController.IsTransitioning, "GameManager-owned final route was overridden.");
        Set("returnRequested", false);
        Set("finishFinalBattle", new Func<bool, bool>(won => { finalCalls++; return false; }));
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (scene, mode) =>
        {
            if (scene.path == LoadingScreenController.ScenePath(LoadingDestination.Decision))
                foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
        };
        SceneManager.sceneLoaded += onLoaded;
        try
        {
            Tick();
            Require(finalCalls == 2 && LoadingScreenController.IsTransitioning, "Standalone battle did not open LoadingScene.");
            Tick();
            Require(finalCalls == 2, "Duplicate exit request.");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (LoadingScreenController.IsTransitioning && DateTime.UtcNow < deadline) await Task.Delay(16);
            Require(!LoadingScreenController.IsTransitioning && SceneManager.GetActiveScene().path == LoadingScreenController.ScenePath(LoadingDestination.Decision),
                "Standalone battle did not reach Decision.");
            return new { passed = true, delayPreserved = true, finalRoutePreserved = true, standaloneRoute = "3DBattle -> Loading -> Decision", duplicateExitRejected = true };
        }
        finally { SceneManager.sceneLoaded -= onLoaded; }
    }
}
