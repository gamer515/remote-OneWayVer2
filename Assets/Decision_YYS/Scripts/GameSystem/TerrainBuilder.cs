using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TerrainData에 정의된 장소 프리팹을 씬에 생성합니다.
/// </summary>
public sealed class TerrainBuilder
{
    private Dictionary<int, GameObject> chunkInstances = new Dictionary<int, GameObject>();

    public void CreateOrLoadChunk(int chunkIndex, GameObject placePrefab = null, Transform parent = null)
    {
        if(placePrefab == null || parent == null)
        {
            return;
        }

        if (chunkInstances.ContainsKey(chunkIndex))
        {
            chunkInstances[chunkIndex].SetActive(true);
            return;
        }

        GameObject chunk = Object.Instantiate(new GameObject($"Chunk_{chunkIndex}"), parent);
        chunk.transform.position = parent.position;
        chunkInstances[chunkIndex] = chunk;
    }

    public void UnloadChunk(int chunkIndex)
    {
        if (chunkInstances.TryGetValue(chunkIndex, out GameObject chunk))
        {
            chunk.SetActive(false);
        }
    }

    public void Build(
        TerrainData terrainData,
        TerrainPlaceRegistry placeRegistry,
        GameObject placePrefab,
        Transform parent)
    {
        if (terrainData?.places == null || placeRegistry == null ||
            placePrefab == null || parent == null)
        {
            return;
        }

        foreach (PlaceData place in terrainData.places)
        {
            if (string.IsNullOrEmpty(place.destination))
                continue;

            GameObject instance = Object.Instantiate(
                placePrefab,
                placeRegistry.GetWorldPosition(place),
                Quaternion.Euler(place.rotation),
                parent);

            instance.name = $"Place_{place.destination}_{place.chunkIndex}";
        }
    }
}
