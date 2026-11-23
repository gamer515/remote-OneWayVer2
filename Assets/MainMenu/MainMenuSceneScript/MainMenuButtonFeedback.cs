using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Animates the label while the parent Button keeps a fixed hit area.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Button))]
public sealed class MainMenuButtonFeedback : MonoBehaviour, IPointerEnterHandler,
    IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("The child image to animate. Keep the Button on its stationary parent.")]
    public Graphic visual;
    [Header("Scale relative to the original image")]
    [Min(.01f)] public float hoverScale = 1.03f;
    [Min(.01f)] public float pressedScale = .94f;
    [Header("Pressed appearance")]
    [Tooltip("Offset in the image's parent UI coordinates.")]
    public Vector2 pressOffset = new Vector2(0f, -3f);
    [Range(0f, 1f)] public float pressedBrightness = .85f;
    [Header("Transition time (seconds)")]
    [Min(0f)] public float pressDuration = .08f;
    [Min(0f)] public float releaseDuration = .12f;

    enum VisualState { Normal, Hovered, Pressed, Disabled }
    Button button;
    RectTransform imageRect;
    Vector3 originalScale, fromScale, toScale;
    Vector2 originalPosition, fromPosition, toPosition;
    Color originalColor, fromColor, toColor;
    bool initialized, pointerInside, pointerHeld;
    VisualState state;
    float elapsed, duration;

    void Awake() => button = GetComponent<Button>();

    void OnEnable()
    {
        if (button == null) button = GetComponent<Button>();
        if (!initialized)
        {
            if (visual == null || visual.transform == transform || !visual.transform.IsChildOf(transform)) return;
            imageRect = visual.rectTransform;
            originalScale = imageRect.localScale;
            originalPosition = imageRect.anchoredPosition;
            originalColor = visual.color;
            initialized = true;
        }
        ResetVisual();
        RefreshTarget();
    }

    void Update()
    {
        if (!initialized) return;
        RefreshTarget();
        if (duration <= 0f) return;
        elapsed = Mathf.Min(elapsed + Time.unscaledDeltaTime, duration);
        float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
        imageRect.localScale = Vector3.LerpUnclamped(fromScale, toScale, t);
        imageRect.anchoredPosition = Vector2.LerpUnclamped(fromPosition, toPosition, t);
        visual.color = Color.LerpUnclamped(fromColor, toColor, t);
        if (elapsed >= duration) duration = 0f;
    }

    void RefreshTarget()
    {
        if (!initialized || visual == null) return;
        bool interactive = button != null && button.IsActive() && button.IsInteractable();
        if (!interactive) pointerHeld = false;
        var next = !interactive ? VisualState.Disabled :
            pointerInside ? (pointerHeld ? VisualState.Pressed : VisualState.Hovered) : VisualState.Normal;
        if (next == state) return;
        state = next;
        bool pressed = state == VisualState.Pressed;
        fromScale = imageRect.localScale;
        fromPosition = imageRect.anchoredPosition;
        fromColor = visual.color;
        toScale = originalScale * (pressed ? pressedScale : state == VisualState.Hovered ? hoverScale : 1f);
        toPosition = originalPosition + (pressed ? pressOffset : Vector2.zero);
        float brightness = pressed ? pressedBrightness : state == VisualState.Disabled ? .55f : 1f;
        toColor = new Color(originalColor.r * brightness, originalColor.g * brightness,
            originalColor.b * brightness, originalColor.a);
        elapsed = 0f;
        duration = Mathf.Max(0f, pressed ? pressDuration : releaseDuration);
        if (duration == 0f)
        {
            imageRect.localScale = toScale;
            imageRect.anchoredPosition = toPosition;
            visual.color = toColor;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        RefreshTarget();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        RefreshTarget();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || button == null || !button.IsInteractable()) return;
        pointerHeld = true;
        RefreshTarget();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        pointerHeld = false;
        RefreshTarget();
    }

    void OnDisable() => ResetVisual();
    void OnApplicationFocus(bool focused) { if (!focused) ResetVisual(); }
    void OnApplicationPause(bool paused) { if (paused) ResetVisual(); }

    void ResetVisual()
    {
        pointerInside = pointerHeld = false;
        state = VisualState.Normal;
        elapsed = duration = 0f;
        if (!initialized || imageRect == null || visual == null) return;
        imageRect.localScale = originalScale;
        imageRect.anchoredPosition = originalPosition;
        visual.color = originalColor;
    }
}
