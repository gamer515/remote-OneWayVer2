using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>NPC speech rises inside the encounter view; the player's draft stays below it.</summary>
public sealed class FloatingDialogueView : MonoBehaviour
{
    [SerializeField] private RectTransform npcArea, playerArea;
    [SerializeField] private TextMeshProUGUI template, playerText;
    [SerializeField] private UnityEngine.UI.Button confirmButton;
    [SerializeField, Min(1)] private float fontSize = 12;
    [SerializeField, Min(1)] private int visibleMessages = 3;
    [SerializeField, Min(0.01f)] private float slideSeconds = 0.3f;
    [Tooltip("대사를 유지한 뒤 서서히 지우는 시간(초)")]
    [SerializeField, Min(0)] private float holdSeconds = 6f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 1.2f;
    [SerializeField, Min(0)] private float messageGap = 12f;
    [Tooltip("선택지 배경의 최대 너비 / 플레이 화면 너비")]
    [SerializeField, Range(.1f,.95f)] private float choiceWidthFraction = .65f;
    private sealed class Line { public RectTransform rect; public CanvasGroup group; public float height, targetY, age; }
    private readonly List<Line> lines = new List<Line>();
    private bool confirmedPlayerLine;
    private void Awake()
    {
        confirmButton.gameObject.SetActive(false);
    }
    public int MessageCount { get; private set; }
    public void SetVisible(bool visible)
    {
        npcArea.gameObject.SetActive(visible);
        if(!visible) { confirmedPlayerLine=false;playerArea.gameObject.SetActive(false); }
    }

    public void Append(string text, string speaker, bool player)
    {
        MessageCount++;
        if (player)
        {
            confirmedPlayerLine = true; playerArea.gameObject.SetActive(true);
            playerText.text = "용사\n" + text; playerText.fontSize = fontSize;
            confirmButton.gameObject.SetActive(false); FitPlayer(); return;
        }
        var bubble = new GameObject("NpcSpeech", typeof(RectTransform), typeof(ChatBubbleGraphic), typeof(CanvasGroup));
        var rect = (RectTransform)bubble.transform; rect.SetParent(npcArea, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        var graphic = bubble.GetComponent<ChatBubbleGraphic>();
        graphic.color = new Color(0.98f, 0.96f, 0.9f, 1); graphic.borderColor = new Color(.45f,.3f,.13f,1); graphic.cornerRadius = 4; graphic.raycastTarget = false;
        var label = Instantiate(template, rect); label.gameObject.SetActive(true); label.fontSize = fontSize; label.enableAutoSizing = false;
        label.alignment = TextAlignmentOptions.TopLeft; label.text = speaker + "\n" + text; label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(10, 8); label.rectTransform.offsetMax = new Vector2(-10, -8);
        float width = Mathf.Min(Mathf.Max(100, npcArea.rect.width), Mathf.Max(80, label.GetPreferredValues(label.text).x + 20));
        float height = Mathf.Max(36, label.GetPreferredValues(label.text, width-20, Mathf.Infinity).y + 16);
        rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(0, -height);
        lines.Add(new Line { rect = rect, group = bubble.GetComponent<CanvasGroup>(), height = height });
        float y = 0;
        for (int i = lines.Count-1; i >= 0; i--) { lines[i].targetY = y; y += lines[i].height + messageGap; }
        confirmedPlayerLine = false; playerArea.gameObject.SetActive(false);
    }
    public void SetDraft(string text, string direction, bool active, bool canConfirm, bool choice)
    {
        if (!active && confirmedPlayerLine) return;
        confirmedPlayerLine = false; playerArea.gameObject.SetActive(active);
        if (!active) return;
        playerText.fontSize = fontSize; playerText.enableAutoSizing = false;
        playerText.text = (choice ? "용사" : "안내") + "\n" +
            (string.IsNullOrWhiteSpace(text) ? "기어를 움직여 답변을 고르세요." : text);
        confirmButton.gameObject.SetActive(false);
        FitPlayer();
    }
    private void FitPlayer()
    {
        float maximumWidth = ((RectTransform)playerArea.parent).rect.width * choiceWidthFraction;
        const float horizontalPadding = 24;
        float naturalWidth = playerText.GetPreferredValues(playerText.text, Mathf.Infinity, Mathf.Infinity).x;
        float width = Mathf.Min(maximumWidth, Mathf.Max(120, naturalWidth + horizontalPadding));
        float height = Mathf.Max(44, playerText.GetPreferredValues(playerText.text, Mathf.Max(40,width-horizontalPadding), Mathf.Infinity).y + 20);
        playerArea.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,width);
        playerArea.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
        playerText.rectTransform.anchorMin=Vector2.zero;playerText.rectTransform.anchorMax=Vector2.one;
        playerText.rectTransform.offsetMin=new Vector2(12,10);playerText.rectTransform.offsetMax=new Vector2(-12,-10);
    }
    private void Update()
    {
        // Editor pause/resume and a stalled frame must not expire every new line at once.
        float delta = Mathf.Min(Time.unscaledDeltaTime, Time.maximumDeltaTime);
        float blend = 1 - Mathf.Exp(-delta * 5 / slideSeconds);
        for (int i = lines.Count-1; i >= 0; i--)
        {
            var line = lines[i]; line.rect.anchoredPosition = Vector2.Lerp(line.rect.anchoredPosition, new Vector2(0,line.targetY), blend);
            line.age += delta;
            bool visible = lines.Count-1-i < visibleMessages;
            float alpha = visible ? Mathf.Clamp01(1-(line.age-holdSeconds)/fadeSeconds) : 0;
            line.group.alpha = Mathf.MoveTowards(line.group.alpha, alpha, delta/fadeSeconds);
            if (alpha <= 0 && line.group.alpha < .01f) { Destroy(line.rect.gameObject); lines.RemoveAt(i); }
        }
    }
}
