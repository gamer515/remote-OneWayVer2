using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 여러 챕터의 지형을 하나의 전역 청크 공간에 생성하고 재사용합니다.
/// </summary>
public sealed class TerrainBuilder
{
    private sealed class TerrainSegment
    {
        public TerrainData Data;
        public TerrainPlaceRegistry Registry;
        public GameObject GroundPrefab;
        public GameObject PlacePrefab;
        public Transform Parent;
        public int FirstGlobalChunkIndex;
        public int ChunkCount;
    }

    private readonly List<TerrainSegment> segments = new List<TerrainSegment>();
    private readonly Dictionary<int, GameObject> chunkInstances = new Dictionary<int, GameObject>();

    public void RegisterTerrain(TerrainData terrainData, TerrainPlaceRegistry placeRegistry,
        GameObject groundPrefab, GameObject placePrefab, Transform parent,
        int firstGlobalChunkIndex, int chunkCount)
    {
        if (terrainData?.places == null || placeRegistry == null || parent == null || chunkCount <= 0)
            return;

        // 로컬 청크 번호가 같은 챕터끼리 충돌하지 않도록 전역 시작 번호를 함께 저장합니다.
        segments.Add(new TerrainSegment
        {
            Data = terrainData,
            Registry = placeRegistry,
            GroundPrefab = groundPrefab,
            PlacePrefab = placePrefab,
            Parent = parent,
            FirstGlobalChunkIndex = firstGlobalChunkIndex,
            ChunkCount = chunkCount
        });
    }

    public bool CreateOrLoadChunk(int globalChunkIndex)
    {
        TerrainSegment segment = FindSegment(globalChunkIndex);
        if (segment == null) return false;

        // 멀어져 회수된 청크는 새로 만들지 않고 다시 활성화합니다.
        if (chunkInstances.TryGetValue(globalChunkIndex, out GameObject cachedChunk))
        {
            cachedChunk.SetActive(true);
            return true;
        }

        int localChunkIndex = globalChunkIndex - segment.FirstGlobalChunkIndex;
        GameObject chunk = new GameObject($"Chunk_{globalChunkIndex}");
        chunk.transform.SetParent(segment.Parent, false);
        chunk.transform.localPosition = Vector3.zero;

        CreateGround(segment, localChunkIndex, chunk.transform);
        CreatePlaces(segment, localChunkIndex, chunk.transform);
        chunkInstances.Add(globalChunkIndex, chunk);
        return true;
    }

    public void UnloadOutsideRange(int minimumChunkIndex, int maximumChunkIndex)
    {
        foreach (KeyValuePair<int, GameObject> pair in chunkInstances)
        {
            bool shouldRemainLoaded = pair.Key >= minimumChunkIndex && pair.Key <= maximumChunkIndex;
            if (!shouldRemainLoaded && pair.Value != null) pair.Value.SetActive(false);
        }
    }

    private TerrainSegment FindSegment(int globalChunkIndex)
    {
        foreach (TerrainSegment segment in segments)
        {
            int lastChunkIndex = segment.FirstGlobalChunkIndex + segment.ChunkCount - 1;
            if (globalChunkIndex >= segment.FirstGlobalChunkIndex && globalChunkIndex <= lastChunkIndex)
                return segment;
        }

        return null;
    }

    private static void CreateGround(TerrainSegment segment, int localChunkIndex, Transform chunkParent)
    {
        if (segment.GroundPrefab == null) return;

        GameObject ground = Object.Instantiate(segment.GroundPrefab,
            segment.Registry.GetChunkWorldPosition(localChunkIndex), Quaternion.identity, chunkParent);
        ground.name = $"Ground_{localChunkIndex}";
    }

    private static void CreatePlaces(TerrainSegment segment, int localChunkIndex, Transform chunkParent)
    {
        if (segment.PlacePrefab == null) return;

        foreach (PlaceData place in segment.Data.places)
        {
            if (place == null || place.chunkIndex != localChunkIndex ||
                string.IsNullOrWhiteSpace(place.destination)) continue;

            GameObject instance = Object.Instantiate(segment.PlacePrefab,
                segment.Registry.GetWorldPosition(place), Quaternion.Euler(place.rotation), chunkParent);
            instance.name = $"Place_{place.destination}_{localChunkIndex}";
        }
    }
}
