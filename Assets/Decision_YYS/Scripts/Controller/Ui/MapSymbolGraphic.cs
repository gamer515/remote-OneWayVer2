using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class MapSymbolGraphic : MaskableGraphic
{
    public enum Symbol { Dot, Place, Shop, Collector }

    [SerializeField] private bool warning;
    public Symbol Shape { get; set; }
    private bool showFront;
    private float frontHeight;
    private Vector2 cloudPosition;
    private float cloudOpacity;

    public void SetCollectorWarning(float height, bool front, Vector2 cloud, float opacity)
    {
        if (showFront == front && (!front || Mathf.Approximately(frontHeight, height)) &&
            Mathf.Approximately(cloudOpacity, opacity) && (opacity <= 0f || cloudPosition == cloud)) return;
        showFront = front;
        frontHeight = height;
        cloudPosition = cloud;
        cloudOpacity = opacity;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        if (warning)
        {
            if (showFront) DrawFront(mesh, rect);
            if (cloudOpacity > 0f) DrawCloud(mesh, rect);
            return;
        }

        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * .48f;
        Disc(mesh, center, Vector2.one * radius, new Color(.04f, .08f, .12f), 24);
        Disc(mesh, center, Vector2.one * (radius - 1.7f), color, 24);
        Color mark = new Color(.99f, .97f, .89f);
        float s = radius * .55f;
        switch (Shape)
        {
            case Symbol.Place:
                Triangle(mesh, center + new Vector2(-s, 0), center + new Vector2(s, 0),
                    center + new Vector2(0, s), mark);
                Box(mesh, center, -s*.7f, -s*.7f, s*.7f, 0, mark);
                Box(mesh, center, -s*.15f, -s*.7f, s*.15f, -s*.2f, color);
                break;
            case Symbol.Shop:
                Quad(mesh, center + new Vector2(-s, 0), center + new Vector2(s, 0),
                    center + new Vector2(s*.71f, s*.64f), center + new Vector2(-s*.71f, s*.64f), mark);
                Box(mesh, center, -s*.77f, -s*.72f, s*.77f, -.15f*s, mark);
                Box(mesh, center, -s*.45f, -s*.52f, s*.45f, -.33f*s, color);
                Box(mesh, center, -.12f*s, .24f*s, .12f*s, .64f*s, color);
                break;
            case Symbol.Collector:
                Disc(mesh, center, Vector2.one * (radius * .28f), mark, 12);
                break;
        }
    }

    private void DrawFront(VertexHelper mesh, Rect r)
    {
        const int segments = 32;
        float amplitude = Mathf.Min(.04f, frontHeight, 1f - frontHeight) * r.height;
        Color line = new Color(1f, .48f, .73f, .82f);
        for (int i = 0; i < segments; i += 2)
        {
            float x1 = r.xMin + r.width * i / segments;
            float x2 = x1 + r.width / segments;
            float y1 = r.yMin + r.height * frontHeight + Mathf.Sin(i * .5f) * amplitude;
            float y2 = r.yMin + r.height * frontHeight + Mathf.Sin((i + 1) * .5f) * amplitude;
            Quad(mesh, new Vector2(x1, y1 - 1.4f), new Vector2(x2, y2 - 1.4f),
                new Vector2(x2, y2 + 1.4f), new Vector2(x1, y1 + 1.4f), line);
        }
    }

    private void DrawCloud(VertexHelper mesh, Rect r)
    {
        var center = new Vector2(r.xMin + cloudPosition.x * r.width, r.yMin + cloudPosition.y * r.height);
        var radius = new Vector2(r.width * .08f, r.height * .031f);
        var tint = new Color(.89f, .84f, 1f, cloudOpacity * .33f);
        Disc(mesh, center - new Vector2(radius.x * .64f, radius.y * .1f), radius * .76f, tint, 16, true);
        Disc(mesh, center + new Vector2(0, radius.y * .23f), radius, tint, 16, true);
        Disc(mesh, center + new Vector2(radius.x * .65f, -radius.y * .11f), radius * .72f, tint, 16, true);
    }

    private static void Disc(VertexHelper mesh, Vector2 center, Vector2 radius, Color tint, int segments, bool soft = false)
    {
        int first = mesh.currentVertCount;
        mesh.AddVert(center, tint, Vector2.zero);
        Color edge = tint;
        if (soft) edge.a = 0f;
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y), edge, Vector2.zero);
            mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % segments);
        }
    }

    private static void Box(VertexHelper mesh, Vector2 center, float left, float bottom, float right, float top, Color tint) =>
        Quad(mesh, center + new Vector2(left, bottom), center + new Vector2(right, bottom),
            center + new Vector2(right, top), center + new Vector2(left, top), tint);

    private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
    {
        Triangle(mesh, a, b, c, tint);
        Triangle(mesh, a, c, d, tint);
    }

    private static void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int first = mesh.currentVertCount;
        mesh.AddVert(a, tint, Vector2.zero);
        mesh.AddVert(b, tint, Vector2.zero);
        mesh.AddVert(c, tint, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2);
    }
}
