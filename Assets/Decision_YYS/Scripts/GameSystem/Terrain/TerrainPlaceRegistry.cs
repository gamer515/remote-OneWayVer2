using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 목적지 이름으로 장소 데이터와 월드 좌표를 조회합니다.
/// </summary>
public sealed class TerrainPlaceRegistry
{
    private readonly Dictionary<string, PlaceData> places =
        new Dictionary<string, PlaceData>(StringComparer.Ordinal);

    private readonly Vector3 terrainOrigin;
    private readonly float chunkSize;

    public TerrainPlaceRegistry(TerrainData terrainData, Vector3 terrainOrigin, float chunkSize)
    {
        this.terrainOrigin = terrainOrigin;
        this.chunkSize = chunkSize;

        if (terrainData?.places == null)
            return;

        foreach (PlaceData place in terrainData.places)
        {
            if (!string.IsNullOrEmpty(place.destination))
                places[place.destination] = place;
        }
    }

    public bool TryGetPlace(string destination, out PlaceData place)
    {
        if (string.IsNullOrEmpty(destination))
        {
            place = null;
            return false;
        }

        return places.TryGetValue(destination, out place);
    }

    public Vector3 GetWorldPosition(PlaceData place)
    {
        if (place == null)
            return terrainOrigin;

        return terrainOrigin + new Vector3(
            place.position.x,
            place.position.y,
            place.position.z + chunkSize * place.chunkIndex);
    }

    public Vector3 GetChunkWorldPosition(int chunkIndex)
    {
        return terrainOrigin + Vector3.forward * (chunkSize * chunkIndex);
    }
}
