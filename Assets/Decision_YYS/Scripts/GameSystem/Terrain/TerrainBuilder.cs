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
        public GameObject[] GroundPrefabs;
        public TerrainPrefabCatalog PrefabCatalog;
        public Transform Parent;
        public int FirstGlobalChunkIndex;
        public int ChunkCount;
        public int RunSeed;
    }

    private readonly List<TerrainSegment> segments = new List<TerrainSegment>();
    private readonly Dictionary<int, GameObject> chunkInstances = new Dictionary<int, GameObject>();

    public void RegisterTerrain(TerrainData terrainData, TerrainPlaceRegistry placeRegistry,
        GameObject[] groundPrefabs, TerrainPrefabCatalog prefabCatalog, Transform parent,
        int firstGlobalChunkIndex, int chunkCount, int runSeed)
    {
        if (terrainData?.places == null || placeRegistry == null || parent == null || chunkCount <= 0)
            return;

        // 로컬 청크 번호가 같은 챕터끼리 충돌하지 않도록 전역 시작 번호를 함께 저장합니다.
        segments.Add(new TerrainSegment
        {
            Data = terrainData,
            Registry = placeRegistry,
            GroundPrefabs = groundPrefabs,
            PrefabCatalog = prefabCatalog,
            Parent = parent,
            FirstGlobalChunkIndex = firstGlobalChunkIndex,
            ChunkCount = chunkCount,
            RunSeed = runSeed
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

        GameObject ground = CreateGround(segment, localChunkIndex, chunk.transform);
        CreatePlaces(segment, localChunkIndex, chunk.transform, ground);
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

    private static GameObject CreateGround(
        TerrainSegment segment,
        int localChunkIndex,
        Transform chunkParent)
    {
        if (segment.GroundPrefabs == null || localChunkIndex < 0 ||
            localChunkIndex >= segment.GroundPrefabs.Length) return null;

        GameObject groundPrefab = segment.GroundPrefabs[localChunkIndex];
        if (groundPrefab == null) return null;

        GameObject ground = Object.Instantiate(groundPrefab,
            segment.Registry.GetChunkWorldPosition(localChunkIndex), Quaternion.identity, chunkParent);
        ground.name = $"Ground_{localChunkIndex}_{groundPrefab.name}";
        return ground;
    }

    private static void CreatePlaces(
        TerrainSegment segment,
        int localChunkIndex,
        Transform chunkParent,
        GameObject ground)
    {
        foreach (PlaceData place in segment.Data.places)
        {
            if (place == null || place.chunkIndex != localChunkIndex) continue;

            if (segment.PrefabCatalog == null ||
                !segment.PrefabCatalog.TryGetPrefab(place.prefabId, out GameObject placePrefab))
            {
                Debug.LogWarning($"prefabId '{place.prefabId}'에 연결된 지형 프리팹이 없습니다.");
                continue;
            }

            // 저작된 Terrain 안에 같은 placeId의 오브젝트가 있으면 그 오브젝트를 재사용합니다.
            // 이를 통해 눈으로 배치한 NPC와 JSON 생성 NPC가 겹치지 않습니다.
            Transform authoredPlace = ground != null
                ? ground.transform.Find($"Place_{place.placeId}")
                : null;
            if (authoredPlace != null)
            {
                authoredPlace.gameObject.SetActive(true);
                authoredPlace.SetPositionAndRotation(
                    segment.Registry.GetWorldPosition(place),
                    Quaternion.Euler(place.rotation));
                continue;
            }

            // Terrain에 미리 배치되지 않은 오브젝트만 카탈로그 프리팹으로 생성합니다.
            GameObject instance = Object.Instantiate(placePrefab,
                segment.Registry.GetWorldPosition(place), Quaternion.Euler(place.rotation), chunkParent);
            instance.name = $"Place_{place.placeId}_{place.prefabId}";
        }
    }
}
