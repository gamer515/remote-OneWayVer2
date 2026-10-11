using UnityEngine;

/// <summary>게임 색상으로 그리는 둥근 말풍선, 작은 꼬리 점과 전송 아이콘입니다.</summary>
[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI/Chat Bubble")]
public sealed class ChatBubbleGraphic : UnityEngine.UI.MaskableGraphic
{
    [Min(0f)] public float cornerRadius = 18f;
    [Min(0f)] public float borderWidth = 1f;
    public Color borderColor = new Color(0.55f, 0.43f, 0.25f, 0.25f);
    public bool showTail;
    public bool tailRight;
    public bool sendArrow;

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
    {
        vertices.Clear();
        Rect area = GetPixelAdjustedRect();
        if (area.width <= 0f || area.height <= 0f) return;
        if (sendArrow)
        {
            var a = Point(area, 0.18f, 0.55f);
            var b = Point(area, 0.83f, 0.82f);
            var c = Point(area, 0.58f, 0.18f);
            var d = Point(area, 0.46f, 0.43f);
            Triangle(vertices, a, b, d, color);
            Triangle(vertices, d, b, c, color);
            return;
        }
        float radius = Mathf.Min(cornerRadius, Mathf.Min(area.width, area.height) * 0.5f);
        if (borderWidth > 0f)
        {
            Rounded(vertices, area, radius, borderColor);
            var inside = new Rect(area.xMin + borderWidth, area.yMin + borderWidth,
                Mathf.Max(0f, area.width - borderWidth * 2f), Mathf.Max(0f, area.height - borderWidth * 2f));
            Rounded(vertices, inside, Mathf.Max(0f, radius - borderWidth), color);
        }
        else Rounded(vertices, area, radius, color);
        if (showTail)
        {
            float x = tailRight ? area.xMax - 12f : area.xMin + 12f;
            Circle(vertices, new Vector2(x, area.yMin - 4f), 3f, color);
            Circle(vertices, new Vector2(x + (tailRight ? 3f : -3f), area.yMin - 11f), 2f, color);
        }
    }

    static Vector2 Point(Rect area, float x, float y) => new Vector2(area.xMin + area.width * x, area.yMin + area.height * y);
    static void Triangle(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int first = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero); vh.AddVert(c, tint, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
    }
    static void Rounded(UnityEngine.UI.VertexHelper vh, Rect area, float radius, Color tint)
    {
        if (area.width <= 0f || area.height <= 0f) return;
        int first = vh.currentVertCount;
        vh.AddVert(area.center, tint, Vector2.zero);
        const int segments = 10;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new Vector2(corner < 2 ? area.xMax - radius : area.xMin + radius,
                corner == 0 || corner == 3 ? area.yMax - radius : area.yMin + radius);
            for (int step = 0; step <= segments; step++)
            {
                float angle = (90f - corner * 90f - step * 90f / segments) * Mathf.Deg2Rad;
                vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
            }
        }
        int perimeter = 4 * (segments + 1);
        for (int i = 0; i < perimeter; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % perimeter);
    }
    static void Circle(UnityEngine.UI.VertexHelper vh, Vector2 center, float radius, Color tint)
    {
        int first = vh.currentVertCount; vh.AddVert(center, tint, Vector2.zero);
        const int segments = 16;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
        }
        for (int i = 0; i < segments; i++) vh.AddTriangle(first, first + i + 1, first + (i + 1) % segments + 1);
    }
}
