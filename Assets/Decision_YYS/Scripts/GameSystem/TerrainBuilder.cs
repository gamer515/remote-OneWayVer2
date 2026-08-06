using UnityEngine;

/// <summary>
/// TerrainData에 정의된 장소 프리팹을 씬에 생성합니다.
/// </summary>
public sealed class TerrainBuilder
{
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
