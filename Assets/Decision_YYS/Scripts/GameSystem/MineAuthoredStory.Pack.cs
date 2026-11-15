using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal sealed partial class MineAuthoredStory
{
    internal const int PackVersion = 4;
    internal const float MinimumLocationGap = 12;
    private JObject packFlow, packPool, packIncoming;
    private JArray packChunks;
    private Dictionary<string, JObject> packNpcs;
    private Dictionary<string, JObject[]> packStories;
    private Dictionary<string, JObject> packVariants = new Dictionary<string, JObject>();
    internal bool IsShort { get; }
    internal int? ShortLayoutSeed { get; set; }
    internal static string ModeId(bool isShort) => isShort ? "Short" : "Full";
    internal IEnumerable<string> PackMainOrder => packFlow["main_story_order"].Values<string>();
    internal string PackName(string id) => (string)packNpcs.GetValueOrDefault(id ?? "")?["name"] ?? id;
    internal bool IsPackNpc(string id) => id != null && packNpcs.ContainsKey(id);
    internal JObject PackIncoming(string id) => (JObject)packIncoming[id];
    internal JObject PackPetRoute(string id) => (JObject)packFlow["pet_routes"][id];

    private void LoadPack()
    {
        string configuration = IsShort ? "ShortStories/" : "Stories/";
        packFlow = MineLearningStation.ReadContent(configuration + "00_main_flow");
        packPool = MineLearningStation.ReadContent(configuration + "00_side_pool");
        packIncoming = MineLearningStation.ReadContent(configuration + "00_pet_incoming_dialogues");
        packChunks = (JArray)MineLearningStation.ReadContent("Relationships/청크_순서")["chunks"];
        packNpcs = MineLearningStation.ReadContent("Relationships/NPC_전체목록")["npcs"]
            .Cast<JObject>().ToDictionary(npc => (string)npc["npc_id"]);
        foreach (string name in packNpcs.Keys.Select(PackName))
            if (!actors.ContainsKey(name)) throw new InvalidOperationException("갱도 NPC 프리팹 누락: " + name);
        packStories = Resources.LoadAll<TextAsset>(MineLearningStation.AuthoredContent + "Stories")
            .Where(asset => asset.name.StartsWith("MAIN-", StringComparison.Ordinal) ||
                asset.name.StartsWith("SIDE-", StringComparison.Ordinal))
            .OrderBy(asset => asset.name).Select(asset => JObject.Parse(asset.text))
            .GroupBy(story => (string)story["story_id"]).ToDictionary(group => group.Key, group => group.ToArray());
    }

    internal JObject PackStory(string id, int seed, string variant = null)
    {
        var candidates = packStories[id];
        int picked = IsShort ? 0 : Pick(seed, 0, id, candidates.Length);
        if (variant == null) return candidates[picked];
        string folder = id.StartsWith("MAIN-", StringComparison.Ordinal) ? "CHUNK-" + id.Substring(5) : "SIDE_STORIES";
        string path = "AbilityVariants/" + folder + "/" + variant + "/" + id + "_V" + (picked + 1);
        if (!packVariants.TryGetValue(path, out var story))
            packVariants[path] = story = MineLearningStation.ReadContent(path);
        return story;
    }

    private void ApplyPackLayout(TerrainData terrain, int seed, TerrainPrefabCatalog prefabs, MineNpcPlacement npcPlacement)
    {
        seed = ShortLayoutSeed ?? seed;
        var places = new List<PlaceData>();
        int chunkCount = PackMainOrder.Count();
        Vector3[] spawnPoints = npcPlacement?.LocalStorySpawnPoints() ?? new[]
        {
            new Vector3(-2.6f, 0f, -4f), new Vector3(2.6f, 0f, -4f),
            new Vector3(-2.6f, 0f, 0f), new Vector3(2.6f, 0f, 0f), new Vector3(2.6f, 0f, 4f)
        };
        terrain.facilitySpacing = Mathf.Max(MinimumLocationGap, terrain.facilitySpacing);
        terrain.facilityRules = Array.Empty<TerrainFacilityRule>();
        var sideStories = packPool["eligible_story_ids_by_chunk"].Children<JProperty>()
            .SelectMany(chunk => chunk.Value.Values<string>()).Distinct().ToArray();
        var sideNpcs = packNpcs.Values.Where(npc => (string)npc["role"] == "supporting")
            .Select(npc => (string)npc["npc_id"]).ToArray();
        for (int index = 0; index < chunkCount; index++)
        {
            var chunk = packChunks[index];
            string chunkId = (string)chunk["chunk_id"];
            string main = PackMainOrder.ElementAt(index);
            string mainNpc = (string)chunk["main_npc_id"];
            string mainLocation = (string)chunk["main_npc_fixed_location_id"];
            bool frontShop = index == 0;
            bool backShop = index == chunkCount - 1;
            var stories = new List<(string Id, string EncounterId, string Npc, string Location)>
                { (main, main, mainNpc, mainLocation) };
            var mainCast = packNpcs.Values.Where(npc => (string)npc["fixed_chunk"] == chunkId &&
                    (string)npc["fixed_location_id"] == mainLocation)
                .Select(npc => (string)npc["npc_id"]).Prepend(mainNpc).Distinct().ToArray();
            var eligible = IsShort ? packPool["eligible_story_ids_by_chunk"][chunkId].Values<string>() : sideStories;
            var pickedStories = eligible.OrderBy(id => MineChapterSettings.Roll(seed, index, "story:" + id)).Take(3).ToArray();
            var pickedNpcs = sideNpcs.Except(mainCast)
                .OrderBy(id => MineChapterSettings.Roll(seed, index, "npc:" + id)).Take(3).ToArray();
            int sideCount = 1 + Pick(seed, index, "side-count", 2);
            var occupiedSideSlots = Enumerable.Range(0, pickedStories.Length)
                .OrderBy(slot => MineChapterSettings.Roll(seed, index, "side-slot:" + slot))
                .Take(sideCount).ToHashSet();
            for (int side = 0; side < pickedNpcs.Length; side++)
            {
                string npc = pickedNpcs[side];
                string locationId = (string)packNpcs[npc]["fixed_location_id"];
                if (occupiedSideSlots.Contains(side))
                {
                    string storyId = pickedStories[side];
                    stories.Add((storyId, storyId + "@" + chunkId, npc, locationId));
                }
                else stories.Add((null, $"{chunkId}-place-{side + 1}", null, locationId));
            }
            var locations = stories.Select(story =>
            {
                var actorIds = story.Id == main ? mainCast : story.Npc != null ? new[] { story.Npc } : Array.Empty<string>();
                var actorList = actorIds.Select(id =>
                    new PlaceStoryActor { name = PackName(id), prefab = actors[PackName(id)] }).ToArray();
                string visual = story.Location switch { "LOC-13" => "LOC-12", "LOC-14" => "LOC-01", "LOC-15" => "LOC-10", _ => story.Location };
                prefabs.TryGetPrefab(visual, out var prefab);
                Vector2 depth = RenderedDepth(prefab);
                var actorBounds = actorList.Select(actor => RenderedBounds(actor.prefab)).ToArray();
                var roomBounds = RenderedBounds(prefab);
                var candidates = spawnPoints.Select(point => new Vector3(
                    roomBounds.center.x + point.x * Mathf.Min(1f, roomBounds.extents.x / 4f), point.y,
                    roomBounds.center.z + point.z * Mathf.Min(1f, roomBounds.extents.z / 5.5f)))
                    .Select((point, slot) => (point, slot))
                    .OrderBy(candidate => MineChapterSettings.Roll(seed, index, story.Id + ":npc:" + candidate.slot))
                    .Select(candidate => candidate.point).ToArray();
                var taken = new List<(Vector3 Center, float Radius)>();
                for (int actor = 0; actor < actorList.Length; actor++)
                {
                    var bounds = actorBounds[actor];
                    float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude + .15f;
                    float Clearance(Vector3 candidate) => taken.Select(other =>
                        Vector3.Distance(candidate, other.Center) - radius - other.Radius)
                        .DefaultIfEmpty(float.MaxValue).Min();
                    var point = candidates.OrderByDescending(candidate => Mathf.Min(0f, Clearance(candidate)))
                        .First();
                    taken.Add((point, radius));
                    actorList[actor].position = new Vector3(point.x + bounds.center.x, point.y - bounds.min.y,
                        point.z + bounds.center.z);
                    depth.x = Mathf.Min(depth.x, point.z - radius);
                    depth.y = Mathf.Max(depth.y, point.z + radius);
                }
                for (int actor = 0; actor < actorList.Length; actor++)
                {
                    int partner = actor ^ 1;
                    Vector3 target = partner < actorList.Length ? actorList[partner].position : roomBounds.center;
                    Vector3 direction = target - actorList[actor].position;
                    direction.y = 0;
                    if (direction.sqrMagnitude < .001f) direction = Vector3.back;
                    float turn = (float)MineChapterSettings.Roll(seed, index, story.Id + ":idle:" + actor);
                    actorList[actor].idleRotation = Quaternion.LookRotation(direction) *
                        Quaternion.Euler(0f, Mathf.Lerp(-12f, 12f, turn), 0f);
                }
                return (Story: story, Actors: actorList, Depth: depth, Visual: visual);
            }).ToList();

            float clearance = MinimumLocationGap;
            float used = locations.Sum(location => location.Depth.y - location.Depth.x) +
                (locations.Count - 1) * clearance + MinimumLocationGap;
            if (used > terrain.chunkLength)
                throw new InvalidOperationException($"갱도 청크 {chunkId}의 장소 외곽과 간격에 {used:F1}유닛이 필요합니다.");
            float extra = (terrain.chunkLength - used) / (locations.Count + 1);
            float edge = MinimumLocationGap * .5f + extra;
            for (int slot = 0; slot < locations.Count; slot++)
            {
                var location = locations[slot];
                float z = edge - location.Depth.x;
                var story = location.Story;
                places.Add(new PlaceData
                {
                    placeId = story.EncounterId, characterId = story.Npc == null ? null : PackName(story.Npc),
                    connectStoryCards = story.Id == null ? null : Prefix + story.Id,
                    chunkIndex = index, position = new Vector3(0, 0, z),
                    storyActors = location.Actors, sharedStoryVisualId = story.EncounterId,
                    storyLocationId = story.Location, storyNpcId = story.Npc,
                    authoredLocationId = location.Visual, storyLocationDepth = location.Depth,
                    storyLocationShop = (frontShop && slot == 0) || (backShop && slot == locations.Count - 1)
                });
                edge = z + location.Depth.y + clearance + extra;
            }
        }
        terrain.places = places.ToArray();
        terrain.groundIds = Enumerable.Range(0, chunkCount)
            .Select(index => index == 0 ? terrain.groundId : "mine_passage_180").ToArray();
        terrain.chunkCount = chunkCount;
    }

    private static Vector2 RenderedDepth(GameObject prefab)
    {
        var bounds = RenderedBounds(prefab);
        return new Vector2(Mathf.Min(0, bounds.min.z), Mathf.Max(0, bounds.max.z));
    }

    internal static Bounds RenderedBounds(GameObject prefab)
    {
        Bounds result = default;
        bool found = false;
        Matrix4x4 inverse = prefab.transform.worldToLocalMatrix;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            var bounds = renderer.localBounds;
            var matrix = inverse * renderer.localToWorldMatrix;
            Vector3 half = new Vector3(
                Mathf.Abs(matrix.m00) * bounds.extents.x + Mathf.Abs(matrix.m01) * bounds.extents.y + Mathf.Abs(matrix.m02) * bounds.extents.z,
                Mathf.Abs(matrix.m10) * bounds.extents.x + Mathf.Abs(matrix.m11) * bounds.extents.y + Mathf.Abs(matrix.m12) * bounds.extents.z,
                Mathf.Abs(matrix.m20) * bounds.extents.x + Mathf.Abs(matrix.m21) * bounds.extents.y + Mathf.Abs(matrix.m22) * bounds.extents.z);
            var local = new Bounds(matrix.MultiplyPoint3x4(bounds.center), 2f * half);
            if (found) result.Encapsulate(local);
            else result = local;
            found = true;
        }
        return result;
    }
}
