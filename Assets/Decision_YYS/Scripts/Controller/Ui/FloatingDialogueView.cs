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
    private TextMeshProUGUI storyHeading, storyNotice;
    private TextMeshProUGUI taxCriteria;
    private TextMeshProUGUI choiceHint;
    private System.Func<int, string> getChoiceHint;
    private string clueTarget;
    private System.Func<string> revealClue;
    private int firstChoice, choiceCount;

    internal void SetDialogueClue(string target, System.Func<string> reveal)
    {
        clueTarget = target;
        revealClue = reveal;
    }

    internal void SetChoiceHints(System.Func<int, string> hint, int first, int count)
    {
        getChoiceHint = hint;
        firstChoice = first;
        choiceCount = count;
    }

    private void LateUpdate()
    {
        string hint = null;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null && playerText.isActiveAndEnabled && playerText.canvas != null)
        {
            var canvas = playerText.canvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var position = mouse.position.ReadValue();
            if (RectTransformUtility.RectangleContainsScreenPoint(playerText.rectTransform, position, camera))
            {
                if (showingChoices && getChoiceHint != null)
                {
                    int line = TMP_TextUtilities.FindIntersectingLine(playerText, position, camera);
                    if (line >= 0 && line < playerText.textInfo.lineCount)
                    {
                        int character = playerText.textInfo.lineInfo[line].firstCharacterIndex;
                        int offset = playerText.textInfo.characterInfo[character].index;
                        int optionStart = playerText.text.Length - draft.Length;
                        if (offset >= optionStart)
                        {
                            int row = 0;
                            for (int i = optionStart; i < offset; i++)
                                if (playerText.text[i] == '\n') row++;
                            if (row < choiceCount) hint = getChoiceHint(firstChoice + row);
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(clueTarget) && revealClue != null)
                {
                    int start = playerText.text.IndexOf(clueTarget, System.StringComparison.Ordinal);
                    int hit = TMP_TextUtilities.FindIntersectingCharacter(playerText, position, camera, true);
                    if (start >= 0 && hit >= 0)
                    {
                        int character = playerText.textInfo.characterInfo[hit].index;
                        if (character >= start && character < start + clueTarget.Length) hint = revealClue();
                    }
                }
            }
        }
        if (choiceHint == null && !string.IsNullOrEmpty(hint))
        {
            choiceHint = Instantiate(playerText, playerArea);
            choiceHint.name = "ChoiceHint";
            choiceHint.alignment = TextAlignmentOptions.Bottom;
            choiceHint.color = new Color(1f, .88f, .57f);
            choiceHint.fontSize = 16;
            var rect = choiceHint.rectTransform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(0, 24);
            rect.anchoredPosition = new Vector2(0, 5);
        }
        if (choiceHint == null) return;
        choiceHint.text = hint ?? "";
        choiceHint.gameObject.SetActive(!string.IsNullOrEmpty(hint));
    }

    public void SetStoryHeading(string text) => SetStoryLabel(ref storyHeading, "StoryHeading", text, .95f, 19f);
    public void SetStoryNotice(string text) => SetStoryLabel(ref storyNotice, "StoryNotice", text, .045f, 15f);

    internal void SetTaxCriteria(string text)
    {
        if (taxCriteria == null && string.IsNullOrEmpty(text)) return;
        if (taxCriteria == null)
        {
            taxCriteria = Instantiate(playerText, playerArea);
            taxCriteria.name = "TaxPassCriteria";
            taxCriteria.alignment = TextAlignmentOptions.Center;
            taxCriteria.color = new Color(1f, .85f, .57f);
            taxCriteria.fontSize = Mathf.Max(12f, fontSize - 1f);
            taxCriteria.raycastTarget = false;
        }
        taxCriteria.text = text;
        taxCriteria.gameObject.SetActive(!string.IsNullOrEmpty(text));
        if (playerArea.gameObject.activeSelf) FitPanel();
    }

    private void SetStoryLabel(ref TextMeshProUGUI label, string objectName, string text, float height, float size)
    {
        if (label == null && string.IsNullOrEmpty(text)) return;
        if (label == null)
        {
            label = Instantiate(playerText, playerArea.parent);
            label.name = objectName;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.fontSize = size;
            var rect = label.rectTransform;
            rect.anchorMin = new Vector2(.08f, height);
            rect.anchorMax = new Vector2(.92f, height);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(0f, 28f);
            rect.anchoredPosition = Vector2.zero;
        }
        label.text = text;
        label.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

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
        SetStoryHeading(null);
        SetStoryNotice(null);
        SetTaxCriteria(null);
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
        bool hasCriteria = taxCriteria != null && taxCriteria.gameObject.activeSelf;
        if (hasCriteria) naturalWidth = Mathf.Max(naturalWidth, taxCriteria.GetPreferredValues().x);
        float minimumWidth = showingChoices ? viewportWidth * choiceMinimumWidthFraction : 120;
        float width = Mathf.Min(maximumWidth, Mathf.Max(minimumWidth, naturalWidth + horizontalPadding));
        float textWidth = Mathf.Max(1, width - horizontalPadding);
        float criteriaHeight = hasCriteria ? taxCriteria.GetPreferredValues(taxCriteria.text, textWidth, Mathf.Infinity).y : 0f;
        float criteriaOffset = hasCriteria ? criteriaHeight + 9f : 0f;
        float height = Mathf.Max(44, playerText.GetPreferredValues(playerText.text, textWidth, Mathf.Infinity).y + 20) +
            criteriaOffset;
        playerArea.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        playerArea.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        playerText.rectTransform.anchorMin = Vector2.zero;
        playerText.rectTransform.anchorMax = Vector2.one;
        playerText.rectTransform.offsetMin = new Vector2(12, 10 + criteriaOffset);
        playerText.rectTransform.offsetMax = new Vector2(-rightPadding, -10);
        if (hasCriteria)
        {
            var rect = taxCriteria.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.right;
            rect.offsetMin = new Vector2(12, 10);
            rect.offsetMax = new Vector2(-rightPadding, 10 + criteriaHeight);
        }
    }
}
