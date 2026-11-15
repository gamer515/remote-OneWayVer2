using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal sealed partial class MineAuthoredStory
{
    internal const string Episode = "Chapter_1/Chapter_1_01";
    internal const string Prefix = "AuthoredMine/";
    private readonly Dictionary<string, GameObject> actors;

    internal MineAuthoredStory(GameObject[] prefabs, EnvController.MineStoryLength length)
    {
        IsShort = length == EnvController.MineStoryLength.Short;
        actors = prefabs.Where(prefab => prefab != null)
            .ToDictionary(prefab => prefab.name.Split('_')[2], StringComparer.Ordinal);
        LoadPack();
    }

    internal void ApplyLayout(TerrainData terrain, int seed, TerrainPrefabCatalog prefabs, MineNpcPlacement npcPlacement) =>
        ApplyPackLayout(terrain, seed, prefabs, npcPlacement);

    private static int Pick(int seed, int index, string key, int count) =>
        Mathf.Min(count - 1, (int)(MineChapterSettings.Roll(seed, index, key) * count));
}
