using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonMapAuthoringCheck
{
    public static string AssetPacks()
    {
        const string dungeon = "Assets/UnityAssets/Buildings_And_Terrains/Dungeon_FBX format";
        const string custom = "Assets/Decision_YYS/Prafabs";
        var settings = DungeonMapAssetPackSettings.instance;
        string guid = AssetDatabase.AssetPathToGUID(custom);
        var previous = settings.entries.Find(p => p.folderGuid == guid);
        string oldName = previous?.displayName;
        string oldSelection = AssetDatabase.GUIDToAssetPath(settings.selectedFolderGuid);
        int checks = 0;
        try
        {
            Check(DungeonMapAuthoringWindow.DiscoverPacks().Exists(p => p.FolderPath == dungeon), "Dungeon auto discovery", ref checks);
            Check(!settings.RegisterFolder("Assets/NoSuchMapPack", "Invalid"), "Invalid folder rejected", ref checks);
            Check(!settings.RegisterFolder(custom, "  "), "Empty label rejected", ref checks);
            Check(settings.RegisterFolder(custom, "Regression Pack"), "Register external folder", ref checks);
            Check(DungeonMapAuthoringWindow.DiscoverPacks().Exists(p => p.folderGuid == guid && p.displayName == "Regression Pack"), "Custom pack discovered", ref checks);
            settings.RegisterFolder(custom, "Renamed Pack");
            Check(DungeonMapAuthoringWindow.DiscoverPacks().FindAll(p => p.folderGuid == guid).Count == 1, "Rename does not duplicate", ref checks);
            Check(DungeonMapAuthoringWindow.DiscoverPacks().Exists(p => p.folderGuid == guid && p.displayName == "Renamed Pack"), "Updated name shown", ref checks);
            settings.SelectFolder(custom);
            Check(settings.selectedFolderGuid == guid, "Selection stored as stable GUID", ref checks);
            settings.UnregisterFolder(custom);
            Check(!DungeonMapAuthoringWindow.DiscoverPacks().Exists(p => p.folderGuid == guid), "Unregister removes custom entry", ref checks);
            return $"PASS: {checks} asset pack checks. Existing registration restored; script reload requested to clear temporary assemblies.";
        }
        finally
        {
            settings.UnregisterFolder(custom);
            if (oldName != null) settings.RegisterFolder(custom, oldName);
            settings.SelectFolder(oldSelection);
            EditorApplication.delayCall += EditorUtility.RequestScriptReload;
        }
    }

    public static string BeginDragCheck()
    {
        int original = SceneManager.GetActiveScene().handle;
        Scene sandbox = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        sandbox.name = "DungeonMapAuthoring_DragCheck";
        GameObject chunk = DungeonMapAuthoringWindow.CreateChunk(new Vector2(50, 120), 2.8f, Vector3.zero);
        Grid grid = DungeonMapAuthoringWindow.EnsureLayer(chunk, 1, 4, "FloorTiles");
        DungeonMapAuthoringWindow.Open();
        var window = EditorWindow.GetWindow<DungeonMapAuthoringWindow>();
        var so = new SerializedObject(window);
        so.FindProperty("chunk").objectReferenceValue = chunk;
        so.FindProperty("targetGrid").objectReferenceValue = grid;
        so.FindProperty("selectedPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnityAssets/Buildings_And_Terrains/Dungeon_FBX format/floor.fbx");
        so.ApplyModifiedPropertiesWithoutUndo();
        typeof(DungeonMapAuthoringWindow).GetField("painting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(window, true);
        window.CreateGUI();
        SceneView view = SceneView.lastActiveSceneView;
        view.LookAt(new Vector3(0, 0, 0), Quaternion.Euler(90, 0, 0), 70, true, true);
        view.Repaint();
        return $"Temporary drag scene ready. Restore scene handle: {original}";
    }

    public static string EndDragCheck(int originalHandle)
    {
        Scene sandbox = SceneManager.GetSceneByName("DungeonMapAuthoring_DragCheck");
        int count = 0;
        try
        {
            foreach (GameObject root in sandbox.GetRootGameObjects())
            {
                Transform tiles = root.transform.Find("DungeonAuthoringGrid/Floor_01/FloorTiles");
                if (tiles != null) count += tiles.childCount;
            }
            if (count < 2) throw new InvalidOperationException($"Drag placed only {count} objects; expected a continuous stroke.");
            Undo.PerformUndo();
            foreach (GameObject root in sandbox.GetRootGameObjects())
            {
                Transform tiles = root.transform.Find("DungeonAuthoringGrid/Floor_01/FloorTiles");
                if (tiles != null && tiles.childCount != 0) throw new InvalidOperationException("UI stroke did not undo as one operation.");
            }
            return $"PASS: Scene mouse drag placed {count} tiles, and one Undo removed the stroke.";
        }
        finally
        {
            Scene original = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).handle == originalHandle) original = SceneManager.GetSceneAt(i);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (sandbox.IsValid()) EditorSceneManager.CloseScene(sandbox, true);
            var window = EditorWindow.GetWindow<DungeonMapAuthoringWindow>();
            typeof(DungeonMapAuthoringWindow).GetField("painting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(window, false);
            window.CreateGUI();
        }
    }

    public static string Regression()
    {
        Scene original = SceneManager.GetActiveScene();
        Scene sandbox = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        DungeonRandomGameObjectBrush brush = null;
        int checks = 0;
        try
        {
            GameObject chunk = DungeonMapAuthoringWindow.CreateChunk(new Vector2(50, 120), 2.8f, new Vector3(10, 2, 20));
            Check(chunk.transform.Find("Ground").localScale == new Vector3(50, 1, 120), "50x120 base", ref checks);
            BoxCollider collider = chunk.transform.Find("Ground").GetComponent<BoxCollider>();
            Physics.SyncTransforms();
            Check(Mathf.Abs(collider.bounds.max.y - 2f) < 0.001f, "Ground top equals chunk height", ref checks);
            Check(chunk.GetComponentsInChildren<Grid>().Length == 5, "One grid and four categories", ref checks);
            Undo.PerformUndo();
            Check(chunk == null, "Whole chunk undoes in one operation", ref checks);
            Undo.PerformRedo();
            foreach (GameObject root in sandbox.GetRootGameObjects())
                if (root.name.StartsWith("MapChunk_")) chunk = root;
            Check(chunk != null, "Whole chunk redo", ref checks);

            Grid buildings = DungeonMapAuthoringWindow.EnsureLayer(chunk, 1, 4f, "Structures");
            Grid nature = DungeonMapAuthoringWindow.EnsureLayer(chunk, 1, 4f, "Nature");
            Grid tiles = DungeonMapAuthoringWindow.EnsureLayer(chunk, 1, 4f, "FloorTiles");
            Grid upstairs = DungeonMapAuthoringWindow.EnsureLayer(chunk, 2, 4f, "Structures");
            Check(Mathf.Abs(upstairs.transform.position.y - buildings.transform.position.y - 4) < 0.001f, "Second floor is four units up", ref checks);
            Check(DungeonMapAuthoringWindow.EnsureLayer(chunk, 2, 9f, "Structures") == upstairs, "Existing layer reused without moving contents", ref checks);
            Check(DungeonMapAuthoringWindow.CellInsideGround(tiles, Vector3Int.zero), "Origin cell inside", ref checks);
            Check(!DungeonMapAuthoringWindow.CellInsideGround(tiles, new Vector3Int(-1, 0, 0)), "Negative cell outside", ref checks);
            Check(!DungeonMapAuthoringWindow.CellInsideGround(tiles, new Vector3Int(18, 43, 0)), "Far cell outside", ref checks);

            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = "TestSource";
            prefab.transform.localScale = new Vector3(2, 3, 4);
            prefab.transform.localRotation = Quaternion.Euler(0, 15, 0);
            brush = ScriptableObject.CreateInstance<DungeonRandomGameObjectBrush>();
            brush.Configure(prefab, new Vector3(2, 0.5f, 3), 1.25f, 1);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            brush.Paint(buildings, buildings.gameObject, Vector3Int.zero);
            brush.Paint(buildings, buildings.gameObject, Vector3Int.zero);
            brush.Paint(nature, nature.gameObject, Vector3Int.zero);
            brush.Paint(tiles, tiles.gameObject, Vector3Int.zero);
            brush.Paint(upstairs, upstairs.gameObject, Vector3Int.zero);
            Undo.CollapseUndoOperations(group);
            Check(buildings.transform.childCount == 1, "Duplicate cell skipped", ref checks);
            Check(nature.transform.childCount == 1 && tiles.transform.childCount == 1 && upstairs.transform.childCount == 1, "Same cell allowed across categories and floors", ref checks);
            Transform placed = buildings.transform.GetChild(0);
            Check(placed.localScale == new Vector3(4, 1.5f, 12), "Scale relative to source", ref checks);
            Check(Mathf.Abs(placed.position.y - 3.25f) < 0.001f, "Height offset", ref checks);
            Check(Quaternion.Angle(placed.rotation, Quaternion.Euler(0, 105, 0)) < 0.01f, "Quarter turn plus source rotation", ref checks);
            Matrix4x4 expected = brush.PlacementMatrix(buildings, Vector3Int.zero, prefab);
            Check(Vector3.Distance(expected.MultiplyPoint3x4(Vector3.one), placed.localToWorldMatrix.MultiplyPoint3x4(Vector3.one)) < 0.001f, "Preview matches placed transform", ref checks);

            brush.Configure(prefab, Vector3.one, 10f, 0);
            brush.Paint(buildings, buildings.gameObject, Vector3Int.zero);
            Check(buildings.transform.childCount == 1, "Changed height cannot duplicate same cell", ref checks);
            brush.Erase(buildings, buildings.gameObject, Vector3Int.zero);
            Check(buildings.transform.childCount == 0, "Elevated object erases at its base cell", ref checks);
            Check(nature.transform.childCount == 1 && upstairs.transform.childCount == 1 && tiles.transform.childCount == 1, "Erase isolated to target layer", ref checks);

            Undo.IncrementCurrentGroup();
            int stroke = Undo.GetCurrentGroup();
            brush.Configure(prefab, Vector3.one, 0, 0);
            for (int i = 1; i <= 3; i++) brush.Paint(buildings, buildings.gameObject, new Vector3Int(i, 0, 0));
            Undo.CollapseUndoOperations(stroke);
            Check(buildings.transform.childCount == 3, "Multi-cell stroke", ref checks);
            Undo.PerformUndo();
            Check(buildings.transform.childCount == 0, "Whole stroke undoes together", ref checks);
            Undo.PerformRedo();
            Check(buildings.transform.childCount == 3, "Whole stroke redo", ref checks);

            chunk.transform.rotation = Quaternion.Euler(0, 45, 0);
            chunk.transform.localScale = Vector3.one * 2;
            Check(DungeonMapAuthoringWindow.CellInsideGround(tiles, Vector3Int.zero), "Rotated scaled chunk bounds", ref checks);
            brush.Configure(prefab, Vector3.one, 0.3f, 2);
            var cell = new Vector3Int(3, 3, 0);
            brush.Paint(tiles, tiles.gameObject, cell);
            Transform rotated = tiles.transform.GetChild(tiles.transform.childCount - 1);
            Check(Vector3.Distance(brush.PlacementMatrix(tiles, cell, prefab).MultiplyPoint3x4(Vector3.one), rotated.localToWorldMatrix.MultiplyPoint3x4(Vector3.one)) < 0.001f, "Rotated scaled preview matches placement", ref checks);

            int rootCount = sandbox.rootCount;
            bool rejected = false;
            try { DungeonMapAuthoringWindow.CreateChunk(new Vector2(-1, 120), 0, Vector3.zero); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && sandbox.rootCount == rootCount, "Invalid dimensions create nothing", ref checks);

            DungeonMapAuthoringWindow.Open();
            Check(EditorWindow.GetWindow<DungeonMapAuthoringWindow>().rootVisualElement.childCount > 0, "Window builds its UI", ref checks);
            return $"PASS: {checks} map authoring checks. Temporary scene closed; existing scenes and assets preserved.";
        }
        finally
        {
            if (brush != null) UnityEngine.Object.DestroyImmediate(brush);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(sandbox, true);
        }
    }

    private static void Check(bool value, string message, ref int checks)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
    }
}
