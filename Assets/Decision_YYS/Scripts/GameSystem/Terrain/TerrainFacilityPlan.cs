using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Reserves deterministic chapter facilities without instantiating unapproved content.</summary>
internal static class TerrainFacilityPlan
{
    internal sealed class History
    {
        internal readonly int FirstChunk;
        internal readonly Dictionary<string, int> LastPlaced = new Dictionary<string, int>();

        internal History(int firstChunk) => FirstChunk = firstChunk;
    }

    internal readonly struct Slot
    {
        internal readonly string Id, Label, Kind;
        internal readonly int Chunk;
        internal readonly Vector2 Position;
        internal readonly bool OptionalCandidate;
        internal float X => Position.x;
        internal float Z => Position.y;

        internal Slot(TerrainFacilityRule rule, int chunk, Vector2 position, bool optionalCandidate)
        {
            Id = rule.id;
            Label = string.IsNullOrWhiteSpace(rule.label) ? rule.id : rule.label;
            Kind = rule.kind;
            Chunk = chunk;
            Position = position;
            OptionalCandidate = optionalCandidate;
        }
    }

    internal static int LocationLimit(TerrainData data) => Mathf.Clamp(data.facilityMajorLimit, 1, 4);

    internal static bool FitAuthoredPlaces(TerrainData data, bool chapterStart)
    {
        int limit = LocationLimit(data);
        int firstReserved = chapterStart ? data.facilityRules?.Count(rule => rule.firstChunk) ?? 0 : 0;
        var pending = new Queue<PlaceData>();
        for (int chunk = 0; chunk < data.chunkCount; chunk++)
        {
            var authored = data.places.Where(place => place.chunkIndex == chunk).OrderBy(place => place.position.z).ToArray();
            int capacity = limit - (chunk == 0 ? firstReserved : 0);
            // Authored places already in the receiving chunk keep their order and authored visuals.
            foreach (var overflow in authored.Skip(Mathf.Max(0, capacity)))
                pending.Enqueue(overflow);
            for (int vacant = capacity - authored.Length; vacant > 0 && pending.Count > 0; vacant--)
            {
                PlaceData moved = pending.Dequeue();
                moved.originalChunkIndex = moved.chunkIndex;
                moved.chunkIndex = chunk;
            }
        }
        return pending.Count == 0 && firstReserved <= limit;
    }

