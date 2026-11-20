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

    private const string RoadsideEncounterPrefix = "RoadsideNpc_Gap_";
    private const float RoadsideEncounterReach = 6.5f;

    internal Transform FindRoadsideNpc(TerrainPlaceRegistry registry, float routeX, float fromZ, float toZ,
        ISet<string> resolved, out string encounterId)
    {
        encounterId = null;
        Transform nearest = null;
        var segment = segments.Find(candidate => candidate.Registry == registry);
        if (segment == null) return null;
        foreach (var entry in chunkInstances)
        {
            if (entry.Key < segment.FirstGlobalChunkIndex ||
                entry.Key >= segment.FirstGlobalChunkIndex + segment.ChunkCount ||
                !entry.Value || !entry.Value.activeInHierarchy) continue;
            string id = "roadside:" + entry.Key;
            if (resolved.Contains(id)) continue;
            foreach (Transform child in entry.Value.transform)
            {
                if (!child.gameObject.activeInHierarchy ||
                    !child.name.StartsWith(RoadsideEncounterPrefix, System.StringComparison.Ordinal) ||
                    Mathf.Abs(child.position.x - routeX) > RoadsideEncounterReach) continue;
                float z = child.position.z;
                if (z >= fromZ && z <= toZ && (nearest == null || z < nearest.position.z))
                {
                    nearest = child;
                    encounterId = id;
                }
            }
        }
        return nearest;
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
        string groundId = segment.Data.groundIds?.ElementAtOrDefault(chunkIndex) ?? segment.Data.groundId;
        if (groundId != "mine_village_180" && groundId != "mine_passage_180") return;
        var catalog = segment.PrefabCatalog;
        if (catalog == null) return;

        Vector3 origin = segment.Registry.GetChunkWorldPosition(chunkIndex);
        var occupied = new List<Bounds>();
        var places = new List<Bounds>();
        foreach (Transform child in chunk)
        {
            if (!child.name.StartsWith("Place_", System.StringComparison.Ordinal)) continue;
            Bounds? placeBounds = null;
            foreach (var renderer in child.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                var bounds = placeBounds ?? renderer.bounds;
                bounds.Encapsulate(renderer.bounds);
                placeBounds = bounds;
            }
            if (!placeBounds.HasValue) continue;
            places.Add(placeBounds.Value);
            occupied.Add(placeBounds.Value);
        }
        foreach (var slot in segment.Facilities.Where(slot => slot.Chunk == chunkIndex))
            occupied.Add(new Bounds(origin + new Vector3(slot.X, 1f, slot.Z), new Vector3(12f, 2f, 12f)));

        // The central strip includes both walking and the widest collector formation.
        const float corridorHalfWidth = 4.25f, preferredDecorationHalfWidth = 7.25f;
        const float placementHalfWidth = 10.5f, clearance = .75f;
        float chunkStart = origin.z + 6f;
        float chunkEnd = origin.z + segment.Data.chunkLength - 6f;
        var gaps = new List<(float start, float end)>();
        float previousEnd = float.NegativeInfinity;
        foreach (var place in places.OrderBy(bounds => bounds.min.z))
        {
            float start = Mathf.Max(previousEnd, chunkStart);
            float end = Mathf.Min(place.min.z, chunkEnd);
            if (!float.IsNegativeInfinity(previousEnd) && end - start >= 3f)
                gaps.Add((start, end));
            previousEnd = Mathf.Max(previousEnd, place.max.z);
        }
        Vector3[] Candidates(float startZ, float endZ)
        {
            var points = new List<Vector3>();
            for (float z = startZ + 1.5f; z <= endZ - 1.5f; z += 3f)
            for (float x = 5.5f; x <= 8.5f; x += 1f)
            {
                points.Add(new Vector3(origin.x - x, origin.y, z));
                points.Add(new Vector3(origin.x + x, origin.y, z));
            }
            return points.ToArray();
        }

        float Roll(string key) => (float)MineChapterSettings.Roll(segment.RunSeed, chunkIndex, key);
        bool Fits(Bounds bounds, float outerLimit)
        {
            if (bounds.min.z < origin.z + 6f || bounds.max.z > origin.z + segment.Data.chunkLength - 6f ||
                bounds.min.x < origin.x - outerLimit || bounds.max.x > origin.x + outerLimit ||
                bounds.min.x < origin.x + corridorHalfWidth && bounds.max.x > origin.x - corridorHalfWidth)
                return false;
            return !occupied.Any(other => bounds.min.x < other.max.x + clearance &&
                bounds.max.x > other.min.x - clearance && bounds.min.z < other.max.z + clearance &&
                bounds.max.z > other.min.z - clearance);
        }

        bool Place(IReadOnlyList<GameObject> prefabs, string key, IEnumerable<Vector3> candidates,
            bool npc, HashSet<GameObject> used, float startZ, float endZ, float outerLimit = placementHalfWidth)
        {
            if (prefabs == null) return false;
            var points = candidates.OrderBy(point => Roll(key + ":point:" +
                Mathf.RoundToInt((point.x - origin.x) * 2f) + ":" +
                Mathf.RoundToInt((point.z - origin.z) * 2f))).ToArray();
            var choices = prefabs.Where(prefab => prefab != null)
                .OrderBy(prefab => used.Contains(prefab))
                .ThenBy(prefab => Roll(key + ":appearance:" + prefab.name));
            foreach (var prefab in choices)
            {
                var localBounds = MineAuthoredStory.RenderedBounds(prefab);
                if (localBounds.size.sqrMagnitude < .001f) continue;
                foreach (var point in points)
                {
                    float facing = point.x < origin.x ? 90f : -90f;
                    // Supplied appearance models face local -Z.
                    Quaternion rotation = Quaternion.Euler(0f, npc ? facing + 180f : facing, 0f);
                    Vector3 size = new Vector3(localBounds.size.z, localBounds.size.y, localBounds.size.x);
                    var bounds = new Bounds(point + Vector3.up * localBounds.extents.y, size);
                    if (bounds.min.z < startZ || bounds.max.z > endZ || !Fits(bounds, outerLimit)) continue;
                    var offset = new Vector3(localBounds.center.x, localBounds.min.y, localBounds.center.z);
                    var instance = Object.Instantiate(prefab, point - rotation * offset, rotation, chunk);
                    bool betweenPlaces = npc && gaps.Any(gap =>
                        bounds.min.z >= gap.start && bounds.max.z <= gap.end);
                    string prefix = betweenPlaces ? RoadsideEncounterPrefix : npc ? "RoadsideNpc_" : "Roadside_";
                    instance.name = prefix + key + "_" + prefab.name;
                    occupied.Add(bounds);
                    used.Add(prefab);
                    return true;
                }
            }
            return false;
        }

        var usedNpcs = new HashSet<GameObject>();
        int npcCount = 1 + Mathf.Min(1, (int)(Roll("roadside-npc:count") * 2));
        var npcPoints = Candidates(chunkStart, chunkEnd);
        var sides = new[] { -1, 1 }.OrderBy(side => Roll("roadside-npc:side:" + side)).ToArray();
        foreach (int side in sides)
        {
            Place(catalog.MineRoadsideNpcs, "npc:" + side,
                npcPoints.Where(point => Mathf.Sign(point.x - origin.x) == side),
                true, usedNpcs, chunkStart, chunkEnd);
            if (usedNpcs.Count >= npcCount) break;
        }

        var usedDecorations = new HashSet<GameObject>();
        int gap = 0;
        foreach (var (startZ, endZ) in gaps)
        {
            var points = Candidates(startZ, endZ);
            string key = "decoration:" + gap++;
            int count = 1 + Mathf.Min(2, (int)(Roll(key + ":count") * 3));
            for (int i = 0; i < count; i++)
            {
                string itemKey = key + ":" + i;
                if (Place(catalog.MineRoadsideDecorations, itemKey, points,
                    false, usedDecorations, startZ, endZ, preferredDecorationHalfWidth)) continue;
                if (!Place(catalog.MineRoadsideDecorations, itemKey, points,
                    false, usedDecorations, startZ, endZ)) break;
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
