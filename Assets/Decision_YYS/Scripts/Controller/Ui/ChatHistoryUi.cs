using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>대화 기록과 확정 전 선택지 초안을 별도로 표시합니다.</summary>
public sealed class ChatHistoryUi : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.ScrollRect scrollRect;
    [SerializeField] private RectTransform content;
    [SerializeField] private TextMeshProUGUI messageTemplate;
    [SerializeField] private TextMeshProUGUI emptyLabel;
    [SerializeField] private RectTransform composer;
    [SerializeField] private TextMeshProUGUI draftLabel;
    [SerializeField] private TextMeshProUGUI draftStateLabel;
    [SerializeField] private RectTransform typingIndicator;
    [SerializeField] private ChatBubbleGraphic[] typingDots;
    [SerializeField] private UnityEngine.UI.Button sendButton;

    private sealed class Message
    {
        public RectTransform bubble;
        public UnityEngine.UI.LayoutElement layout;
        public TextMeshProUGUI label;
    }
    private readonly List<Message> messages = new List<Message>();
    private readonly Dictionary<string, string> speakerNames = new Dictionary<string, string>();
    private float lastWidth = -1f;
    private float lastDraftWidth = -1f;
    private bool draftLayoutDirty = true;
    private bool pendingLayout;
    private bool composerActive;
    private bool choiceDraft = true;
    private bool draftCanConfirm;
    private string draftText = string.Empty;
    private string draftDirection = string.Empty;

    public int MessageCount => messages.Count;
    public string DraftText => composerActive && choiceDraft ? draftText : string.Empty;
    public bool IsTyping => composerActive && choiceDraft;

    private void Awake()
    {
        if (messageTemplate != null) messageTemplate.gameObject.SetActive(false);
        if (emptyLabel != null) emptyLabel.gameObject.SetActive(messages.Count == 0);
        UpdateComposer();
    }

    public void AppendDialogue(string text, string speakerId = null) =>
        Append(text, ResolveSpeaker(speakerId), false);

    public void AppendPlayerChoice(string text)
    {
        Append(text, "나", true);
        SetComposerVisible(false);
    }

    public void SetChoiceDraft(string text, string direction = null, bool canConfirm = true)
    {
        choiceDraft = true;
        draftText = text ?? string.Empty;
        draftDirection = direction ?? string.Empty;
        draftCanConfirm = canConfirm && !string.IsNullOrWhiteSpace(draftText);
        UpdateComposer();
    }

    public void SetStatus(string text)
    {
        choiceDraft = false;
        draftText = text ?? string.Empty;
        draftDirection = string.Empty;
        draftCanConfirm = false;
        UpdateComposer();
    }

    public void SetComposerVisible(bool visible)
    {
        composerActive = visible;
        if (!visible)
        {
            draftText = string.Empty;
            draftDirection = string.Empty;
            draftCanConfirm = false;
        }
        UpdateComposer();
    }

    private void UpdateComposer()
    {
        draftLayoutDirty = true;
        if (draftLabel != null)
        {
            draftLabel.text = composerActive
                ? (string.IsNullOrWhiteSpace(draftText) ? "기어를 움직여 답변을 고르세요." : draftText)
                : "선택지를 기다리는 중...";
            draftLabel.color = composerActive
                ? new Color(0.24f, 0.19f, 0.13f, 1f) : new Color(0.54f, 0.48f, 0.37f, 1f);
        }
        if (draftStateLabel != null)
            draftStateLabel.text = !composerActive ? "내 답변" : !choiceDraft ? "배분 중" :
                (string.IsNullOrEmpty(draftDirection) ? "작성 중" : "작성 중 / " + draftDirection);
        if (typingIndicator != null) typingIndicator.gameObject.SetActive(IsTyping);
        if (sendButton != null) sendButton.interactable = composerActive && choiceDraft && draftCanConfirm;
        pendingLayout = true;
    }

    private string ResolveSpeaker(string speakerId)
    {
        if (string.IsNullOrWhiteSpace(speakerId)) return "이야기";
        if (speakerNames.TryGetValue(speakerId, out string name)) return name;
        TextAsset profile = Resources.Load<TextAsset>("Story_Json_Data/Characters/" + speakerId);
        StoryCharacter character = profile != null ? JsonUtility.FromJson<StoryCharacter>(profile.text) : null;
        name = string.IsNullOrWhiteSpace(character?.name) ? speakerId : character.name;
        speakerNames[speakerId] = name;
        return name;
    }

    private void Append(string text, string speaker, bool player)
    {
        if (string.IsNullOrWhiteSpace(text) || content == null || messageTemplate == null) return;
        var row = new GameObject(player ? "PlayerChoice" : "Dialogue",
            typeof(RectTransform), typeof(UnityEngine.UI.LayoutElement));
        row.transform.SetParent(content, false);
        var bubbleObject = new GameObject("Bubble", typeof(RectTransform), typeof(ChatBubbleGraphic));
        var bubble = (RectTransform)bubbleObject.transform;
        bubble.SetParent(row.transform, false);
        bubble.anchorMin = bubble.anchorMax = bubble.pivot = new Vector2(player ? 1f : 0f, 1f);
        bubble.anchoredPosition = Vector2.zero;
        var background = bubbleObject.GetComponent<ChatBubbleGraphic>();
        background.color = player ? new Color(0.79f, 0.81f, 0.71f, 1f) : new Color(0.91f, 0.84f, 0.68f, 1f);
        background.cornerRadius = 20f;
        background.showTail = true;
        background.tailRight = player;
        background.raycastTarget = false;

        TextMeshProUGUI label = Instantiate(messageTemplate, bubble, false);
        label.name = "Message";
        var labelPosition = label.transform.localPosition;
        labelPosition.z = 0f;
        label.transform.localPosition = labelPosition;
        label.gameObject.SetActive(true);
        label.richText = true;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.raycastTarget = false;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(18f, 14f);
        label.rectTransform.offsetMax = new Vector2(-18f, -14f);
        string safeSpeaker = speaker.Replace("<", "").Replace(">", "");
        label.text = "<size=75%><color=#78562B>" + safeSpeaker + "</color></size>\n" + text;
        var message = new Message { bubble = bubble,
            layout = row.GetComponent<UnityEngine.UI.LayoutElement>(), label = label };
        messages.Add(message);
        SizeMessage(message, Mathf.Max(1f, content.rect.width));
        if (emptyLabel != null) emptyLabel.gameObject.SetActive(false);
        pendingLayout = true;
    }

    private static void SizeMessage(Message message, float availableWidth)
    {
        float maximumWidth = Mathf.Max(1f, availableWidth * 0.94f);
        float width = Mathf.Min(maximumWidth, Mathf.Max(150f, message.label.GetPreferredValues(message.label.text).x + 36f));
        float height = Mathf.Max(74f, message.label.GetPreferredValues(message.label.text,
            Mathf.Max(1f, width - 36f), Mathf.Infinity).y + 28f);
        message.bubble.sizeDelta = new Vector2(width, height);
        message.layout.preferredHeight = height + 14f; // 꼬리 점까지 다음 메시지와 겹치지 않게 확보합니다.
        message.layout.flexibleHeight = 0f;
    }

    private void LateUpdate()
    {
        RefreshLayout();
        AnimateTypingDots(Time.unscaledTime);
    }

    private void AnimateTypingDots(float time)
    {
        if (!IsTyping || typingDots == null) return;
        for (int i = 0; i < typingDots.Length; i++)
        {
            if (typingDots[i] == null) continue;
            float wave = Mathf.Sin(time * 5f - i * 1.1f);
            var position = typingDots[i].rectTransform.anchoredPosition;
            position.y = -18f + wave * 5f;
            typingDots[i].rectTransform.anchoredPosition = position;
            var tint = typingDots[i].color;
            tint.a = 0.65f + 0.35f * (0.5f + wave * 0.5f);
            typingDots[i].color = tint;
        }
    }

    public void RefreshLayout()
    {
        if (content == null || scrollRect == null || scrollRect.viewport == null) return;
        if (composer != null && draftLabel != null)
        {
            float textWidth = Mathf.Max(1f, composer.rect.width - 82f);
            float height = composer.rect.height;
            if (draftLayoutDirty || Mathf.Abs(textWidth - lastDraftWidth) > 0.5f)
            {
                // Reserve a readable history area, then fit longer drafts within the composer.
                float originalSize = messageTemplate != null ? messageTemplate.fontSize : 25f;
                draftLabel.fontSize = originalSize;
                height = Mathf.Clamp(draftLabel.GetPreferredValues(draftLabel.text, textWidth, Mathf.Infinity).y + 54f, 100f, 190f);
                while (draftLabel.fontSize > Mathf.Min(18f, originalSize) &&
                    draftLabel.GetPreferredValues(draftLabel.text, textWidth, Mathf.Infinity).y > height - 50f)
                    draftLabel.fontSize -= 1f;
                lastDraftWidth = textWidth;
                draftLayoutDirty = false;
            }
            if (Mathf.Abs(composer.rect.height - height) > 0.5f)
            {
                composer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                pendingLayout = true;
            }
            if (typingIndicator != null)
                typingIndicator.anchoredPosition = new Vector2(-18f, composer.anchoredPosition.y + height + 16f);
            var bottom = scrollRect.viewport.offsetMin;
            float typingSpace = IsTyping && typingIndicator != null ? typingIndicator.rect.height + 28f : 20f;
            float needed = composer.anchoredPosition.y + height + typingSpace;
            if (Mathf.Abs(bottom.y - needed) > 0.5f)
            {
                bottom.y = needed;
                scrollRect.viewport.offsetMin = bottom;
                pendingLayout = true;
            }
            if (emptyLabel != null)
            {
                var emptyOffset = emptyLabel.rectTransform.offsetMin;
                emptyOffset.y = needed;
                emptyLabel.rectTransform.offsetMin = emptyOffset;
            }
            if (scrollRect.verticalScrollbar != null)
            {
                var track = (RectTransform)scrollRect.verticalScrollbar.transform;
                var offset = track.offsetMin;
                offset.y = needed;
                track.offsetMin = offset;
            }
        }
        float width = content.rect.width;
        var minimum = content.GetComponent<UnityEngine.UI.LayoutElement>();
        float viewportHeight = scrollRect.viewport.rect.height;
        bool changed = Mathf.Abs(width - lastWidth) > 0.5f ||
            (minimum != null && Mathf.Abs(minimum.minHeight - viewportHeight) > 0.5f);
        if (changed)
        {
            lastWidth = width;
            if (minimum != null) minimum.minHeight = viewportHeight;
            foreach (Message message in messages) SizeMessage(message, width);
            pendingLayout = true;
        }
        if (!pendingLayout) return;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Canvas.ForceUpdateCanvases();
        scrollRect.StopMovement();
        scrollRect.verticalNormalizedPosition = 0f;
        pendingLayout = false;
    }
}
