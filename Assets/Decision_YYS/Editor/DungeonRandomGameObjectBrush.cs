using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEngine;

[Serializable]
public sealed class DungeonBrushEntry
{
    public GameObject prefab;
    [Min(0.01f)] public float weight = 1f;
}

/// <summary>
/// Paints one weighted prefab per selected Grid cell. Randomness happens only while authoring;
/// painted instances are ordinary scene/prefab objects and never change during play.
/// </summary>
[CustomGridBrush(true, false, false, "Dungeon Random Prefab Brush")]
public sealed class DungeonRandomGameObjectBrush : GridBrushBase
{
    [SerializeField] private DungeonBrushEntry[] entries = Array.Empty<DungeonBrushEntry>();
    [SerializeField] private Vector3 anchor = new Vector3(0.5f, 0.5f, 0f);
    [SerializeField] private Vector3 offset;
    [SerializeField] private Vector3 scale = Vector3.one;
    [SerializeField] private bool randomizeQuarterTurns;

    private int quarterTurns;

    public override void Paint(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (gridLayout == null || brushTarget == null || HasObjectInCell(gridLayout, brushTarget, position))
            return;

        GameObject prefab = PickPrefab();
        if (prefab == null) return;

        GameObject instance = PrefabUtility.IsPartOfPrefabAsset(prefab)
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, brushTarget.scene)
            : Instantiate(prefab);
        if (instance == null) return;

        Undo.RegisterCreatedObjectUndo(instance, "Paint Dungeon Prefab");
        instance.name = prefab.name;
        instance.transform.SetParent(brushTarget.transform, false);
        instance.transform.position = GetCellCenter(gridLayout, position) + offset;
        int turns = quarterTurns + (randomizeQuarterTurns ? UnityEngine.Random.Range(0, 4) : 0);
        instance.transform.localRotation = Quaternion.Euler(0f, turns * 90f, 0f);
        instance.transform.localScale = scale;
        EditorSceneManager.MarkSceneDirty(brushTarget.scene);
    }

    public override void Erase(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
    {
        if (gridLayout == null || brushTarget == null) return;

        for (int index = brushTarget.transform.childCount - 1; index >= 0; index--)
        {
            Transform child = brushTarget.transform.GetChild(index);
            if (gridLayout.WorldToCell(child.position) != position) continue;
            Undo.DestroyObjectImmediate(child.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(brushTarget.scene);
    }

    public override void BoxFill(GridLayout gridLayout, GameObject brushTarget, BoundsInt bounds)
    {
        foreach (Vector3Int position in bounds.allPositionsWithin)
            Paint(gridLayout, brushTarget, position);
    }

    public override void BoxErase(GridLayout gridLayout, GameObject brushTarget, BoundsInt bounds)
    {
        foreach (Vector3Int position in bounds.allPositionsWithin)
            Erase(gridLayout, brushTarget, position);
    }

    public override void Rotate(RotationDirection direction, GridLayout.CellLayout layout)
    {
        quarterTurns = (quarterTurns + (direction == RotationDirection.Clockwise ? 1 : 3)) % 4;
    }

    private GameObject PickPrefab()
    {
        float totalWeight = 0f;
        foreach (DungeonBrushEntry entry in entries)
        {
            if (entry?.prefab != null) totalWeight += Mathf.Max(0f, entry.weight);
        }

        if (totalWeight <= 0f) return null;
        float value = UnityEngine.Random.value * totalWeight;
        foreach (DungeonBrushEntry entry in entries)
        {
            if (entry?.prefab == null) continue;
            value -= Mathf.Max(0f, entry.weight);
            if (value <= 0f) return entry.prefab;
        }

        return null;
    }

    private static bool HasObjectInCell(
        GridLayout gridLayout,
        GameObject brushTarget,
        Vector3Int position)
    {
        foreach (Transform child in brushTarget.transform)
        {
            if (gridLayout.WorldToCell(child.position) == position) return true;
        }

        return false;
    }

    private Vector3 GetCellCenter(GridLayout gridLayout, Vector3Int position)
    {
        Vector3 local = gridLayout.CellToLocalInterpolated(position + anchor);
        return gridLayout.LocalToWorld(local);
    }
}

[CustomEditor(typeof(DungeonRandomGameObjectBrush))]
public sealed class DungeonRandomGameObjectBrushEditor : GridBrushEditorBase
{
    public override GameObject[] validTargets
    {
        get
        {
            var targets = new List<GameObject>();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            GridLayout[] grids = stage != null
                ? stage.prefabContentsRoot.GetComponentsInChildren<GridLayout>(true)
                : UnityEngine.Object.FindObjectsByType<GridLayout>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (GridLayout grid in grids)
            {
                if (grid == null || !grid.gameObject.activeInHierarchy) continue;
                if (grid.name == "FloorTiles" || grid.name == "Structures" ||
                    grid.name == "Decorations")
                    targets.Add(grid.gameObject);
            }

            return targets.ToArray();
        }
    }
}
