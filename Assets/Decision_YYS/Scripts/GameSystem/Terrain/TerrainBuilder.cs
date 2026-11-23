using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// 여러 챕터의 지형을 하나의 전역 청크 공간에 생성하고 재사용합니다.
/// </summary>
public sealed class TerrainBuilder
{
    internal enum MapPointKind { Place, Shop, RoadsideNpc }

    internal readonly struct MapPoint
    {
        internal readonly Transform Target;
        internal readonly MapPointKind Kind;

        internal MapPoint(Transform target, MapPointKind kind)
        {
            Target = target;
            Kind = kind;
        }
    }

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
    private readonly Dictionary<int, List<MapPoint>> chunkMapPoints = new Dictionary<int, List<MapPoint>>();
    private readonly Dictionary<string, TerrainFacilityPlan.History> facilityHistory = new Dictionary<string, TerrainFacilityPlan.History>();
    private readonly List<FacilityMarker> facilityMarkers = new List<FacilityMarker>();
    internal IReadOnlyList<FacilityMarker> FacilityMarkers => facilityMarkers;
    internal IEnumerable<List<MapPoint>> LoadedMapPoints => chunkMapPoints.Values;
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
    private const string CentralRoadsideKey = "npc:center";
    private const float RoadsideEncounterReach = 6.5f;
    private const float ShortcutEncounterReach = 1.6f;

    internal bool TryGetShortcutRoute(TerrainPlaceRegistry registry, PlaceData place, int side,
        float height, float encounterStopDistance, out Vector3[] route, out Transform actor)
    {
        route = null;
        actor = null;
        var location = FindStoryPlace(registry, place);
        if (location == null || !TryWorldBounds(location, out var bounds)) return false;

        Vector3 origin = registry.GetChunkWorldPosition(place.chunkIndex);
        float entryZ = bounds.min.z - 2f, exitZ = bounds.max.z + 2f;
        const float clearance = .55f;
        var blockers = new List<Bounds>();
        var roadsideActors = new List<Transform>();
        foreach (Transform child in location.parent)
        {
            if (child == location || !child.gameObject.activeInHierarchy) continue;
            if (child.name.StartsWith("RoadsideNpc_", System.StringComparison.Ordinal))
                roadsideActors.Add(child);
            else if ((child.name.StartsWith("Place_", System.StringComparison.Ordinal) ||
                child.name.StartsWith("Roadside_", System.StringComparison.Ordinal)) &&
                TryWorldBounds(child, out var obstacle)) blockers.Add(obstacle);
        }

        bool Clear(Vector3 a, Vector3 b) => !blockers.Any(blocker =>
            Mathf.Min(a.x, b.x) < blocker.max.x + clearance && Mathf.Max(a.x, b.x) > blocker.min.x - clearance &&
            Mathf.Min(a.z, b.z) < blocker.max.z + clearance && Mathf.Max(a.z, b.z) > blocker.min.z - clearance);

        bool InShortcutReach(Vector3 position, float laneX)
        {
            if ((position.x - origin.x) * side <= 0f) return false;

            // An actor just beyond a turn can still be within reach of the chosen side of the route.
            float dx = position.x - laneX;
            float dz = position.z - Mathf.Clamp(position.z, entryZ, exitZ);
            return dx * dx + dz * dz <= ShortcutEncounterReach * ShortcutEncounterReach;
        }

        float edge = side < 0 ? bounds.min.x : bounds.max.x;
        var exit = new Vector3(origin.x, height, exitZ);
        for (float margin = 1.25f; margin <= 3.25f; margin += .5f)
        {
            float x = edge + side * margin;
            if ((x - origin.x) * side < 4.25f || Mathf.Abs(x - origin.x) > 10.5f) continue;
            var candidateActor = roadsideActors.Where(candidate =>
                    InShortcutReach(candidate.position, x))
                .OrderBy(candidate => candidate.position.z).FirstOrDefault();
            float approachZ = candidateActor == null ? entryZ :
                Mathf.Min(entryZ, candidateActor.position.z - encounterStopDistance);
            var entry = new Vector3(origin.x, height, approachZ);
            var sideEntry = new Vector3(x, height, approachZ);
            var sideExit = new Vector3(x, height, exitZ);
            if (!Clear(entry, sideEntry) || !Clear(sideEntry, sideExit) || !Clear(sideExit, exit)) continue;
            if (candidateActor == null)
            {
                route ??= new[] { entry, sideEntry, sideExit, exit };
                continue;
            }
            route = new[] { entry, sideEntry, sideExit, exit };
            actor = candidateActor;
            return true;
        }
        return route != null;
    }

