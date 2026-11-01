using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Reflection;

public static class MineChapterReview
{
    private const string Pack = "Assets/AiAsset/MineChunkModularPack/Prefabs/";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static string BeginPlayReview()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().isDirty) throw new Exception("Expected saved Edit mode scene.");
        var singleton = typeof(SaveIOService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        var folder = Path.GetFullPath("output/MineChapterReview/PlaySave-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        singleton.SetValue(null, typeof(SaveIOService).GetConstructor(Hidden, null, new[] { typeof(string) }, null).Invoke(new object[] { folder }));
        var save = new SaveManager();
        save.LoadProgress().guideProgress.committedStats = new[] { 3, 3, 3, 3 };
        var env = UnityEngine.Object.FindFirstObjectByType<EnvController>(FindObjectsInactive.Include);
        // Start at the end of the existing tutorial, before the authored miner anchor.
        var start = env.TerrainOrigin + Vector3.forward * 52;
        save.SaveProgressAndPlayerPosition(1, 0, 0, start, Quaternion.identity);
        SessionState.SetBool("MineReview.PreviousOptionsEnabled", EditorSettings.enterPlayModeOptionsEnabled);
        SessionState.SetInt("MineReview.PreviousOptions", (int)EditorSettings.enterPlayModeOptions);
        SessionState.SetBool("MineReview.Active", true);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorApplication.isPlaying = true;
        return folder;
    }

    public static object State()
    {
        var manager = UnityEngine.Object.FindFirstObjectByType<DecisionManager>(FindObjectsInactive.Include);
        if (manager == null) return new { playing = Application.isPlaying, manager = false };
        var cards = (ScenarioData)typeof(DecisionManager).GetField("activeCards", Hidden).GetValue(manager);
        var index = (int)typeof(DecisionManager).GetField("activeCardIndex", Hidden).GetValue(manager);
        var stats = (StatContainer)typeof(DecisionManager).GetField("statContainer", Hidden).GetValue(manager);
        return new { playing = Application.isPlaying, state = typeof(DecisionManager).GetField("currentState", Hidden).GetValue(manager).ToString(),
            paused = EditorApplication.isPaused, time = Time.time,
            enabled = manager.enabled, active = manager.gameObject.activeInHierarchy,
            hasSession = typeof(DecisionManager).GetField("session", Hidden).GetValue(manager) != null,
            place = typeof(DecisionManager).GetField("activePlaceId", Hidden).GetValue(manager),
            card = index, text = cards?.MainStory?.ElementAtOrDefault(index)?.text, stats = stats.stats,
            storage = SaveIOService.Instance.StorageRoot };
    }

    public static object ResumeReview()
    {
        EditorApplication.isPaused = false;
        Application.runInBackground = true;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        EditorApplication.QueuePlayerLoopUpdate();
        return State();
    }

    public static object Yellow()
    {
        UnityEngine.Object.FindFirstObjectByType<DecisionManager>().OnJourneyYellowPressed();
        return State();
    }

    public static object ActivateStoryReview()
    {
        if (!Application.isPlaying || !SaveIOService.Instance.StorageRoot.Contains("MineChapterReview")) throw new Exception("Expected isolated Play review.");
        var game = GameManager.Instance;
        var settings = new SerializedObject(game);
        settings.FindProperty("playerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("a4effee4f7a9daf4cbb492b8d81afd19"));
        settings.ApplyModifiedPropertiesWithoutUndo();
        var launcher = UnityEngine.Object.FindFirstObjectByType<DecisionMiniGameTestLauncher>();
        if (launcher != null)
        {
            var bar = (GameObject)typeof(DecisionMiniGameTestLauncher).GetField("testBar", Hidden).GetValue(launcher);
            if (bar != null) bar.SetActive(false);
            launcher.enabled = false;
        }
        var manager = UnityEngine.Object.FindFirstObjectByType<DecisionManager>(FindObjectsInactive.Include);
        manager.enabled = true;
        if (typeof(DecisionManager).GetField("session", Hidden).GetValue(manager) == null)
            typeof(DecisionManager).GetMethod("Initialize", Hidden).Invoke(manager, null);
        return "Story review activated with the same player prefab as MainMenuScene.";
    }

    public static object Select(int slot)
    {
        var manager = UnityEngine.Object.FindFirstObjectByType<DecisionManager>();
        typeof(DecisionManager).GetMethod("HandleEncounterGearSelection", Hidden).Invoke(manager, new object[] { slot });
        return State();
    }

    public static string FinishReview()
    {
        if (Application.isPlaying) throw new Exception("Stop Play Mode first.");
        if (!SessionState.GetBool("MineReview.Active", false)) return "No active review.";
        EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt("MineReview.PreviousOptions", 0);
        EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool("MineReview.PreviousOptionsEnabled", false);
        typeof(SaveIOService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        SessionState.SetBool("MineReview.Active", false);
        ChapterContentCatalog.Reset();
        return "Review stopped; normal save storage and Play Mode settings restored.";
    }
    public static string Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/DecisionScene.unity" || scene.isDirty)
            throw new InvalidOperationException("Expected the saved DecisionScene; preserving unsaved edits.");
        Directory.CreateDirectory("output/MineChapterReview");
        string backup = "output/MineChapterReview/DecisionScene.before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
        File.Copy(scene.path, backup, false);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Assembly/Assembly_MineChunk_120u.prefab");
        var originalNpc = source.transform.Find("04_NPCs/NPC_Miner");
        if (originalNpc == null) throw new InvalidOperationException("Missing authored miner anchor.");
        Vector3 position = originalNpc.position;
        Vector3 rotation = originalNpc.eulerAngles;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        GameObject variant;
        const string variantPath = "Assets/Decision_YYS/Prafabs/Terrain/MineVillageFirstChunk.prefab";
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            UnityEngine.Object.DestroyImmediate(instance.transform.Find("04_NPCs").gameObject);
            var placeRoot = new GameObject("Place_brook");
            placeRoot.transform.SetParent(instance.transform, false);
            rotation.y += 180f;
            placeRoot.transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
            var npc = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "NPCs/NPC_Miner.prefab"), placeRoot.transform);
            npc.transform.localPosition = Vector3.zero;
            npc.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(variantPath));
            variant = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
        var env = UnityEngine.Object.FindFirstObjectByType<EnvController>(FindObjectsInactive.Include);
        var serialized = new SerializedObject(env);
        SetEntry(serialized.FindProperty("groundEntries"), "groundId", "mine_chunk", variant);
        SetEntry(serialized.FindProperty("terrainPrefabCatalog.characterEntries"), "prefabId", "mine_miner",
            AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "NPCs/NPC_Miner.prefab"));
        serialized.ApplyModifiedPropertiesWithoutUndo();
        string transformFile = "Assets/Decision_YYS/Resources/Chapters/Chapter_01/Transform.json";
        var data = JObject.Parse(File.ReadAllText(transformFile));
        var place = data["episodes"]["Chapter_1_01"]["TerrainTransforms"]["places"][0];
        place["position"] = JObject.FromObject(new { x = position.x, y = position.y, z = position.z });
        place["rotation"] = JObject.FromObject(new { x = rotation.x, y = rotation.y, z = rotation.z });
        File.WriteAllText(transformFile, data.ToString());
        AssetDatabase.Refresh();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        ChapterContentCatalog.Reset();
        return Newtonsoft.Json.JsonConvert.SerializeObject(new { variantPath, backup, brook = position.ToString(), originalPackUnchanged = true });
    }

    public static string FixFacingInReview()
    {
        if (!Application.isPlaying) throw new Exception("Expected isolated Play review.");
        var model = GameObject.Find("Place_brook").transform;
        var wrapper = new GameObject("Place_brook").transform;
        wrapper.SetParent(model.parent, false);
        wrapper.SetPositionAndRotation(model.position, model.rotation);
        model.name = "NPC_Miner";
        model.SetParent(wrapper, false);
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.Euler(0, 180f, 0);
        return "Model forward convention corrected through a prefab wrapper.";
    }

    private static void SetEntry(SerializedProperty list, string idField, string id, GameObject prefab)
    {
        int index = -1;
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).FindPropertyRelative(idField).stringValue == id) index = i;
        if (index < 0) { index = list.arraySize; list.arraySize++; }
        var entry = list.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative(idField).stringValue = id;
        entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
    }
    public static string Inspect()
    {
        var scene = SceneManager.GetActiveScene();
        var env = UnityEngine.Object.FindFirstObjectByType<EnvController>(FindObjectsInactive.Include);
        var settings = env == null ? null : new SerializedObject(env);
        var paths = new[] {
            "Assets/AiAsset/MineChunkModularPack/Prefabs/Assembly/Assembly_MineChunk_120u.prefab",
            "Assets/AiAsset/MineChunkModularPack/Prefabs/Environment/Environment_MineChunk_50x120.prefab",
            "Assets/AiAsset/MineChunkModularPack/Prefabs/NPCs/NPC_Miner.prefab" };
        return Newtonsoft.Json.JsonConvert.SerializeObject(new {
            scene = scene.path, dirty = scene.isDirty,
            chunkSize = settings?.FindProperty("chunkSize").floatValue,
            roots = scene.GetRootGameObjects().Select(x => x.name).ToArray(),
            prefabs = paths.Select(p => {
                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                return new { path = p, exists = obj != null,
                    children = obj == null ? null : obj.transform.Cast<Transform>().Select(t => new { t.name, position = t.localPosition.ToString() }).ToArray() };
            }).ToArray()
        });
    }

    public static string InspectMine()
    {
        var obj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AiAsset/MineChunkModularPack/Prefabs/Assembly/Assembly_MineChunk_120u.prefab");
        return Newtonsoft.Json.JsonConvert.SerializeObject(obj.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.Contains("NPC") || t.name.Contains("Miner") || t.name.Contains("Anchor"))
            .Select(t => new { t.name, position = t.position.ToString(), rotation = t.eulerAngles.ToString() }).ToArray());
    }
}
