using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Read-only project checks, outside Assets: no scene saves and no player save mutations.
public static class StoryCleanupAudit
{
    public static object OpenSearch()
    {
        const string query = "t:scene";
        var candidates = new[] {
            UnityEditor.Search.SearchService.GetProvider("asset"),
            UnityEditor.Search.SearchService.GetProvider("scene") };
        var preferred = candidates.Where(provider => provider != null).ToArray();
        var context = preferred.Length > 0
            ? new UnityEditor.Search.SearchContext(preferred, query)
            : new UnityEditor.Search.SearchContext(UnityEditor.Search.SearchService.GetActiveProviders(), query);
        UnityEditor.Search.SearchService.ShowWindow(context);
        return new { opened = true, query };
    }

    public static object RemoveUnusedCalculator()
    {
        const string path = "Assets/Decision_YYS/Scripts/Controller/BettingOutcomeCalculator.cs";
        if (!File.Exists(path)) return new { removed = false, alreadyAbsent = true };
        var users = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.EndsWith(".unity") || p.EndsWith(".prefab") || p.EndsWith(".asset"))
            .Where(p => AssetDatabase.GetDependencies(p, true).Contains(path)).ToArray();
        if (users.Length > 0) throw new InvalidOperationException("Calculator asset still referenced: " + string.Join(", ", users));
        var codeUsers = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories)
            .Where(p => p.Replace('\\', '/') != path)
            .Where(p => File.ReadAllText(p).Contains("BettingOutcomeCalculator")).ToArray();
        if (codeUsers.Length > 0) throw new InvalidOperationException("Calculator still called: " + string.Join(", ", codeUsers));
        if (!AssetDatabase.DeleteAsset(path)) throw new InvalidOperationException("Could not delete unused calculator.");
        return new { removed = true, path, recovery = "Git history", assetReferences = users.Length, codeReferences = codeUsers.Length };
    }

    public static object Schema()
    {
        string root = "Assets/Decision_YYS/Resources/Story_Json_Data";
        string[] retired = { "background", "isTransition", "statWeights" };
        var types = new[] { typeof(Dialogue), typeof(EncounterCard) };
        foreach (var type in types)
            foreach (var field in retired)
                Require(type.GetField(field) == null, type.Name + "." + field + " still exists");
        int files = 0, cards = 0;
        foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var json = JObject.Parse(File.ReadAllText(file));
            var story = json["MainStory"] as JArray;
            if (story == null) continue;
            files++;
            foreach (var card in story.OfType<JObject>())
            {
                cards++;
                foreach (var field in retired) Require(card[field] == null, file + ": obsolete " + field);
            }
        }
        // Old save files may still contain retired fields. JsonUtility must ignore those safely.
        var legacy = JsonUtility.FromJson<Dialogue>("{\"type\":\"Choice\",\"text\":\"legacy\",\"background\":\"#ffffff\",\"isTransition\":true,\"statWeights\":[1,1,1,1],\"options\":[\"a\",\"b\",\"c\",\"d\"]}");
        Require(legacy.IsChoice && legacy.text == "legacy" && legacy.options.Length == 4, "Legacy dialogue no longer loads");
        Require(typeof(UiController).GetMethod("ChangeUiImage") == null, "Retired UI wrapper still exists");
        Require(typeof(MainStoryUi).GetMethod("StartSwapStoryScreen") == null, "Retired screen wrapper still exists");
        Require(typeof(DecisionPresentationController).GetMethod("PlayStoryTransition") == null, "Retired transition wrapper still exists");
        Require(typeof(StoryRelayManager).GetMethod("SendPacket", BindingFlags.NonPublic | BindingFlags.Instance) == null, "External send wrapper still exists");
        Require(!File.Exists("Assets/Decision_YYS/Scripts/Controller/BettingOutcomeCalculator.cs"), "Unused calculator still exists");
        Require(!File.Exists("Assets/Decision_YYS/Scripts/Controller/StoryProgressController.cs"), "Unused story-index wrapper still exists");
        Require(!File.Exists("Assets/Decision_YYS/Scripts/Controller/Decision/DecisionInputController.cs"), "Unused gear-input wrapper still exists");
        return new { success = true, storyFiles = files, cards, oldSavesLoad = true, externalSendRemoved = true };
    }

    public static object RemoveUnusedControllers()
    {
        var targets = new Dictionary<string, string[]> {
            { "Assets/Decision_YYS/Scripts/Controller/StoryProgressController.cs", new[] { "StoryProgressController", "StoryAdvanceResult" } },
            { "Assets/Decision_YYS/Scripts/Controller/Decision/DecisionInputController.cs", new[] { "DecisionInputController" } }
        };
        var serialized = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.EndsWith(".unity") || p.EndsWith(".prefab") || p.EndsWith(".asset"))
            .SelectMany(p => AssetDatabase.GetDependencies(p, true)).ToHashSet();
        var sources = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories)
            .ToDictionary(p => p.Replace('\\', '/'), File.ReadAllText);
        // Validate every target before deleting any; scene/component-backed scripts are excluded.
        foreach (var target in targets)
        {
            if (!File.Exists(target.Key)) continue;
            if (serialized.Contains(target.Key)) throw new InvalidOperationException("Referenced script: " + target.Key);
            if (sources.Any(s => s.Key != target.Key && target.Value.Any(token => s.Value.Contains(token))))
                throw new InvalidOperationException("Code still uses " + target.Key);
        }
        var removed = new List<string>();
        foreach (var target in targets.Keys)
            if (File.Exists(target))
            {
                if (!AssetDatabase.DeleteAsset(target)) throw new InvalidOperationException("Could not delete " + target);
                removed.Add(target);
            }
        return new { removed, recovery = "Git history", sceneSaved = false };
    }

    public static object Assets()
    {
        var paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && File.Exists(p)).ToArray();
        var enabledScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var resourceRoots = paths.Where(p => p.Contains("/Resources/")).ToArray();
        var preloaded = PlayerSettings.GetPreloadedAssets().Where(a => a != null).Select(AssetDatabase.GetAssetPath).ToArray();
        var roots = enabledScenes.Concat(resourceRoots).Concat(preloaded).Distinct().ToArray();
        var used = new HashSet<string>(AssetDatabase.GetDependencies(roots, true));
        var scenePaths = paths.Where(p => p.EndsWith(".unity")).ToArray();
        var prefabPaths = paths.Where(p => p.EndsWith(".prefab")).ToArray();
        var targets = new[] {
            "Assets/Scenes/TempDecisionScene.unity", "Assets/Scenes/TempAttackScene.unity",
            "Assets/Scenes/MainMenuTestGameScene.unity", "Assets/Settings/SampleSceneProfile.asset",
            "Assets/Decision_YYS/Scripts/TempDualMiniGame.cs",
            "Assets/Decision_YYS/Scripts/Ai/AIAPIClient.cs",
            "Assets/Decision_YYS/Scripts/Controller/Ui/DecisionMiniGameTestLauncher.cs" };
        var ownerDeps = scenePaths.Concat(prefabPaths).ToDictionary(p => p, p => new HashSet<string>(AssetDatabase.GetDependencies(p, true)));
        var targetRows = targets.Where(File.Exists).Select(p => new {
            path = p, includedByKnownRoots = used.Contains(p), bytes = new FileInfo(p).Length,
            referencedBy = ownerDeps.Where(pair => pair.Key != p && pair.Value.Contains(p)).Select(pair => pair.Key).ToArray()
        }).ToArray();
        var groups = new[] { "Assets/AiAsset/JourneyInterfaceV3/", "Assets/AiAsset/JourneyInterfaceV4Baked/", "Assets/AiAsset/JourneyMapKit/" }
            .Select(prefix => {
                var members = paths.Where(p => p.StartsWith(prefix)).ToArray();
                var linked = members.Where(used.Contains).ToArray();
                return new { folder = prefix, assets = members.Length, bytes = members.Sum(p => new FileInfo(p).Length),
                    includedByKnownRoots = linked.Length, includedBytes = linked.Sum(p => new FileInfo(p).Length),
                    linkedExamples = linked.Take(6).ToArray(),
                    unlinkedPreviewScenes = members.Where(p => p.EndsWith(".unity") && !used.Contains(p)).ToArray() };
            }).ToArray();
        var large = paths.Select(p => new { path = p, bytes = new FileInfo(p).Length, includedByKnownRoots = used.Contains(p) })
            .OrderByDescending(p => p.bytes).Take(10).ToArray();
        int missingScripts = 0;
        var liveTools = new List<object>();
        for (int index = 0; index < SceneManager.sceneCount; index++)
            foreach (var root in SceneManager.GetSceneAt(index).GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                    foreach (var behaviour in transform.GetComponents<MonoBehaviour>())
                        if (behaviour != null && (behaviour is AIAPIClient || behaviour is DecisionMiniGameTestLauncher || behaviour is TempDualMiniGame))
                            liveTools.Add(new { objectName = transform.name, component = behaviour.GetType().Name, enabled = behaviour.enabled, active = transform.gameObject.activeInHierarchy });
                }
        return new { enabledScenes, resourcesRootCount = resourceRoots.Length, targets = targetRows, groups,
            previewScenes = scenePaths.Where(p => !used.Contains(p) && (p.Contains("Preview") || p.Contains("Test") || p.Contains("Temp") || p.Contains("JourneyKit"))).ToArray(),
            largeAssets = large, loadedSceneMissingScripts = missingScripts, liveTools,
            limitation = "Known roots = enabled build scenes, Resources, preloaded assets. Dynamic paths, Addressables, other project settings and future authoring use need separate review. Not a safe-delete list." };
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
