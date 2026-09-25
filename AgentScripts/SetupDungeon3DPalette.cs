using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;

public static class SetupDungeon3DPalette
{
    private const string PaletteFolder = "Assets/Decision_YYS/TilePalettes/Dungeon3D";
    private const string BrushFolder = PaletteFolder + "/Brushes";
    private const string DungeonFolder =
        "Assets/UnityAssets/Buildings_And_Terrains/Dungeon_FBX format";
    private const string GroundPath = "Assets/Decision_YYS/Prafabs/Terrain_Initial_Village.prefab";

    public static string Apply()
    {
        EnsureFolder("Assets/Decision_YYS", "TilePalettes");
        EnsureFolder("Assets/Decision_YYS/TilePalettes", "Dungeon3D");
        EnsureFolder(PaletteFolder, "Brushes");

        string palettePath = PaletteFolder + "/Dungeon3DPalette.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(palettePath) == null)
        {
            GridPaletteUtility.CreateNewPalette(
                PaletteFolder,
                "Dungeon3DPalette",
                GridLayout.CellLayout.Rectangle,
                GridPalette.CellSizing.Manual,
                new Vector3(2.8f, 2.8f, 2.8f),
                GridLayout.CellSwizzle.XZY);
        }

        CreateRandomFloorBrush();
        string[] names =
        {
            "floor", "floor-detail", "wall", "wall-half", "wall-narrow", "wall-opening",
            "stairs", "gate", "column", "wood-structure", "wood-support", "banner",
            "barrel", "pot", "rocks", "stones", "table", "chair", "chest", "trap"
        };
        foreach (string name in names) CreateGameObjectBrush(name);

        ConfigureGroundPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return $"Palette ready: {palettePath}; brushes={names.Length + 1}; cellSize=2.8; swizzle=XZY";
    }

    private static void CreateRandomFloorBrush()
    {
        string path = BrushFolder + "/Dungeon_RandomFloor.asset";
        DungeonRandomGameObjectBrush brush =
            AssetDatabase.LoadAssetAtPath<DungeonRandomGameObjectBrush>(path);
        if (brush == null)
        {
            brush = ScriptableObject.CreateInstance<DungeonRandomGameObjectBrush>();
            brush.name = "Dungeon Random Floor";
            AssetDatabase.CreateAsset(brush, path);
        }

        var serialized = new SerializedObject(brush);
        SerializedProperty entries = serialized.FindProperty("entries");
        entries.arraySize = 2;
        SetEntry(entries.GetArrayElementAtIndex(0), "floor", 4f);
        SetEntry(entries.GetArrayElementAtIndex(1), "floor-detail", 1f);
        serialized.FindProperty("anchor").vector3Value = new Vector3(0.5f, 0.5f, 0f);
        serialized.FindProperty("offset").vector3Value = Vector3.zero;
        serialized.FindProperty("scale").vector3Value = Vector3.one;
        serialized.FindProperty("randomizeQuarterTurns").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(brush);
    }

    private static void SetEntry(SerializedProperty entry, string name, float weight)
    {
        entry.FindPropertyRelative("prefab").objectReferenceValue = LoadDungeonAsset(name);
        entry.FindPropertyRelative("weight").floatValue = weight;
    }

    private static void CreateGameObjectBrush(string name)
    {
        string safeName = name.Replace('-', '_');
        string path = $"{BrushFolder}/Dungeon_{safeName}.asset";
        GameObjectBrush brush = AssetDatabase.LoadAssetAtPath<GameObjectBrush>(path);
        if (brush == null)
        {
            brush = ScriptableObject.CreateInstance<GameObjectBrush>();
            brush.name = $"Dungeon {name}";
            AssetDatabase.CreateAsset(brush, path);
        }

        brush.Init(Vector3Int.one);
        brush.SetGameObject(Vector3Int.zero, LoadDungeonAsset(name));
        brush.SetOffset(Vector3Int.zero, Vector3.zero);
        brush.SetScale(Vector3Int.zero, Vector3.one);
        brush.SetOrientation(Vector3Int.zero, Quaternion.identity);
        brush.m_Anchor = new Vector3(0.5f, 0.5f, 0f);
        EditorUtility.SetDirty(brush);
    }

    private static void ConfigureGroundPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(GroundPath);
        try
        {
            Transform gridTransform = root.transform.Find("DungeonAuthoringGrid");
            GameObject gridObject;
            if (gridTransform == null)
            {
                gridObject = new GameObject("DungeonAuthoringGrid", typeof(Grid));
                gridObject.transform.SetParent(root.transform, false);
            }
            else
            {
                gridObject = gridTransform.gameObject;
            }

            // The authored ground now uses the centered 25 x 50 terrain footprint.
            gridObject.transform.localPosition = new Vector3(-1.4f, 0f, 0f);

            Grid grid = gridObject.GetComponent<Grid>();
            if (grid == null) grid = gridObject.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSize = new Vector3(2.8f, 2.8f, 2.8f);
            grid.cellGap = Vector3.zero;
            grid.cellSwizzle = GridLayout.CellSwizzle.XZY;

            EnsureLayerGrid(gridObject.transform, "FloorTiles");
            EnsureLayerGrid(gridObject.transform, "Structures");
            EnsureLayerGrid(gridObject.transform, "Decorations");
            PrefabUtility.SaveAsPrefabAsset(root, GroundPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureLayerGrid(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        GameObject child = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null) child.transform.SetParent(parent, false);
        Grid grid = child.GetComponent<Grid>();
        if (grid == null) grid = child.AddComponent<Grid>();
        grid.cellLayout = GridLayout.CellLayout.Rectangle;
        grid.cellSize = new Vector3(2.8f, 2.8f, 2.8f);
        grid.cellGap = Vector3.zero;
        grid.cellSwizzle = GridLayout.CellSwizzle.XZY;
    }

    private static GameObject LoadDungeonAsset(string name)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>($"{DungeonFolder}/{name}.fbx");
    }

    private static void EnsureFolder(string parent, string name)
    {
        string fullPath = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(fullPath)) AssetDatabase.CreateFolder(parent, name);
    }
}
