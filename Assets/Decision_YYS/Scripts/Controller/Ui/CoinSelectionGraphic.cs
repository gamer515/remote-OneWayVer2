using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
internal sealed class CoinSelectionGraphic : MaskableGraphic
{
    private static readonly Color32 OutlineColor = new(255, 250, 197, 255);
    private static readonly Color32 ShadowColor = new(44, 41, 25, 210);
    private readonly List<(Vector2 Center, Vector2 Right, Vector2 Forward)> rings = new();
    private Rect? selection;

    internal void BeginFrame(Rect? area)
    {
        selection = area;
        rings.Clear();
    }

    internal void AddCoin(Vector2 center, Vector2 right, Vector2 forward) =>
        rings.Add((center, (right - center) * 1.12f, (forward - center) * 1.12f));

    internal void EndFrame()
    {
        gameObject.SetActive(selection.HasValue || rings.Count > 0);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (selection is Rect area)
        {
            Dash(mesh, new Vector2(area.xMin, area.yMin), new Vector2(area.xMax, area.yMin));
            Dash(mesh, new Vector2(area.xMax, area.yMin), new Vector2(area.xMax, area.yMax));
            Dash(mesh, new Vector2(area.xMax, area.yMax), new Vector2(area.xMin, area.yMax));
            Dash(mesh, new Vector2(area.xMin, area.yMax), new Vector2(area.xMin, area.yMin));
        }
        foreach (var ring in rings)
        {
            DrawRing(mesh, ring, 3f, ShadowColor);
            DrawRing(mesh, ring, 1.4f, OutlineColor);
        }
    }

    private static void Dash(VertexHelper mesh, Vector2 start, Vector2 end)
    {
        float distance = Vector2.Distance(start, end);
        Vector2 direction = (end - start).normalized;
        for (float offset = 0f; offset < distance; offset += 10f)
        {
            Vector2 from = start + direction * offset;
            Vector2 to = start + direction * Mathf.Min(offset + 5f, distance);
            Stroke(mesh, from, to, 2.5f, ShadowColor);
            Stroke(mesh, from, to, 1.25f, OutlineColor);
        }
    }

    private static void DrawRing(VertexHelper mesh, (Vector2 Center, Vector2 Right, Vector2 Forward) ring,
        float width, Color32 color)
    {
        const int segments = 32;
        // Shared vertices close the ring without gaps or darker corners.
        int first = mesh.currentVertCount;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * (2f * Mathf.PI / segments);
            float x = Mathf.Cos(angle), y = Mathf.Sin(angle);
            Vector2 point = ring.Center + ring.Right * x + ring.Forward * y;
            Vector2 tangent = ring.Forward * x - ring.Right * y;
            Vector2 normal = Vector2.Perpendicular(tangent).normalized * (width * .5f);
            mesh.AddVert(point + normal, color, Vector2.zero);
            mesh.AddVert(point - normal, color, Vector2.zero);
            int current = first + i * 2, next = first + (i + 1) % segments * 2;
            mesh.AddTriangle(current, current + 1, next + 1);
            mesh.AddTriangle(current, next + 1, next);
        }
    }

    private static void Stroke(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color32 color)
    {
        Vector2 normal = Vector2.Perpendicular(to - from).normalized * (width * .5f);
        int first = mesh.currentVertCount;
        mesh.AddVert(from + normal, color, Vector2.zero);
        mesh.AddVert(from - normal, color, Vector2.zero);
        mesh.AddVert(to - normal, color, Vector2.zero);
        mesh.AddVert(to + normal, color, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first, first + 2, first + 3);
    }
}
