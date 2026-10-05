using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using JourneyMapKit;

// Run through Unity Pipeline; never rewrites scene YAML or the source V3 prefab.
public static class JourneyV3Migration
{
    const string ScenePath = "Assets/Scenes/DecisionScene.unity";
    const string V3 = "Assets/AiAsset/JourneyInterfaceV3/";
    const string Generated = "Assets/Decision_YYS/JourneyV3Integration";
    static readonly string[] Colors = { "Blue", "Red", "Yellow", "Teal" };
    public class Geometry { public Node[] objects; }
    public class Node
    {
        public string name, parent, kind, material;
        public float[] vertices, normals, uv;
        public int[] indices;
    }
    static Scene Decision()
    {
        var scene = SceneManager.GetSceneByPath(ScenePath);
        return scene.IsValid() && scene.isLoaded ? scene :
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
    }
    static Transform Find(Scene scene, string path)
    {
        var parts = path.Split('/');
        var root = scene.GetRootGameObjects().FirstOrDefault(o => o.name == parts[0]);
        var result = root == null ? null : parts.Length == 1 ? root.transform :
            root.transform.Find(string.Join("/", parts.Skip(1)));
        if (result == null) throw new Exception("Missing scene object: " + path);
        return result;
    }
    static T Asset<T>(string path) where T : UnityEngine.Object
    {
        var value = AssetDatabase.LoadAssetAtPath<T>(path);
        if (value == null) throw new Exception("Missing asset: " + path);
        return value;
    }
    static Transform Child(Transform root, string path)
    {
        var value = root.Find(path);
        if (value == null) throw new Exception("Missing V3 object: " + path);
        return value;
    }
    static IEnumerable<Transform> All(Scene s) => s.GetRootGameObjects()
        .SelectMany(o => o.GetComponentsInChildren<Transform>(true));
    static Geometry SourceGeometry() => JsonConvert.DeserializeObject<Geometry>(
        File.ReadAllText("AssetBuild/JourneyInterfaceV2BuildProject/BoardGeometryV3.json"));
    public static object Inspect()
    {
        var s = Decision();
        var assembly = Asset<GameObject>(V3 + "Prefabs/JourneyBoardAssembly.prefab");
        var nodes = SourceGeometry().objects.Where(n => n.parent == "Canister_Blue" && n.kind != "empty").ToArray();
        return new {
            scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
                var a = SceneManager.GetSceneAt(i); return new { a.path, a.isDirty }; }).ToArray(),
            assemblyPosition = assembly.transform.position.ToString("F4"),
            coinParts = nodes.Select(n => new { n.name, n.material, center = MakeMesh(n).bounds.center.ToString("F4") }).ToArray(),
            objects = All(s).Where(t => t.name.Contains("Canvas") || t.name.Contains("Journey") ||
                t.name == "FantasyBackpack" || t.name.Contains("SpawnPoint")).Select(t => new {
                    t.name, pos = t.position.ToString("F4"), components = t.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name).ToArray()
                }).ToArray()
        };
    }
    static Mesh MakeMesh(Node n)
    {
        var mesh = new Mesh { name = n.name };
        mesh.vertices = Enumerable.Range(0, n.vertices.Length / 3)
            .Select(i => new Vector3(n.vertices[i*3], n.vertices[i*3+1], n.vertices[i*3+2])).ToArray();
        var normals = n.normals == null ? null : Enumerable.Range(0, n.normals.Length / 3)
            .Select(i => new Vector3(n.normals[i*3], n.normals[i*3+1], n.normals[i*3+2])).ToArray();
        if (normals != null) mesh.normals = normals;
        if (n.uv != null) mesh.uv = Enumerable.Range(0, n.uv.Length / 2)
            .Select(i => new Vector2(n.uv[i*2], n.uv[i*2+1])).ToArray();
        var triangles = (int[])n.indices.Clone();
        var vertices = mesh.vertices;
        if (normals != null) for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i+1], c = triangles[i+2];
            if (Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a], vertices[c]-vertices[a]),
                normals[a]+normals[b]+normals[c]) < 0)
            { triangles[i+1] = c; triangles[i+2] = b; }
        }
        mesh.triangles = triangles;
        if (normals == null) mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
    class Prepared
    {
        public string color;
        public Mesh housing, stock;
        public Material[] housingMaterials, stockMaterials;
        public Node[] discs;
        public Vector3[] centers;
    }
    static Mesh Combine(Node[] nodes, Vector3 offset, string name, out Material[] materials)
    {
        var groups = nodes.GroupBy(n => n.material).ToArray();
        materials = groups.Select(g => Asset<Material>(V3 + "Materials/" + g.Key + ".mat")).ToArray();
        var temporary = new List<Mesh>();
        try
        {
            var submeshes = new List<CombineInstance>();
            foreach (var group in groups)
            {
                var parts = group.Select(n => { var m = MakeMesh(n); temporary.Add(m);
                    return new CombineInstance { mesh = m, transform = Matrix4x4.Translate(offset) }; }).ToArray();
                var sub = new Mesh(); temporary.Add(sub);
                sub.CombineMeshes(parts, true, true);
                submeshes.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
            }
            var result = new Mesh { name = name };
            result.CombineMeshes(submeshes.ToArray(), false, true);
            return result;
        }
        finally { foreach (var m in temporary) UnityEngine.Object.DestroyImmediate(m); }
    }
    static Prepared[] Prepare()
    {
        var source = SourceGeometry();
        return Colors.Select(color => {
            var nodes = source.objects.Where(n => n.parent == "Canister_" + color && n.kind != "empty").ToArray();
            var discs = nodes.Where(n => n.name.StartsWith("StoredCoin", StringComparison.Ordinal)).ToArray();
            if (discs.Length != 14) throw new Exception("Expected 14 stock discs: " + color);
            var centers = discs.Select(n => { var m = MakeMesh(n); var c = m.bounds.center;
                UnityEngine.Object.DestroyImmediate(m); return c; }).ToArray();
            var coinNodes = nodes.Where(n => n.name.StartsWith("StoredCoin", StringComparison.Ordinal) ||
                n.name.StartsWith("CoinRim", StringComparison.Ordinal)).ToArray();
            // Rims sit 0.03 units above their disc center; pair by the exported part suffix.
            var pair = coinNodes.Where(n => n.name == discs[0].name ||
                n.name == discs[0].name.Replace("StoredCoin", "CoinRim")).ToArray();
            if (pair.Length != 2) throw new Exception("Expected disc/rim pair: " + color + " found " + pair.Length);
            var p = new Prepared { color = color, discs = discs, centers = centers };
            p.housing = Combine(nodes.Except(coinNodes).ToArray(), Vector3.zero, color + "Housing", out p.housingMaterials);
            p.stock = Combine(pair, -centers[0], color + "StockCoin", out p.stockMaterials);
            return p;
        }).ToArray();
    }
    static void Bind(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        Undo.RecordObject(target, "Journey V3 reference");
        var so = new SerializedObject(target);
        var property = so.FindProperty(field);
        if (property == null) throw new Exception("Missing field: " + field);
        property.objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
    static string Pose(Transform t) => t.position.ToString("F6") + t.rotation.ToString("F6") + t.lossyScale.ToString("F6");
    static Dictionary<Transform,string> Protected(Scene s) => All(s).Where(t =>
        t.name.Contains("Pose") || t.name.Contains("SpawnPoint") || t.name == "KnightCharacter_Copy" ||
        t.name == "HandRoot" || t.name == "NpcHandRoot" || t.name == "DuelAuthoring_World")
        .ToDictionary(t => t, Pose);
    public static object Migrate()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play before migration");
        var s = Decision();
        if (All(s).Any(t => t.name == "JourneyInterfaceV3_Runtime")) throw new Exception("V3 already installed; refusing duplicate migration");
        if (AssetDatabase.IsValidFolder(Generated)) throw new Exception("Generated folder already exists; refusing overwrite");
        var prefab = Asset<GameObject>(V3 + "Prefabs/JourneyBoardAssembly.prefab");
        var overlay = Asset<GameObject>(V3 + "Prefabs/UIOverlay_1920x1080.prefab");
        var coins = Colors.Select(c => Asset<GameObject>(V3 + "Prefabs/JourneyCoin_" + c + ".prefab")).ToArray();
        var prepared = Prepare(); // All source geometry/material validation before any scene mutation.
        var protectedPoses = Protected(s);
        string backup = "Temp/JourneyV3Migration/DecisionBefore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
        Directory.CreateDirectory("Temp/JourneyV3Migration");
        if (!EditorSceneManager.SaveScene(s, backup, true)) throw new Exception("Backup failed");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace Decision interface with Journey V3");
        bool ownsFolder = false;
        try
        {
            AssetDatabase.CreateFolder("Assets/Decision_YYS", "JourneyV3Integration"); ownsFolder = true;
            var parent = Find(s, "_Gameplay3D/3D_UI");
            var board = (GameObject)PrefabUtility.InstantiatePrefab(prefab, s);
            Undo.RegisterCreatedObjectUndo(board, "V3 board");
            PrefabUtility.UnpackPrefabInstance(board, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            board.name = "JourneyInterfaceV3_Runtime";
            board.transform.SetParent(parent, true);
            var stacks = new List<JourneyCoinStack>();
            foreach (var p in prepared)
            {
                AssetDatabase.CreateAsset(p.housing, Generated + "/" + p.color + "Housing.asset");
                AssetDatabase.CreateAsset(p.stock, Generated + "/" + p.color + "StockCoin.asset");
                var canister = Child(board.transform, "CoinSupplyRack/Canister_" + p.color);
                var visual = Child(canister, "Visuals");
                visual.GetComponent<MeshFilter>().sharedMesh = p.housing;
                visual.GetComponent<MeshRenderer>().sharedMaterials = p.housingMaterials;
                var stock = new List<GameObject>();
                foreach (int index in Enumerable.Range(0, p.discs.Length).OrderBy(i => p.centers[i].y))
                {
                    var coin = Child(canister, p.discs[index].name);
                    coin.localPosition = p.centers[index];
                    Undo.AddComponent<MeshFilter>(coin.gameObject).sharedMesh = p.stock;
                    Undo.AddComponent<MeshRenderer>(coin.gameObject).sharedMaterials = p.stockMaterials;
                    stock.Add(coin.gameObject);
                }
                var stack = Undo.AddComponent<JourneyCoinStack>(canister.gameObject);
                stack.coins = stock.ToArray(); stack.SetCount(10); stacks.Add(stack);
            }
            var references = Find(s, "_Gameplay3D/3D_UI/JourneyController").GetComponent<JourneyBoardReferences>();
            Undo.RecordObject(references, "Rebind Journey controls");
            references.gear = Child(board.transform, "GearPivot");
            references.gearHandleCollider = Child(board.transform, "GearPivot/GearHandle").GetComponent<SphereCollider>();
            references.yellowButton = Child(board.transform, "YellowButton/YellowButtonCap").GetComponent<Collider>();
            references.chuteEntry = Child(board.transform, "TransferEntry");
            references.chuteExit = Child(board.transform, "TransferExit");
            references.traySpawn = references.chuteEntry;
            references.trayFloor = Child(board.transform, "CollectionTray/TrayPaperCenter").GetComponent<Collider>();
            references.supplyStacks = stacks.ToArray();
            references.supplyButtons = Colors.Select(c => Child(board.transform, "CoinSupplyRack/Canister_"+c+"/SupplyButton_"+c)
                .GetComponentInChildren<Collider>(true)).ToArray();
            references.supplySpawns = Colors.Select(c => Child(board.transform, "CoinSupplyRack/Canister_"+c+"/CoinSpawn_"+c)).ToArray();
            references.coinEntrances = references.supplySpawns;
            PrefabUtility.RecordPrefabInstancePropertyModifications(references);
            var manager = Find(s, "_Systems/Decision_Manager").GetComponent<DecisionManager>();
            Undo.RecordObject(manager, "V3 coin prefabs");
            var serializedManager = new SerializedObject(manager);
            var coinArray = serializedManager.FindProperty("journeyCoinPrefabs"); coinArray.arraySize = 4;
            for (int i = 0; i < 4; i++) coinArray.GetArrayElementAtIndex(i).objectReferenceValue = coins[i];
            serializedManager.ApplyModifiedProperties();
            var inventory = Find(s, "_Gameplay3D/3D_UI/FantasyBackpack").GetComponent<BackpackInventoryController>();
            var bag = Child(board.transform, "BackpackMount/JourneyBackpack");
            Bind(inventory, "backpackAnimator", null);
            Bind(inventory, "backpackAnimation", bag.GetComponent<Animation>());
            Bind(inventory, "backpackCollider", Child(bag, "Body/BackpackCanvasBody").GetComponent<Collider>());
            Bind(inventory, "boardDropArea", Child(board.transform, "GridBoard/GridFloor").GetComponent<Collider>());
            var serializedInventory = new SerializedObject(inventory);
            serializedInventory.FindProperty("animationDuration").floatValue = .45f;
            serializedInventory.ApplyModifiedProperties();
            var oldAnimator = inventory.GetComponent<Animator>();
            if (oldAnimator != null) { Undo.RecordObject(oldAnimator, "Disable old bag Animator"); oldAnimator.enabled = false; }
            foreach (string name in new[] { "JourneyCoinSupplyRack", "JourneyController", "JourneyGridBoard", "JourneyCoinChute", "FantasyBackpack" })
            {
                var old = Find(s, "_Gameplay3D/3D_UI/" + name);
                foreach (var renderer in old.GetComponentsInChildren<Renderer>(true))
                {
                    if (InDuelAuthoring(renderer.transform, old)) continue;
                    Undo.RecordObject(renderer, "Hide replaced graphics"); renderer.enabled = false;
                }
                foreach (var collider in old.GetComponentsInChildren<Collider>(true))
                {
                    if (InDuelAuthoring(collider.transform, old)) continue;
                    Undo.RecordObject(collider, "Disable replaced colliders"); collider.enabled = false;
                }
            }
            var canvas = Find(s, "_UI/Decision_Canvas").GetComponent<Canvas>();
            Undo.RecordObject(canvas, "UI behind 3D panel"); canvas.planeDistance = 100f;
            var uiRoot = Find(s, "_UI/Decision_Canvas/Decision_UI_Root");
            var frames = (GameObject)PrefabUtility.InstantiatePrefab(overlay, s);
            Undo.RegisterCreatedObjectUndo(frames, "V3 UI frames");
            frames.name = "JourneyInterfaceV3_Frames";
            frames.transform.SetParent(uiRoot, false); frames.transform.SetAsLastSibling();
            foreach (var graphic in frames.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (string path in new[] { "", "/Encounter_Display", "/Encounter_Display/Event_View/Walking_View_Back", "/Face_Display" })
            {
                var t = path.Length == 0 ? uiRoot : Find(s, "_UI/Decision_Canvas/Decision_UI_Root" + path);
                var image = t.GetComponent<Image>(); if (image == null) continue;
                Undo.RecordObject(image, "Replace ornate background"); image.sprite = null;
                image.color = path.Length == 0 ? new Color(.956f,.939f,.866f,1f) : Color.clear;
                image.raycastTarget = false;
            }
            foreach (var pair in protectedPoses) if (Pose(pair.Key) != pair.Value)
                throw new Exception("Protected duel transform changed: " + pair.Key.name);
            if (references.supplyButtons.Any(c => c == null) || references.gearHandleCollider == null || references.yellowButton == null)
                throw new Exception("Control collider binding incomplete");
            AssetDatabase.SaveAssets();
            PersistBindings(); // Native prefab overrides and imported lid-clip references must survive Play/reload.
            EditorSceneManager.MarkSceneDirty(s);
            if (!EditorSceneManager.SaveScene(s)) throw new Exception("Decision save failed");
            Undo.CollapseUndoOperations(group);
            return new { saved = s.path, backup, protectedTransforms = protectedPoses.Count,
                board = board.transform.position.ToString("F4"), uiPlaneDistance = canvas.planeDistance,
                stocks = stacks.Select(a => a.coins.Length).ToArray(), generatedMeshes = 8 };
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            if (ownsFolder) AssetDatabase.DeleteAsset(Generated);
            throw;
        }
    }
    static bool InDuelAuthoring(Transform t, Transform stop)
    {
        while (t != null && t != stop) { if (t.name == "DuelAuthoring_World") return true; t = t.parent; }
        return false;
    }
    public static object PersistBindings()
    {
        var s = Decision();
        var board = Find(s,"_Gameplay3D/3D_UI/JourneyInterfaceV3_Runtime");
        var refs = Find(s,"_Gameplay3D/3D_UI/JourneyController").GetComponent<JourneyBoardReferences>();
        Undo.RecordObject(refs,"Persist V3 prefab bindings");
        refs.gear = Child(board,"GearPivot");
        refs.gearHandleCollider = Child(board,"GearPivot/GearHandle").GetComponent<SphereCollider>();
        refs.yellowButton = Child(board,"YellowButton/YellowButtonCap").GetComponent<Collider>();
        refs.chuteEntry = Child(board,"TransferEntry"); refs.chuteExit = Child(board,"TransferExit");
        refs.traySpawn = refs.chuteEntry;
        refs.trayFloor = Child(board,"CollectionTray/TrayPaperCenter").GetComponent<Collider>();
        refs.supplyStacks = Colors.Select(c => Child(board,"CoinSupplyRack/Canister_"+c).GetComponent<JourneyCoinStack>()).ToArray();
        refs.supplyButtons = Colors.Select(c => Child(board,"CoinSupplyRack/Canister_"+c+"/SupplyButton_"+c).GetComponentInChildren<Collider>(true)).ToArray();
        refs.supplySpawns = Colors.Select(c => Child(board,"CoinSupplyRack/Canister_"+c+"/CoinSpawn_"+c)).ToArray();
        refs.coinEntrances = refs.supplySpawns;
        PrefabUtility.RecordPrefabInstancePropertyModifications(refs);
        EditorUtility.SetDirty(refs);
        foreach (string name in new[] {"JourneyCoinSupplyRack","JourneyController","JourneyGridBoard","JourneyCoinChute","FantasyBackpack"})
        {
            var old = Find(s,"_Gameplay3D/3D_UI/"+name);
            foreach (var r in old.GetComponentsInChildren<Renderer>(true))
            {
                if(InDuelAuthoring(r.transform,old)) continue;
                Undo.RecordObject(r,"V3 hide legacy graphics"); r.enabled=false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
            foreach (var c in old.GetComponentsInChildren<Collider>(true))
            {
                if(InDuelAuthoring(c.transform,old)) continue;
                Undo.RecordObject(c,"V3 hide legacy colliders"); c.enabled=false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            }
        }
        var inventory = Find(s,"_Gameplay3D/3D_UI/FantasyBackpack").GetComponent<BackpackInventoryController>();
        var bagAnimation=Child(board,"BackpackMount/JourneyBackpack").GetComponent<Animation>();
        Undo.RecordObject(bagAnimation,"Connect exported V3 lid clips");
        AnimationUtility.SetAnimationClips(bagAnimation,new[]{
            Asset<AnimationClip>(V3+"Meshes/Backpack_OpenLid.anim"),
            Asset<AnimationClip>(V3+"Meshes/Backpack_CloseLid.anim")});
        var animator = inventory.GetComponent<Animator>();
        if(animator != null) { Undo.RecordObject(animator,"V3 bag animation"); animator.enabled=false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator); }
        PrefabUtility.RecordPrefabInstancePropertyModifications(inventory);
        var uiRoot=Find(s,"_UI/Decision_Canvas/Decision_UI_Root");
        foreach(string path in new[]{"","/Encounter_Display","/Encounter_Display/Event_View/Walking_View_Back","/Face_Display"})
        {
            var image=(path.Length==0?uiRoot:Find(s,"_UI/Decision_Canvas/Decision_UI_Root"+path)).GetComponent<Image>();
            if(image==null)continue;
            Undo.RecordObject(image,"V3 clear old UI frames"); image.sprite=null;
            image.color=path.Length==0?new Color(.956f,.939f,.866f,1f):Color.clear; image.raycastTarget=false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(image);
        }
        // Save only the requested Decision scene, never the unsaved preview scene.
        EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s);
        return Validate();
    }
    public static object Validate()
    {
        var s = Decision();
        var board = Find(s, "_Gameplay3D/3D_UI/JourneyInterfaceV3_Runtime");
        var input = Find(s, "_Gameplay3D/3D_UI/JourneyController").GetComponent<JourneyBoardInput>();
        var refs = input.board;
        var camera = Find(s, "_World/Main Camera").GetComponent<Camera>();
        Physics.SyncTransforms();
        var controls = new[] { refs.yellowButton, refs.gearHandleCollider }.Concat(refs.supplyButtons).ToArray();
        var inventory = new SerializedObject(Find(s, "_Gameplay3D/3D_UI/FantasyBackpack").GetComponent<BackpackInventoryController>());
        return new {
            controlHits = controls.Select(c => {
                var pixel = camera.WorldToScreenPoint(c.bounds.center);
                return new { c.name, enabled = c.enabled && c.gameObject.activeInHierarchy,
                    hit = Physics.RaycastAll(camera.ScreenPointToRay(pixel),1000f,camera.cullingMask).Any(h => h.collider == c),
                    pixel = pixel.ToString("F2") };
            }).ToArray(),
            callbackYellow = input.onYellowPressed.GetPersistentEventCount(),
            callbackGear = input.onGearSelected.GetPersistentEventCount(),
            stockCounts = refs.supplyStacks.Select(stack => stack.coins.Count(c => c.activeSelf)).ToArray(),
            stockRenderers = refs.supplyStacks.Select(stack => stack.coins.All(c => c.GetComponent<MeshRenderer>() != null)).ToArray(),
            uiDepth = Find(s, "_UI/Decision_Canvas").GetComponent<Canvas>().planeDistance,
            inventoryBag = inventory.FindProperty("backpackAnimation").objectReferenceValue != null,
            inventoryUi = inventory.FindProperty("inventoryPanel").objectReferenceValue != null,
            missingScripts = s.GetRootGameObjects().Sum(o => o.GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))),
            eventSystems = All(s).Count(t => t.GetComponent<UnityEngine.EventSystems.EventSystem>() != null),
            frameRaycasts = Find(s, "_UI/Decision_Canvas/Decision_UI_Root/JourneyInterfaceV3_Frames")
                .GetComponentsInChildren<Graphic>(true).Any(g => g.raycastTarget),
            oldEnabledRenderers = new[] {"JourneyCoinSupplyRack","JourneyController","JourneyGridBoard","JourneyCoinChute","FantasyBackpack"}
                .Sum(n => { var root = Find(s,"_Gameplay3D/3D_UI/"+n); return root.GetComponentsInChildren<Renderer>(true)
                    .Count(r => r.enabled && !InDuelAuthoring(r.transform,root)); }),
            newRenderers = board.GetComponentsInChildren<Renderer>(true).Length
        };
    }
    public static object IsolateView()
    {
        var s = Decision();
        if (!string.IsNullOrEmpty(SessionState.GetString("JourneyV3PreviewRoots", ""))) throw new Exception("View already isolated");
        var otherRoots = Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .Where(a => a != s).SelectMany(a => a.GetRootGameObjects()).ToArray();
        SessionState.SetString("JourneyV3PreviewRoots",JsonConvert.SerializeObject(otherRoots.Select(o =>
            new { id = o.GetInstanceID(), active = o.activeSelf }).ToArray()));
        SessionState.SetInt("JourneyV3PreviousScene",SceneManager.GetActiveScene().handle);
        foreach (var root in otherRoots) root.SetActive(false);
        SceneManager.SetActiveScene(s);
        return new { isolated = s.path, hiddenPreviewRoots = otherRoots.Length };
    }
    public static object RestoreView()
    {
        var states = Newtonsoft.Json.Linq.JArray.Parse(SessionState.GetString("JourneyV3PreviewRoots", "[]"));
        foreach (var state in states)
        {
            var root = EditorUtility.InstanceIDToObject((int)state["id"]) as GameObject;
            if (root != null) root.SetActive((bool)state["active"]);
        }
        int previous = SessionState.GetInt("JourneyV3PreviousScene",0);
        var old = Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).FirstOrDefault(s => s.handle == previous);
        if (old.IsValid()) SceneManager.SetActiveScene(old);
        SessionState.EraseString("JourneyV3PreviewRoots");
        return "Preview root states restored without saving preview edits";
    }
}
