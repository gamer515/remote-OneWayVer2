using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Plans visible rooms separately from the chapter's facility schedule.</summary>
internal sealed class TerrainRoomLayout
{
    private readonly struct Room
    {
        internal readonly Transform Source;
        internal readonly float Before, After;
        internal readonly int Index;

        internal Room(Transform source, int index)
        {
            Source = source;
            Index = index;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var mesh in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.sharedMesh == null) continue;
                Bounds local = mesh.sharedMesh.bounds;
                Matrix4x4 transform = source.parent.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    bounds.Encapsulate(transform.MultiplyPoint3x4(local.center +
                        Vector3.Scale(local.extents, new Vector3(x, y, z))) - source.localPosition);
            }
            Before = -bounds.min.z;
            After = bounds.max.z;
        }
    }

    private readonly string groupPath, companionPath;
    private readonly bool useTemplates;
    private readonly Room[] rooms;
    private readonly Dictionary<int, float> offsets = new Dictionary<int, float>();

    internal TerrainRoomLayout(Transform sourceGroup, string groupPath, string companionPath,
        bool useTemplates, IEnumerable<float> requiredCenters, IEnumerable<float> relocatedCenters,
        int limit, float length, float spacing, int seed, int chunk)
    {
        this.groupPath = groupPath;
        this.companionPath = companionPath;
        this.useTemplates = useTemplates;
        rooms = sourceGroup.Cast<Transform>().Select((room, index) => new Room(room, index))
            .OrderBy(room => room.Source.localPosition.z).ToArray();

        float free = length;
        float approach = Mathf.Max(spacing, length * .1f);
        float departure = spacing * .5f;
        var required = requiredCenters.Select(NearestRoom).Select(room => room.Index).ToHashSet();
        var relocated = relocatedCenters.Select(NearestRoom).Select(room => room.Index).ToHashSet();
        var selected = rooms.Where(room => !relocated.Contains(room.Index)).ToList();
        int count = useTemplates && limit > 3 && Roll("count") < .5f ? 3 : limit;
        while (selected.Count > count && RemoveOptional()) { }
        while (selected.Count > 0)
        {
            free = length - approach - departure - spacing * (selected.Count - 1) -
                selected.Sum(room => room.Before + room.After);
            if (free >= 0) break;
            if (!RemoveOptional()) return;
        }
        if (selected.Count == 0) return;

        var weights = Enumerable.Range(0, selected.Count + 1)
            .Select(index => .15f + Roll("gap:" + index) * (index % 2 == 0 ? 3f : 1f)).ToArray();
        float unit = free / weights.Sum();
        float cursor = approach;
        for (int i = 0; i < selected.Count; i++)
        {
            Room room = selected[i];
            float center = cursor + room.Before + weights[i] * unit;
            offsets.Add(room.Index, center - room.Source.localPosition.z);
            cursor = center + room.After + spacing;
        }

        bool RemoveOptional()
        {
            var optional = selected.Where(room => !required.Contains(room.Index)).ToArray();
            if (optional.Length == 0) return false;
            int index = Mathf.Min(optional.Length - 1, (int)(Roll("pick:" + selected.Count) * optional.Length));
            return selected.Remove(optional[index]);
        }
        float Roll(string key) => (float)MineChapterSettings.Roll(seed, chunk, key + ":" + sourceGroup.root.name);
    }

    private Room NearestRoom(float originalZ) =>
        rooms.OrderBy(room => Mathf.Abs(room.Source.localPosition.z - originalZ)).First();

    internal float OffsetAt(float originalZ)
    {
        if (rooms.Length == 0) return 0;
        var closest = NearestRoom(originalZ);
        return offsets.TryGetValue(closest.Index, out float offset) ? offset : 0;
    }

    internal void Apply(Transform ground)
    {
        if (offsets.Count == 0) return;
        Transform group = ground.Find(groupPath);
        Transform companions = string.IsNullOrWhiteSpace(companionPath) ? null : ground.Find(companionPath);
        if (useTemplates)
        {
            group = new GameObject(groupPath).transform;
            group.SetParent(ground, false);
        }
        if (group == null) return;

        if (companions != null)
            foreach (Transform companion in companions)
            {
                var room = rooms.OrderBy(item => Mathf.Abs(item.Source.localPosition.z - companion.localPosition.z)).First();
                if (Mathf.Abs(room.Source.localPosition.z - companion.localPosition.z) > .5f) continue;
                companion.gameObject.SetActive(offsets.TryGetValue(room.Index, out float offset));
                companion.localPosition += Vector3.forward * offset;
            }
        foreach (var room in rooms)
        {
            if (!offsets.TryGetValue(room.Index, out float offset))
            {
                if (!useTemplates) group.GetChild(room.Index).gameObject.SetActive(false);
                continue;
            }
            Transform placed = useTemplates ? Object.Instantiate(room.Source, group) : group.GetChild(room.Index);
            placed.localPosition = room.Source.localPosition + Vector3.forward * offset;
        }
    }
}
