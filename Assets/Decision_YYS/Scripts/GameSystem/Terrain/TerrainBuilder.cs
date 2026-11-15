using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// 여러 챕터의 지형을 하나의 전역 청크 공간에 생성하고 재사용합니다.
/// </summary>
public sealed class TerrainBuilder
{
    internal readonly struct FacilityMarker
    {
        internal readonly string Id;
        internal readonly Vector3 Position;
        internal readonly bool IsShop;
        internal FacilityMarker(string id, Vector3 position, bool isShop)
        { Id = id; Position = position; IsShop = isShop; }
    }

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
        public TerrainFacilityPlan.Slot[] Facilities;
        public TerrainRoomLayout[] RoomLayouts;
    }

    private readonly List<TerrainSegment> segments = new List<TerrainSegment>();
    private readonly Dictionary<int, GameObject> chunkInstances = new Dictionary<int, GameObject>();
    private readonly Dictionary<string, TerrainFacilityPlan.History> facilityHistory = new Dictionary<string, TerrainFacilityPlan.History>();
    private readonly List<FacilityMarker> facilityMarkers = new List<FacilityMarker>();
    internal IReadOnlyList<FacilityMarker> FacilityMarkers => facilityMarkers;
    internal bool HasChapter(string chapterId) => facilityHistory.ContainsKey(chapterId);

    internal Transform FindStoryPlace(TerrainPlaceRegistry registry, PlaceData place)
    {
        var segment = segments.Find(candidate => candidate.Registry == registry);
        if (segment == null ||
            !chunkInstances.TryGetValue(segment.FirstGlobalChunkIndex + place.chunkIndex, out var chunk) ||
            chunk == null || !chunk.activeInHierarchy) return null;
        return chunk.transform.Find("Place_" + (place.sharedStoryVisualId ?? place.placeId));
    }

    internal float? LoadedChunkEndZ(TerrainPlaceRegistry registry)
    {
        var segment = segments.Find(candidate => candidate.Registry == registry);
        if (segment == null) return null;
        for (int localIndex = segment.ChunkCount - 1; localIndex >= 0; localIndex--)
        {
            if (chunkInstances.TryGetValue(segment.FirstGlobalChunkIndex + localIndex, out var chunk) &&
                chunk != null && chunk.activeInHierarchy)
                return registry.GetChunkWorldPosition(localIndex + 1).z;
        }
        return null;
    }

    public void RegisterTerrain(TerrainData terrainData, TerrainPlaceRegistry placeRegistry,
        GameObject[] groundPrefabs, TerrainPrefabCatalog prefabCatalog, Transform parent,
        int firstGlobalChunkIndex, int chunkCount, int runSeed, string chapterId)
        => RegisterTerrain(terrainData, placeRegistry, groundPrefabs, prefabCatalog, parent, firstGlobalChunkIndex,
            chunkCount, runSeed, chapterId, null, false);

    internal void RegisterTerrain(TerrainData terrainData, TerrainPlaceRegistry placeRegistry,
        GameObject[] groundPrefabs, TerrainPrefabCatalog prefabCatalog, Transform parent,
        int firstGlobalChunkIndex, int chunkCount, int runSeed, string chapterId, TerrainRoomLayout[] roomLayouts, bool capLocations)
    {
        if (terrainData?.places == null || placeRegistry == null || parent == null || chunkCount <= 0)
            return;

        if (!facilityHistory.TryGetValue(chapterId, out var history))
            facilityHistory[chapterId] = history = new TerrainFacilityPlan.History(firstGlobalChunkIndex);
        var facilities = TerrainFacilityPlan.Build(terrainData, runSeed, firstGlobalChunkIndex, history, capLocations);
        foreach (var place in terrainData.places.Where(place => place.storyLocationShop))
            facilityMarkers.Add(new FacilityMarker($"{firstGlobalChunkIndex + place.chunkIndex}:shop",
                placeRegistry.GetWorldPosition(place), true));
        foreach (var slot in facilities)
        {
            bool isShop = slot.Id == "shop";
            if (isShop || slot.OptionalCandidate)
                facilityMarkers.Add(new FacilityMarker($"{firstGlobalChunkIndex + slot.Chunk}:{slot.Id}",
                    placeRegistry.GetChunkWorldPosition(slot.Chunk) + new Vector3(slot.X, 0, slot.Z), isShop));
        }
        if (facilities.Length > 0)
            Debug.Log($"[장소 배치 예고] {chapterId} · " + string.Join(" / ",
                facilities.GroupBy(slot => slot.Chunk).Select(group =>
                    $"청크 {firstGlobalChunkIndex + group.Key + 1}: {string.Join(", ", group.Select(slot => slot.Label))}")));

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
            RunSeed = runSeed,
            Facilities = facilities,
            RoomLayouts = roomLayouts
        });
    }

    public bool CreateOrLoadChunk(int globalChunkIndex)
    {
        TerrainSegment segment = FindSegment(globalChunkIndex);
        if (segment == null) return false;

        if (chunkInstances.TryGetValue(globalChunkIndex, out GameObject loadedChunk) && loadedChunk != null)
            return true;
        chunkInstances.Remove(globalChunkIndex);

        int localChunkIndex = globalChunkIndex - segment.FirstGlobalChunkIndex;
        GameObject chunk = new GameObject($"Chunk_{globalChunkIndex}");
        chunk.transform.SetParent(segment.Parent, false);
        chunk.transform.localPosition = Vector3.zero;

        GameObject ground = CreateGround(segment, localChunkIndex, chunk.transform);
        if (ground != null) segment.RoomLayouts?[localChunkIndex]?.Apply(ground.transform);
        if (ground != null && segment.Data.places.Any(place => place.storyActors != null))
            foreach (Transform child in ground.transform)
                if (child.name.StartsWith("Place_", System.StringComparison.Ordinal))
                    child.gameObject.SetActive(false);
        if (ground != null && segment.Data.places.Any(place => place.authoredLocationId != null))
        {
            // The story layout owns the locations and NPCs on this ground.
            ground.transform.Find("02_Buildings")?.gameObject.SetActive(false);
            ground.transform.Find("04_NPCs")?.gameObject.SetActive(false);
            var decorations = ground.transform.Find("03_Decorations");
            if (decorations != null)
                foreach (Transform decoration in decorations)
                    if (decoration.name != "Decor_MineEdges") decoration.gameObject.SetActive(false);
        }
        CreatePlaces(segment, localChunkIndex, chunk.transform, ground);
        CreateMineRoadsideDecorations(segment, localChunkIndex, chunk.transform);
        foreach (var slot in segment.Facilities)
            if (slot.Chunk == localChunkIndex)
                Debug.Log($"[장소 예정] {slot.Label} ({slot.Id}) · 청크 {globalChunkIndex + 1} · 로컬 X={slot.X:F1}, Z={slot.Z:F1} · {(slot.OptionalCandidate ? "주요 선택 후보" : "빈 자리 예약")} · 에셋/상호작용 연결 대기");
        chunkInstances.Add(globalChunkIndex, chunk);
        return true;
    }

    private static void CreateMineRoadsideDecorations(TerrainSegment segment, int chunkIndex, Transform chunk)
    {
        var decorations = segment.PrefabCatalog?.MineRoadsideDecorations;
        if (decorations == null || decorations.Count == 0) return;
        var places = segment.Data.places.Where(place => place.chunkIndex == chunkIndex &&
            place.authoredLocationId != null && place.placeId == place.sharedStoryVisualId).ToArray();
        if (places.Length == 0) return;

        var occupied = places.Select(place => new Vector2(place.position.z + place.storyLocationDepth.x,
            place.position.z + place.storyLocationDepth.y))
            .Concat(segment.Facilities.Where(slot => slot.Chunk == chunkIndex)
                .Select(slot => new Vector2(slot.Z - 6f, slot.Z + 6f)))
            .OrderBy(span => span.x).ToArray();
        float length = segment.Data.chunkLength;
        Vector3 origin = segment.Registry.GetChunkWorldPosition(chunkIndex);
        for (int gap = 1; gap < occupied.Length; gap++)
        {
            float end = Mathf.Min(occupied[gap].x - 2f, length - 6f);
            float start = occupied[gap - 1].y + 2f;
            if (end <= start) continue;
            int count = Mathf.Max(1, Mathf.FloorToInt((end - start) / 16f));
            for (int slot = 0; slot < count; slot++)
            {
                string key = $"mine-roadside:{gap}:{slot}";
                int index = Mathf.Min(decorations.Count - 1, (int)(MineChapterSettings.Roll(
                    segment.RunSeed, chunkIndex, key) * decorations.Count));
                float slotStart = Mathf.Lerp(start, end, (float)slot / count);
                float slotEnd = Mathf.Lerp(start, end, (slot + 1f) / count);
                var prefab = Enumerable.Range(0, decorations.Count)
                    .Select(offset => decorations[(index + offset) % decorations.Count])
                    .FirstOrDefault(candidate => candidate != null &&
                        MineAuthoredStory.RenderedBounds(candidate).extents.z <= (end - start) / (2f * count));
                if (prefab == null) continue;
                var localBounds = MineAuthoredStory.RenderedBounds(prefab);
                var candidates = new[] { new Vector2(-4f, .30f), new Vector2(4f, .30f),
                    new Vector2(-5f, .70f), new Vector2(5f, .70f) };
                int pointIndex = Mathf.Min(candidates.Length - 1, (int)(MineChapterSettings.Roll(
                    segment.RunSeed, chunkIndex, key + ":point") * candidates.Length));
                Vector2 point = candidates[pointIndex];
                float z = Mathf.Lerp(slotStart + localBounds.extents.z, slotEnd - localBounds.extents.z, point.y);
                float side = Mathf.Sign(point.x);
                float x = side * Mathf.Max(Mathf.Abs(point.x), 2.5f + localBounds.extents.x);
                Vector3 center = origin + new Vector3(x, 0f, z);
                Quaternion rotation = side < 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                var offset = new Vector3(localBounds.center.x, localBounds.min.y, localBounds.center.z);
                var instance = Object.Instantiate(prefab, center - rotation * offset, rotation, chunk);
                instance.name = "Roadside_" + prefab.name;
            }
        }
    }

    public void UnloadOutsideRange(int minimumChunkIndex, int maximumChunkIndex)
    {
        // 등록된 지형과 배치 정보는 인스턴스보다 오래 유지되어 같은 청크를 다시 생성할 수 있습니다.
        var unloadedIndices = chunkInstances.Keys
            .Where(index => index < minimumChunkIndex || index > maximumChunkIndex).ToArray();
        foreach (int index in unloadedIndices)
        {
            GameObject chunk = chunkInstances[index];
            chunkInstances.Remove(index);
            if (chunk == null) continue;
            chunk.SetActive(false);
            Object.Destroy(chunk);
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
            if (place != null && place.originalChunkIndex == localChunkIndex && ground != null)
            {
                var old = ground.transform.Find($"Place_{place.placeId}");
                if (old != null) old.gameObject.SetActive(false);
            }
            if (place == null || place.chunkIndex != localChunkIndex) continue;
            if (place.storyActors != null)
            {
                if (place.sharedStoryVisualId != null && place.placeId != place.sharedStoryVisualId) continue;
                var group = new GameObject("Place_" + place.placeId).transform;
                group.SetParent(chunkParent, false);
                group.position = segment.Registry.GetWorldPosition(place);
                if (place.authoredLocationId != null && segment.PrefabCatalog != null &&
                    segment.PrefabCatalog.TryGetPrefab(place.authoredLocationId, out var locationPrefab))
                    Object.Instantiate(locationPrefab, group);
                for (int index = 0; index < place.storyActors.Length; index++)
                {
                    var actor = place.storyActors[index];
                    var socket = new GameObject("ActorSocket_" + actor.name).transform;
                    socket.SetParent(group, false);
                    socket.localPosition = place.authoredLocationId != null ? actor.position : new Vector3(0, 0, index * 1.5f);
                    socket.localRotation = actor.idleRotation;
                    // These appearance prefabs face local -Z. The dialogue socket faces +Z toward the player.
                    var model = Object.Instantiate(actor.prefab, socket);
                    model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
                }
                continue;
            }

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
            if (authoredPlace == null && place.originalChunkIndex >= 0)
            {
                var source = segment.GroundPrefabs[place.originalChunkIndex].transform.Find($"Place_{place.placeId}");
                if (source != null)
                {
                    authoredPlace = Object.Instantiate(source, ground != null ? ground.transform : chunkParent);
                    authoredPlace.name = source.name;
                }
            }
            if (authoredPlace != null)
            {
                authoredPlace.gameObject.SetActive(true);
                authoredPlace.SetPositionAndRotation(
                    segment.Registry.GetWorldPosition(place),
                    Quaternion.Euler(place.rotation));
                PositionActorSockets(segment, place, authoredPlace);
                continue;
            }

            // Terrain에 미리 배치되지 않은 오브젝트만 카탈로그 프리팹으로 생성합니다.
            GameObject instance = Object.Instantiate(placePrefab,
                segment.Registry.GetWorldPosition(place), Quaternion.Euler(place.rotation), chunkParent);
            instance.name = $"Place_{place.placeId}_{place.prefabId}";
        }
    }

    private static void PositionActorSockets(TerrainSegment segment, PlaceData place, Transform root)
    {
        if (place.spawnPoints == null || place.spawnPoints.Length == 0) return;
        int first = System.Array.FindIndex(place.spawnPoints, p => Vector3.SqrMagnitude(p - place.position) < 0.0001f);
        int actor = 0;
        foreach (Transform child in root)
        {
            if (!child.name.StartsWith("ActorSocket_", System.StringComparison.Ordinal)) continue;
            Vector3 localPoint = place.actorPositions?.Length > actor ? place.actorPositions[actor] : place.spawnPoints[(System.Math.Max(0, first) + actor) % place.spawnPoints.Length];
            actor++;
            child.position = segment.Registry.GetChunkWorldPosition(place.chunkIndex) + localPoint;
        }
    }
}
