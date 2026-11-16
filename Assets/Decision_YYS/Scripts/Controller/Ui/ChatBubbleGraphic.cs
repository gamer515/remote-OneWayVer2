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
    private Transform speakerAnchor;
    private Transform speakerHead;
    private Transform speakerRig;
    private Canvas speakerCanvas;

    internal static ChatBubbleGraphic CreateNoticeBackdrop(Transform parent, string objectName, bool tail)
    {
        Canvas foreground = null;
        if (tail)
        {
            var overlay = new GameObject(objectName + "Overlay", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(overlay, parent.gameObject.scene);
            foreground = overlay.GetComponent<Canvas>();
            foreground.renderMode = RenderMode.ScreenSpaceOverlay;
            foreground.sortingOrder = 150;
            var scaler = overlay.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            parent = overlay.transform;
        }
        var panel = new GameObject(objectName, typeof(RectTransform), typeof(ChatBubbleGraphic))
            .GetComponent<ChatBubbleGraphic>();
        panel.rectTransform.SetParent(parent, false);
        panel.speakerCanvas = foreground;
        panel.color = new Color(1f, .97f, .88f, .98f);
        panel.borderColor = new Color(.66f, .52f, .32f, .85f);
        panel.cornerRadius = tail ? 10f : 5f;
        panel.showTail = tail;
        panel.raycastTarget = false;
        return panel;
    }

    internal void DisposeNoticeOverlay()
    {
        if (speakerCanvas != null) Destroy(speakerCanvas.gameObject);
    }

    internal void FitNotice(TMPro.TextMeshProUGUI label, float maxWidth)
    {
        const float horizontalPadding = 24f;
        const float verticalPadding = 14f;
        label.margin = Vector4.zero;
        label.textWrappingMode = TMPro.TextWrappingModes.Normal;
        label.overflowMode = TMPro.TextOverflowModes.Overflow;
        float naturalWidth = label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).x;
        float textWidth = Mathf.Clamp(Mathf.Ceil(naturalWidth), 1f, Mathf.Max(1f, maxWidth - horizontalPadding));
        float height = Mathf.Ceil(label.GetPreferredValues(label.text, textWidth, Mathf.Infinity).y);
        rectTransform.sizeDelta = new Vector2(textWidth + horizontalPadding, height + verticalPadding);
        label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    internal bool FollowSpeaker(Transform speaker, Camera worldCamera, float gap)
    {
        if (speaker == null || !speaker.gameObject.activeInHierarchy || worldCamera == null)
            return false;
        if (speakerAnchor != speaker)
        {
            speakerAnchor = speaker;
            var animator = speaker.GetComponentInParent<Animator>();
            speakerRig = animator != null ? animator.transform : null;
            speakerHead = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        }
        Vector3 headPosition = speakerHead != null
            ? speakerHead.position + speakerRig.up * (.55f * Mathf.Abs(speakerRig.lossyScale.y))
            : speaker.position;
        Vector3 screen = worldCamera.WorldToScreenPoint(headPosition);
        var parent = (RectTransform)rectTransform.parent;
        var parentCanvas = parent.GetComponentInParent<Canvas>();
        if (parentCanvas == null) return false;
        var canvasCamera = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera;
        if (screen.z <= 0f ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, canvasCamera, out var point))
            return false;

        float halfWidth = rectTransform.rect.width * .5f;
        float halfHeight = rectTransform.rect.height * .5f;
        const float margin = 8f;
        float left = parent.rect.xMin + halfWidth + margin;
        float right = parent.rect.xMax - halfWidth - margin;
        float desired = point.x + halfWidth - 12f;
        bool useRightTail = desired > right;
        if (useRightTail) desired = point.x - halfWidth + 12f;
        float x = left <= right ? Mathf.Clamp(desired, left, right) : parent.rect.center.x;
        float y = Mathf.Min(point.y + halfHeight + gap, parent.rect.yMax - halfHeight - margin);
        rectTransform.localPosition = new Vector3(x, y, 0f);
        if (tailRight != useRightTail)
        {
            tailRight = useRightTail;
            SetVerticesDirty();
        }
        return true;
    }

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