    internal static Slot[] Build(TerrainData data, int seed, int firstChunk, History history, bool capLocations = false)
    {
        if (data.facilityRules == null || data.facilityRules.Length == 0 || data.chunkCount <= 0)
            return Array.Empty<Slot>();

        var slots = new List<Slot>();
        var chunks = Enumerable.Range(0, data.chunkCount).ToArray();
        var authored = chunks.Select(chunk => data.places.Where(place => place.chunkIndex == chunk).ToArray()).ToArray();
        var occupied = authored.Select(places => places.SelectMany(place =>
                new[] { place.position }.Concat(place.actorPositions ?? Array.Empty<Vector3>()))
            .Select(point => new Vector2(point.x, point.z)).ToList()).ToArray();
        var optionalCounts = new int[data.chunkCount];
        var locationCounts = authored.Select(places => places.Length).ToArray();
        float spacing = Mathf.Max(1, data.facilitySpacing);
        float edge = Mathf.Max(3, spacing * .5f);

        foreach (var rule in data.facilityRules.OrderByDescending(rule => rule.priority)
            .ThenBy(rule => capLocations && rule.oncePerChapter ? MineChapterSettings.Roll(seed, history.FirstChunk, rule.id + ":priority") : 0))
        {
            bool TryReserve(int chunk, bool atFront, bool atEnd)
            {
                if (capLocations && locationCounts[chunk] >= LocationLimit(data)) return false;
                float firstPlace = authored[chunk].Select(place => place.position.z).DefaultIfEmpty(data.chunkLength).Min();
                float low = atEnd && !atFront ? Mathf.Max(edge, data.chunkLength - edge - spacing) : edge;
                if (!atFront && firstChunk + chunk == history.FirstChunk)
                    low = Mathf.Max(low, slots.Where(slot => slot.Chunk == chunk && slot.Id == "shop")
                        .Select(slot => slot.Z + 1f).DefaultIfEmpty(edge).Max());
                float high = atFront ? Mathf.Min(firstPlace - 1, data.chunkLength - edge) : data.chunkLength - edge;
                int positions = Mathf.FloorToInt(high - low) + 1;
                if (positions <= 0) return false;
                int global = firstChunk + chunk;
                int proposal = (int)(MineChapterSettings.Roll(seed, global, rule.id + ":position") * positions);
                float lane = spacing;
                bool left = MineChapterSettings.Roll(seed, global, rule.id + ":side") < .5;
                for (int offset = 0; offset < positions; offset++)
                    if (TryPosition((proposal + offset) % positions)) return true;
                return false;

                bool TryPosition(int index)
                {
                    var point = new Vector2(left ? -lane : lane, low + index);
                    if (occupied[chunk].Any(other => (other - point).sqrMagnitude < spacing * spacing))
                    {
                        point.x = -point.x;
                        if (occupied[chunk].Any(other => (other - point).sqrMagnitude < spacing * spacing)) return false;
                    }
                    int authoredEncounters = authored[chunk].Count(place => !string.IsNullOrWhiteSpace(place.connectStoryCards));
                    bool optional = optionalCounts[chunk] < data.facilityOptionalLimit &&
                        authoredEncounters + optionalCounts[chunk] < data.facilityMajorLimit;
                    if (optional) optionalCounts[chunk]++;
                    locationCounts[chunk]++;
                    occupied[chunk].Add(point);
                    slots.Add(new Slot(rule, chunk, point, optional));
                    history.LastPlaced[rule.id] = global;
                    return true;
                }
            }

            bool seen = history.LastPlaced.TryGetValue(rule.id, out int last);
            if (rule.oncePerChapter)
            {
                if (seen) continue;
                var candidates = chunks.OrderBy(chunk => slots.Count(slot => slot.Chunk == chunk && slot.Kind == rule.kind))
                    .ThenBy(chunk => MineChapterSettings.Roll(seed, firstChunk + chunk, rule.id + ":chunk"));
                if (!candidates.Any(chunk => TryReserve(chunk, false, false)) && !capLocations)
                    Warn(rule, firstChunk, spacing);
                continue;
            }

            foreach (int chunk in Schedule(rule, seed, firstChunk, data.chunkCount, history.FirstChunk, seen ? last : null))
                if (!TryReserve(chunk, rule.firstChunk && firstChunk + chunk == history.FirstChunk,
                        rule.lastChunk && chunk == data.chunkCount - 1) &&
                    (!capLocations || rule.firstChunk || rule.lastChunk))
                    Warn(rule, firstChunk + chunk, spacing);
        }

        return slots.OrderBy(slot => slot.Chunk).ThenBy(slot => slot.Z).ToArray();
    }

    private static IEnumerable<int> Schedule(TerrainFacilityRule rule, int seed, int firstChunk, int count,
        int chapterStart, int? last)
    {
        int end = firstChunk + count - 1;
        int firstMin = Mathf.Max(1, rule.firstMinimumChunk);
        int firstMax = Mathf.Max(firstMin, rule.firstMaximumChunk);
        int next = last.HasValue ? Next(last.Value) : rule.firstChunk ? chapterStart :
            chapterStart - 1 + Pick(firstMin, firstMax, chapterStart, ":first");
        while (next < firstChunk && next != int.MaxValue) next = Next(next);
        int latest = -1;
        while (next <= end)
        {
            latest = next;
            yield return next - firstChunk;
            next = Next(next);
        }
        if (rule.lastChunk && latest != end) yield return count - 1;

        int Next(int current) => rule.minimumChunkGap > 0
            ? current + Pick(rule.minimumChunkGap, rule.maximumChunkGap, current, ":gap") : int.MaxValue;
        int Pick(int min, int max, int chunk, string suffix) =>
            min + (int)(MineChapterSettings.Roll(seed, chunk, rule.id + suffix) * (max - min + 1));
    }

    private static void Warn(TerrainFacilityRule rule, int chunk, float spacing) =>
        Debug.LogWarning($"[장소 계획] {rule.id}: 청크 {chunk + 1} · 최소 간격 {spacing:F1}을 만족하는 빈 자리가 없습니다.");
}
