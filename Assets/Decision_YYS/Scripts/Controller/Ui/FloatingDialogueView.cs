using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>NPC speech rises inside the encounter view; the player's draft stays below it.</summary>
public sealed class FloatingDialogueView : MonoBehaviour
{
    [SerializeField] private RectTransform npcArea, playerArea;
    [SerializeField] private TextMeshProUGUI template, playerText;
    [SerializeField] private UnityEngine.UI.Button confirmButton;
    [SerializeField, Min(1)] private float fontSize = 10;
    [SerializeField, Min(1)] private int visibleMessages = 3;
    [SerializeField, Min(0.01f)] private float slideSeconds = 0.3f;
    private sealed class Line { public RectTransform rect; public CanvasGroup group; public float height, targetY; }
    private readonly List<Line> lines = new List<Line>();
    private bool confirmedPlayerLine;
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
            playerText.text = "나\n" + text; confirmButton.gameObject.SetActive(false); return;
        }
        var bubble = new GameObject("NpcSpeech", typeof(RectTransform), typeof(ChatBubbleGraphic), typeof(CanvasGroup));
        var rect = (RectTransform)bubble.transform; rect.SetParent(npcArea, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 0);
        var graphic = bubble.GetComponent<ChatBubbleGraphic>();
        graphic.color = new Color(0.98f, 0.96f, 0.9f, 1); graphic.borderColor = new Color(.45f,.3f,.13f,1); graphic.cornerRadius = 4; graphic.raycastTarget = false;
        var label = Instantiate(template, rect); label.gameObject.SetActive(true); label.fontSize = fontSize; label.enableAutoSizing = false;
        label.alignment = TextAlignmentOptions.TopLeft; label.text = speaker + "\n" + text; label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(10, 8); label.rectTransform.offsetMax = new Vector2(-10, -8);
        float width = Mathf.Max(100, npcArea.rect.width);
        float height = Mathf.Max(36, label.GetPreferredValues(label.text, width-20, Mathf.Infinity).y + 16);
        rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(0, -height);
        lines.Add(new Line { rect = rect, group = bubble.GetComponent<CanvasGroup>(), height = height });
        float y = 0;
        for (int i = lines.Count-1; i >= 0; i--) { lines[i].targetY = y; y += lines[i].height + 8; }
        confirmedPlayerLine = false; playerArea.gameObject.SetActive(false);
    }
    public void SetDraft(string text, string direction, bool active, bool canConfirm, bool choice)
    {
        if (!active && confirmedPlayerLine) return;
        confirmedPlayerLine = false; playerArea.gameObject.SetActive(active);
        if (!active) return;
        playerText.fontSize = fontSize; playerText.enableAutoSizing = false;
        playerText.text = (choice ? "내 선택" : "안내") + (string.IsNullOrEmpty(direction) ? "" : " / " + direction) + "\n" +
            (string.IsNullOrWhiteSpace(text) ? "기어를 움직여 답변을 고르세요." : text);
        confirmButton.gameObject.SetActive(choice); confirmButton.interactable = canConfirm;
    }
    private void Update()
    {
        float blend = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 5 / slideSeconds);
        for (int i = lines.Count-1; i >= 0; i--)
        {
            var line = lines[i]; line.rect.anchoredPosition = Vector2.Lerp(line.rect.anchoredPosition, new Vector2(0,line.targetY), blend);
            bool visible = lines.Count-1-i < visibleMessages;
            line.group.alpha = Mathf.Lerp(line.group.alpha, visible ? 1 : 0, blend);
            if (!visible && line.group.alpha < .01f) { Destroy(line.rect.gameObject); lines.RemoveAt(i); }
        }
    }
}