    private static bool TryWorldBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            if (found) bounds.Encapsulate(renderer.bounds);
            else bounds = renderer.bounds;
            found = true;
        }
        return found;
    }

    internal Transform FindRoadsideNpc(TerrainPlaceRegistry registry, float routeX, float fromZ, float toZ,
        float destinationZ, float stopDistance, ISet<string> resolved, out string encounterId)
    {
        encounterId = null;
        Transform nearest = null;
        float nearestTriggerZ = float.PositiveInfinity;
        var segment = segments.Find(candidate => candidate.Registry == registry);
        if (segment == null) return null;
        foreach (var entry in chunkInstances)
        {
            if (entry.Key < segment.FirstGlobalChunkIndex ||
                entry.Key >= segment.FirstGlobalChunkIndex + segment.ChunkCount ||
                !entry.Value || !entry.Value.activeInHierarchy) continue;
            string chunkEncounterId = "roadside:" + entry.Key;
            foreach (Transform child in entry.Value.transform)
            {
                if (!child.gameObject.activeInHierarchy ||
                    !child.name.StartsWith(RoadsideEncounterPrefix, System.StringComparison.Ordinal) ||
                    Mathf.Abs(child.position.x - routeX) > RoadsideEncounterReach) continue;
                float z = child.position.z;
                if (z < fromZ || z >= destinationZ) continue;
                bool central = child.name.StartsWith(RoadsideEncounterPrefix + CentralRoadsideKey + "_",
                    System.StringComparison.Ordinal);
                // A flank encounter must not consume the separate NPC on the through route.
                string id = central ? chunkEncounterId + ":center" : chunkEncounterId;
                if (resolved.Contains(id)) continue;
                float triggerZ = central ? Mathf.Max(fromZ, z - stopDistance) : z;
                if (triggerZ <= toZ && triggerZ < nearestTriggerZ)
                {
                    nearest = child;
                    nearestTriggerZ = triggerZ;
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
        chunkMapPoints.Remove(globalChunkIndex);

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
        var mapPoints = new List<MapPoint>();
        CreatePlaces(segment, localChunkIndex, chunk.transform, ground, mapPoints);
        CreateMineRoadsideDecorations(segment, localChunkIndex, chunk.transform, mapPoints);
        foreach (var slot in segment.Facilities)
            if (slot.Chunk == localChunkIndex)
                Debug.Log($"[장소 예정] {slot.Label} ({slot.Id}) · 청크 {globalChunkIndex + 1} · 로컬 X={slot.X:F1}, Z={slot.Z:F1} · {(slot.OptionalCandidate ? "주요 선택 후보" : "빈 자리 예약")} · 에셋/상호작용 연결 대기");
        chunkInstances.Add(globalChunkIndex, chunk);
        chunkMapPoints.Add(globalChunkIndex, mapPoints);
        return true;
    }

    private static void CreateMineRoadsideDecorations(TerrainSegment segment, int chunkIndex, Transform chunk,
        List<MapPoint> mapPoints)
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
            if (!TryWorldBounds(child, out var placeBounds)) continue;
            places.Add(placeBounds);
            occupied.Add(placeBounds);
        }
        foreach (var slot in segment.Facilities.Where(slot => slot.Chunk == chunkIndex))
            occupied.Add(new Bounds(origin + new Vector3(slot.X, 1f, slot.Z), new Vector3(12f, 2f, 12f)));

        // Decorations and flank NPCs stay clear of the walking and collector strip.
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
        bool Fits(Bounds bounds, float outerLimit, bool central)
        {
            if (bounds.min.z < origin.z + 6f || bounds.max.z > origin.z + segment.Data.chunkLength - 6f ||
                bounds.min.x < origin.x - outerLimit || bounds.max.x > origin.x + outerLimit)
                return false;
            float coreHalfWidth = central ? .65f : corridorHalfWidth;
            if (bounds.min.x < origin.x + coreHalfWidth && bounds.max.x > origin.x - coreHalfWidth)
                return false;
            return !occupied.Any(other => bounds.min.x < other.max.x + clearance &&
                bounds.max.x > other.min.x - clearance && bounds.min.z < other.max.z + clearance &&
                bounds.max.z > other.min.z - clearance);
        }

        bool Place(IReadOnlyList<GameObject> prefabs, string key, IEnumerable<Vector3> candidates,
            bool npc, HashSet<GameObject> used, float startZ, float endZ,
            float outerLimit = placementHalfWidth, bool central = false)
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
                    Quaternion rotation = Quaternion.Euler(0f, central ? 0f : npc ? facing + 180f : facing, 0f);
                    Vector3 size = central ? localBounds.size :
                        new Vector3(localBounds.size.z, localBounds.size.y, localBounds.size.x);
                    var bounds = new Bounds(point + Vector3.up * localBounds.extents.y, size);
                    if (bounds.min.z < startZ || bounds.max.z > endZ || !Fits(bounds, outerLimit, central)) continue;
                    var offset = new Vector3(localBounds.center.x, localBounds.min.y, localBounds.center.z);
                    var instance = Object.Instantiate(prefab, point - rotation * offset, rotation, chunk);
                    bool betweenPlaces = npc && gaps.Any(gap =>
                        bounds.min.z >= gap.start && bounds.max.z <= gap.end);
                    string prefix = betweenPlaces ? RoadsideEncounterPrefix : npc ? "RoadsideNpc_" : "Roadside_";
                    instance.name = prefix + key + "_" + prefab.name;
                    if (npc) mapPoints.Add(new MapPoint(instance.transform, MapPointKind.RoadsideNpc));
                    occupied.Add(bounds);
                    used.Add(prefab);
                    return true;
                }
            }
            return false;
        }

        var usedNpcs = new HashSet<GameObject>();
        int npcCount = 1 + Mathf.Min(1, (int)(Roll("roadside-npc:count") * 2));
        int placedNpcs = 0;
        foreach (var (startZ, endZ) in gaps.OrderByDescending(gap => gap.end - gap.start))
        {
            var points = new List<Vector3>();
            for (float z = startZ + 2f; z <= endZ - 2f; z += 3f)
            {
                points.Add(new Vector3(origin.x - 1.65f, origin.y, z));
                points.Add(new Vector3(origin.x + 1.65f, origin.y, z));
            }
            if (!Place(catalog.MineRoadsideNpcs, CentralRoadsideKey, points, true, usedNpcs,
                startZ, endZ, central: true)) continue;
            placedNpcs++;
            break;
        }
        var npcPoints = Candidates(chunkStart, chunkEnd);
        var sides = new[] { -1, 1 }.OrderBy(side => Roll("roadside-npc:side:" + side)).ToArray();
        foreach (int side in sides)
        {
            if (placedNpcs >= npcCount) break;
            if (Place(catalog.MineRoadsideNpcs, "npc:" + side,
                npcPoints.Where(point => Mathf.Sign(point.x - origin.x) == side),
                true, usedNpcs, chunkStart, chunkEnd)) placedNpcs++;
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
            chunkMapPoints.Remove(index);
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
        GameObject ground,
        List<MapPoint> mapPoints)
    {
        var shopVisuals = new HashSet<string>(segment.Data.places
            .Where(place => place != null && place.storyLocationShop)
            .Select(place => place.sharedStoryVisualId ?? place.placeId));

        foreach (PlaceData place in segment.Data.places)
        {
            if (place != null && place.originalChunkIndex == localChunkIndex && ground != null)
            {
                var old = ground.transform.Find($"Place_{place.placeId}");
                if (old != null) old.gameObject.SetActive(false);
            }
            if (place == null || place.chunkIndex != localChunkIndex) continue;
            var markerKind = shopVisuals.Contains(place.sharedStoryVisualId ?? place.placeId)
                ? MapPointKind.Shop
                : MapPointKind.Place;
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
                if (group.childCount > 0) mapPoints.Add(new MapPoint(group, markerKind));
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
                mapPoints.Add(new MapPoint(authoredPlace, markerKind));
                continue;
            }

            // Terrain에 미리 배치되지 않은 오브젝트만 카탈로그 프리팹으로 생성합니다.
            GameObject instance = Object.Instantiate(placePrefab,
                segment.Registry.GetWorldPosition(place), Quaternion.Euler(place.rotation), chunkParent);
            instance.name = $"Place_{place.placeId}_{place.prefabId}";
            mapPoints.Add(new MapPoint(instance.transform, markerKind));
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
