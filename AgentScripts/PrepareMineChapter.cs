using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

public static class PrepareMineChapter
{
    public static object ApplyDuelAndBossPresentation()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().isDirty) throw new Exception("Saved Edit mode required.");
        if (SceneManager.GetActiveScene().name != "DecisionScene") EditorSceneManager.OpenScene("Assets/Scenes/DecisionScene.unity");
        var setup = new SerializedObject(UnityEngine.Object.FindFirstObjectByType<TutorialMiniGameController>());
        var pose = (Transform)setup.FindProperty("knightSpawnPose").objectReferenceValue;
        setup.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        const string playerPath = "Assets/Decision_YYS/Prafabs/People/KnightCharacter.prefab";
        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tags.FindProperty("layers"); int bodyLayer = LayerMask.NameToLayer("PlayerBody");
        if (bodyLayer < 0)
        {
            bodyLayer = Enumerable.Range(8, 24).First(i=>string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue));
            layers.GetArrayElementAtIndex(bodyLayer).stringValue = "PlayerBody"; tags.ApplyModifiedPropertiesWithoutUndo();
        }
        var playerRoot = PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            var player = new SerializedObject(playerRoot.GetComponent<Player>());
            player.FindProperty("dialogueFraming.headroom").floatValue = 0.05f;
            player.FindProperty("duelFraming.headroom").floatValue = 0.05f;
            foreach (var r in playerRoot.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = bodyLayer;
            foreach (var c in playerRoot.GetComponentsInChildren<Camera>(true)) c.cullingMask &= ~(1 << bodyLayer);
            player.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(playerRoot, playerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(playerRoot); }
        const string mapPath = "Assets/Decision_YYS/Prafabs/Terrain/BossRevealPreparedChunk.prefab";
        var root = PrefabUtility.LoadPrefabContents(mapPath);
        try
        {
            var anchor = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "BossAnchor" && t.parent.name == "04_PlacementAnchors");
            var existing = anchor.Find("Boss1Preview");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var wrapper = new GameObject("Boss1Preview"); wrapper.transform.SetParent(anchor, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Boss1/Boss1_AllAnimations.prefab"), wrapper.transform);
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            model.transform.localRotation = Quaternion.Euler(0,180,0);
            var animator = model.GetComponentInChildren<Animator>(); animator.applyRootMotion = false;
            animator.Play("Axe_Breathe_and_Look_Around", 0, 0); animator.Update(0);
            var rs = model.GetComponentsInChildren<Renderer>(true); var bounds = rs[0].bounds;
            foreach (var r in rs.Skip(1)) bounds.Encapsulate(r.bounds);
            model.transform.localScale *= 14f / bounds.size.y;
            var feet = model.GetComponentsInChildren<Transform>().Where(t=>t.name == "LeftFoot" || t.name == "RightFoot").ToArray();
            model.transform.position += Vector3.up * (anchor.position.y + 0.35f - feet.Min(t=>t.position.y));
            var preview = new SerializedObject(wrapper.AddComponent<BossPreviewIdle>());
            preview.FindProperty("animator").objectReferenceValue = animator;
            preview.FindProperty("leftFoot").objectReferenceValue = model.GetComponentsInChildren<Transform>().Single(t=>t.name == "LeftFoot");
            preview.FindProperty("rightFoot").objectReferenceValue = model.GetComponentsInChildren<Transform>().Single(t=>t.name == "RightFoot");
            preview.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, mapPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return new { knightScale = setup.FindProperty("knightScale").floatValue, knightPosition = pose.position.ToString(), bossHeight = 14, model = "Boss1_AllAnimations", states = "Axe_Breathe_and_Look_Around / Sword_Shout" };
    }
    public static object ApplyNpcPackAndMovement()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().isDirty) throw new Exception("Saved Edit mode scene required.");
        if (SceneManager.GetActiveScene().name != "DecisionScene") EditorSceneManager.OpenScene("Assets/Scenes/DecisionScene.unity");
        const string path = "Assets/Decision_YYS/Prafabs/Terrain/MineVillageFirstChunk.prefab";
        string[] ids = { "brook", "moa", "til", "nea_sia" };
        string[][] actors = { new[] { "Brook", "Dor" }, new[] { "Moa", "Pico" }, new[] { "Til", "Bella" }, new[] { "Nea", "Sia" } };
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            for (int i = 0; i < ids.Length; i++)
            {
                foreach (string actor in actors[i])
                {
                    var socket = root.transform.Find("Place_" + ids[i] + "/ActorSocket_" + actor);
                    string modelPath = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/AiAsset/Chunk1StoryNPCPack_Appearance_URP/Prefabs/NPCs" })
                        .Select(AssetDatabase.GUIDToAssetPath).Single(p => p.EndsWith("_" + actor + ".prefab"));
                    while (socket.childCount > 0) UnityEngine.Object.DestroyImmediate(socket.GetChild(0).gameObject);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), socket);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.Euler(0, 180, 0);
                }
                var oldPoints = root.transform.Find("SpawnPoints_" + ids[i]);
                if (oldPoints != null) UnityEngine.Object.DestroyImmediate(oldPoints.gameObject);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var env = UnityEngine.Object.FindFirstObjectByType<EnvController>(FindObjectsInactive.Include);
        var configRoot = GameObject.Find("MineNPCPlacement") ?? new GameObject("MineNPCPlacement");
        configRoot.transform.position = env.TerrainOrigin;
        var config = configRoot.GetComponent<MineNpcPlacement>() ?? configRoot.AddComponent<MineNpcPlacement>();
        var data = new SerializedObject(config); var places = data.FindProperty("places");
        // Keep Inspector edits when reapplying model connections.
        if (places.arraySize == 0)
        {
            places.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                var p = places.GetArrayElementAtIndex(i); float z = 20 + i * 27;
                p.FindPropertyRelative("placeId").stringValue = ids[i];
                p.FindPropertyRelative("label").stringValue = (i + 1) + ": " + string.Join(" / ", actors[i]);
                var points = p.FindPropertyRelative("spawnPoints"); points.arraySize = 5;
                Vector3[] positions = i == 3
                    ? new[] { new Vector3(-2.6f,0,92), new Vector3(2.6f,0,92), new Vector3(-2.6f,0,94.5f), new Vector3(2.6f,0,94.5f), new Vector3(2.6f,0,96) }
                    : new[] { new Vector3(-2.6f,0,z-4), new Vector3(2.6f,0,z-4), new Vector3(-2.6f,0,z), new Vector3(2.6f,0,z), new Vector3(2.6f,0,z+4) };
                for (int j = 0; j < 5; j++) points.GetArrayElementAtIndex(j).vector3Value = positions[j];
                p.FindPropertyRelative("entrance").vector3Value = new Vector3(0,0,z-7.4f);
                p.FindPropertyRelative("exit").vector3Value = new Vector3(0,0,z+7.4f);
                foreach (string field in new[] { "firstActorPoints", "secondActorPoints" })
                {
                    var mask = p.FindPropertyRelative(field); mask.arraySize = 0;
                    if (i != 3) continue;
                    int[] values = field == "firstActorPoints" ? new[] {0,1} : new[] {2,3,4};
                    mask.arraySize = values.Length;
                    for (int j = 0; j < values.Length; j++) mask.GetArrayElementAtIndex(j).intValue = values[j];
                }
            }
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        var environment = new SerializedObject(env); environment.FindProperty("npcPlacement").objectReferenceValue = config;
        environment.ApplyModifiedPropertiesWithoutUndo();
        var manager = new SerializedObject(UnityEngine.Object.FindFirstObjectByType<DecisionManager>(FindObjectsInactive.Include));
        manager.FindProperty("encounterStopDistance").floatValue = 2;
        manager.ApplyModifiedPropertiesWithoutUndo();
        var collectors = new SerializedObject(UnityEngine.Object.FindFirstObjectByType<MineCollectorProcession>(FindObjectsInactive.Include));
        collectors.FindProperty("entranceBuffer").floatValue = 1.5f; collectors.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(env.gameObject.scene); EditorSceneManager.SaveScene(env.gameObject.scene);
        AssetDatabase.SaveAssets(); Selection.activeGameObject = configRoot;
        return new { models = 8, places = config.Places.Length, points = config.Places.Sum(p => p.spawnPoints.Length), distance = 2 };
    }
    public static object ApplyRevision()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().isDirty) throw new Exception("Saved Edit mode scene required.");
        const string prefabPath = "Assets/Decision_YYS/Prafabs/Terrain/MineVillageFirstChunk.prefab";
        const string transformPath = "Assets/Decision_YYS/Resources/Chapters/Chapter_01/Transform.json";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AiAsset/MineChunkModularPack/Prefabs/Assembly/Assembly_MineChunk_120u.prefab");
        var npcAnchors = source.GetComponentsInChildren<Transform>(true).Where(t => t.name == "NPC_Spawn").OrderBy(t => t.position.z).ToArray();
        var entries = source.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Entrance").OrderBy(t => t.position.z).ToArray();
        var exits = source.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Exit").OrderBy(t => t.position.z).ToArray();
        var data = JObject.Parse(File.ReadAllText(transformPath));
        var places = (JArray)data["episodes"]["Chapter_1_01"]["TerrainTransforms"]["places"];
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Vector3[] offsets = { new Vector3(-2.6f, 0, -4), new Vector3(2.6f, 0, -4), new Vector3(-2.6f, 0, 0), new Vector3(2.6f, 0, 0), new Vector3(2.6f, 0, 4) };
            for (int i = 0; i < places.Count; i++)
            {
                string id = (string)places[i]["placeId"];
                var pointsRoot = Child(root.transform, "SpawnPoints_" + id);
                var points = new JArray();
                for (int j = 0; j < offsets.Length; j++)
                {
                    var point = pointsRoot.Find("Point_" + (j + 1));
                    if (point == null)
                    {
                        point = Child(pointsRoot, "Point_" + (j + 1));
                        var saved = places[i]["spawnPoints"]?.ElementAtOrDefault(j);
                        point.position = saved != null ? new Vector3((float)saved["x"], (float)saved["y"], (float)saved["z"]) : npcAnchors[i].parent.parent.position + offsets[j];
                    }
                    var p = root.transform.InverseTransformPoint(point.position);
                    points.Add(new JObject { ["x"] = p.x, ["y"] = p.y, ["z"] = p.z });
                }
                places[i]["spawnPoints"] = points;
                var place = root.transform.Find("Place_" + id);
                var model = place.Find("NPC_Miner");
                if (model != null)
                {
                    model.SetParent(place.Find("ActorSocket_Brook"), false);
                    model.localPosition = Vector3.zero; model.localRotation = Quaternion.Euler(0, 180, 0);
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        File.WriteAllText(transformPath, data.ToString()); AssetDatabase.ImportAsset(transformPath);
        EditorSceneManager.OpenScene("Assets/Scenes/DecisionScene.unity");
        var env = UnityEngine.Object.FindFirstObjectByType<EnvController>(FindObjectsInactive.Include);
        var controllerRoot = GameObject.Find("MineTaxCollectors") ?? new GameObject("MineTaxCollectors");
        controllerRoot.transform.position = env.TerrainOrigin;
        var controller = controllerRoot.GetComponent<MineCollectorProcession>() ?? controllerRoot.AddComponent<MineCollectorProcession>();
        var settings = new SerializedObject(controller); var stops = settings.FindProperty("stops"); stops.arraySize = 4;
        for (int i = 0; i < stops.arraySize; i++)
        {
            var stop = Child(controllerRoot.transform, "Place_" + (i + 1));
            var entry = Child(stop, "Entrance"); entry.localPosition = entries[i].position;
            var exit = Child(stop, "Exit"); exit.localPosition = exits[i].position;
            stops.GetArrayElementAtIndex(i).FindPropertyRelative("entrance").objectReferenceValue = entry;
            stops.GetArrayElementAtIndex(i).FindPropertyRelative("exit").objectReferenceValue = exit;
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(env.gameObject.scene); EditorSceneManager.SaveScene(env.gameObject.scene);
        AssetDatabase.SaveAssets(); ChapterContentCatalog.Reset(); Selection.activeGameObject = controllerRoot;
        return ApplyNpcPackAndMovement();
    }
    private static Transform Child(Transform parent, string name)
    {
        var existing = parent.Find(name); if (existing != null) return existing;
        var child = new GameObject(name).transform; child.SetParent(parent, false); return child;
    }
    public static object Main()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().isDirty) throw new Exception("저장된 Edit 모드에서 실행하세요.");
        const string folder = "Assets/Decision_YYS/Prafabs/Terrain/";
        Directory.CreateDirectory(folder);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AiAsset/MineChunkModularPack/Prefabs/Assembly/Assembly_MineChunk_120u.prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        GameObject terrain;
        try
        {
            // Preserve the nested modular source instead of duplicating every mesh into the game prefab.
            var npcs = instance.transform.Find("04_NPCs"); if (npcs != null) npcs.gameObject.SetActive(false);
            string[] places = { "brook", "moa", "til", "nea_sia" };
            string[][] people = { new[] { "Brook", "Dor" }, new[] { "Moa", "Pico" }, new[] { "Til", "Bella" }, new[] { "Nea", "Sia" } };
            var anchors = instance.GetComponentsInChildren<Transform>(true).Where(t => t.name == "NPC_Spawn").OrderBy(t => t.position.z).ToArray();
            if (anchors.Length != 4) throw new Exception("원본 장소 앵커가 4개여야 합니다.");
            for (int i = 0; i < places.Length; i++)
            {
                var root = new GameObject("Place_" + places[i]); root.transform.SetParent(instance.transform, false);
                root.transform.SetPositionAndRotation(anchors[i].position, Quaternion.Euler(0, 180, 0));
                foreach (string person in people[i]) new GameObject("ActorSocket_" + person).transform.SetParent(root.transform, false);
                if (i == 0)
                {
                    var miner = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AiAsset/MineChunkModularPack/Prefabs/NPCs/NPC_Miner.prefab"), root.transform);
                    miner.transform.localPosition = Vector3.zero; miner.transform.localRotation = Quaternion.Euler(0, 180, 0);
                }
            }
            terrain = PrefabUtility.SaveAsPrefabAsset(instance, folder + "MineVillageFirstChunk.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
        var empty = new GameObject("MineEncounterAnchor");
        GameObject anchor;
        try { anchor = PrefabUtility.SaveAsPrefabAsset(empty, folder + "MineEncounterAnchor.prefab"); }
        finally { UnityEngine.Object.DestroyImmediate(empty); }
        var bossSource = AssetDatabase.LoadAssetAtPath<GameObject>(MineChapterSettings.Load().bossPrefab);
        if (bossSource == null) throw new Exception("지정한 BossRevealMapPack이 없습니다.");
        var bossInstance = (GameObject)PrefabUtility.InstantiatePrefab(bossSource);
        GameObject boss;
        try
        {
            // Original boss pack is centered on its square. Place the entrance at local Z=0.
            var renderers = bossInstance.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            var wrapper = new GameObject("BossRevealPreparedChunk");
            bossInstance.transform.SetParent(wrapper.transform, true);
            bossInstance.transform.position += Vector3.forward * -bounds.min.z;
            boss = PrefabUtility.SaveAsPrefabAsset(wrapper, folder + "BossRevealPreparedChunk.prefab");
            UnityEngine.Object.DestroyImmediate(wrapper);
        }
        finally { if (bossInstance != null) UnityEngine.Object.DestroyImmediate(bossInstance); }
        EditorSceneManager.OpenScene("Assets/Scenes/DecisionScene.unity");
        var env = UnityEngine.Object.FindFirstObjectByType<EnvController>(FindObjectsInactive.Include);
        var serialized = new SerializedObject(env);
        Entry(serialized.FindProperty("groundEntries"), "groundId", "mine_chunk", terrain);
        Entry(serialized.FindProperty("groundEntries"), "groundId", "boss_preview", boss);
        Entry(serialized.FindProperty("terrainPrefabCatalog.characterEntries"), "prefabId", "mine_anchor", anchor);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var manager = UnityEngine.Object.FindFirstObjectByType<DecisionManager>(FindObjectsInactive.Include);
        var managerData = new SerializedObject(manager);
        managerData.FindProperty("mineCollectorPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AiAsset/MineChunkModularPack/Prefabs/NPCs/NPC_Inspector.prefab");
        managerData.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(env.gameObject.scene); EditorSceneManager.SaveScene(env.gameObject.scene);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); ChapterContentCatalog.Reset();
        return ApplyRevision();
    }
    private static void Entry(SerializedProperty list, string field, string id, GameObject prefab)
    {
        int index = -1;
        for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).FindPropertyRelative(field).stringValue == id) index = i;
        if (index < 0) { index = list.arraySize; list.arraySize++; }
        var entry = list.GetArrayElementAtIndex(index); entry.FindPropertyRelative(field).stringValue = id; entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
    }
}
