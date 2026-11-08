using TMPro;
using UnityEngine;

/// <summary>Shares one lower-screen panel between dialogue, choices and encounter prompts.</summary>
public sealed class FloatingDialogueView : MonoBehaviour
{
    [SerializeField] private RectTransform playerArea;
    [SerializeField] private TextMeshProUGUI playerText;
    [SerializeField] private UnityEngine.UI.Button confirmButton;
    [SerializeField, Min(1)] private float fontSize = 15;
    [SerializeField] private Color background = new Color(.12f, .13f, .14f, .82f);
    [SerializeField] private Color borderColor = new Color(.25f, .27f, .29f, .85f);
    [SerializeField] private Color textColor = new Color(.98f, .98f, .97f, 1);
    [SerializeField, Range(.1f, .95f)] private float choiceMinimumWidthFraction = .5f;
    [SerializeField, Range(.1f, .95f)] private float choiceWidthFraction = .65f;
    [SerializeField] private Vector2 panelAnchor = new Vector2(.5f, .08f);

    private string speech;
    private string draft;
    private bool showingChoices;
    private TextMeshProUGUI overflowText;

    public int MessageCount { get; private set; }

    private void Awake()
    {
        confirmButton.gameObject.SetActive(false);
        playerText.color = textColor;
        playerText.enableAutoSizing = false;
        playerText.raycastTarget = false;
        playerArea.anchorMin = playerArea.anchorMax = panelAnchor;
        playerArea.pivot = new Vector2(.5f, 0f);
        playerArea.anchoredPosition = Vector2.zero;
        if (playerArea.TryGetComponent<ChatBubbleGraphic>(out var graphic))
        {
            graphic.color = background;
            graphic.borderColor = borderColor;
            graphic.showTail = false;
            graphic.raycastTarget = false;
        }
        Render();
    }

    public void SetVisible(bool visible)
    {
        if (visible) return;
        speech = draft = null;
        showingChoices = false;
        if (overflowText != null) overflowText.gameObject.SetActive(false);
        Render();
    }

    public void Append(string text, string speaker, bool player)
    {
        MessageCount++;
        speech = (player ? "용사" : speaker) + "\n" + text;
        Render();
    }

    public void SetDraft(string text, bool active, bool choice, string overflow = null)
    {
        draft = active ? text : null;
        showingChoices = active && choice;
        if (showingChoices && !string.IsNullOrEmpty(overflow))
        {
            if (overflowText == null)
            {
                overflowText = Instantiate(playerText, playerArea);
                overflowText.name = "ChoiceOverflow";
                overflowText.alignment = TextAlignmentOptions.Center;
                var rect = overflowText.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(1, .35f);
                rect.pivot = new Vector2(1, .5f);
                rect.sizeDelta = new Vector2(34, 28);
                rect.anchoredPosition = new Vector2(-8, 0);
            }
            overflowText.text = overflow;
            overflowText.gameObject.SetActive(true);
        }
        else if (overflowText != null) overflowText.gameObject.SetActive(false);
        Render();
    }

    private void Render()
    {
        bool hasDraft = !string.IsNullOrWhiteSpace(draft);
        playerArea.gameObject.SetActive(hasDraft || !string.IsNullOrWhiteSpace(speech));
        if (!playerArea.gameObject.activeSelf) return;
        playerText.fontSize = fontSize;
        playerText.alignment = showingChoices ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Center;
        playerText.text = hasDraft && showingChoices
            ? (string.IsNullOrWhiteSpace(speech) ? "용사" : speech) + "\n\n" + draft
            : hasDraft ? draft : speech;
        FitPanel();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (playerArea != null && playerArea.gameObject.activeSelf && !string.IsNullOrEmpty(playerText.text))
            FitPanel();
    }

    private void FitPanel()
    {
        float viewportWidth = ((RectTransform)playerArea.parent).rect.width;
        float maximumWidth = viewportWidth * choiceWidthFraction;
        if (maximumWidth <= 0) return;
        float rightPadding = overflowText != null && overflowText.gameObject.activeSelf ? 46 : 12;
        float horizontalPadding = 12 + rightPadding;
        float naturalWidth = playerText.GetPreferredValues(playerText.text, Mathf.Infinity, Mathf.Infinity).x;
        float minimumWidth = showingChoices ? viewportWidth * choiceMinimumWidthFraction : 120;
        float width = Mathf.Min(maximumWidth, Mathf.Max(minimumWidth, naturalWidth + horizontalPadding));
        float height = Mathf.Max(44, playerText.GetPreferredValues(playerText.text, Mathf.Max(1, width - horizontalPadding), Mathf.Infinity).y + 20);
        playerArea.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        playerArea.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        playerText.rectTransform.anchorMin = Vector2.zero;
        playerText.rectTransform.anchorMax = Vector2.one;
        playerText.rectTransform.offsetMin = new Vector2(12, 10);
        playerText.rectTransform.offsetMax = new Vector2(-rightPadding, -10);
    }
}
