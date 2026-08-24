using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Constants;

public class MainStoryUi : ParentUi
{
    [SerializeField] private RectTransform cardFront;
    [SerializeField] private RectTransform cardBack;

    [SerializeField] private TextMeshProUGUI front_Dialogue_Text;
    [SerializeField] private TextMeshProUGUI back_Dialogue_Text;
    [SerializeField] private TextMeshProUGUI option_Text;
    [SerializeField] private Animator shutterAnimator;

    private StoryShutterTransition shutterTransition;

    private void Awake()
    {
        // 씬에 배치된 CameraShutter Animator를 일반 전환 객체에 주입합니다.
        if (shutterAnimator != null)
            shutterTransition = new StoryShutterTransition(shutterAnimator);
    }

    public override void SetActivateUi(bool turn)
    {
        cardFront.gameObject.SetActive(turn);
        cardBack.gameObject.SetActive(turn);
    }

    public void SetActiveTextUi(bool isActive)
    {
        if (option_Text != null)
            option_Text.gameObject.SetActive(isActive);
    }

    public void WriteText(TextTarget target, string text)
    {
        TextMeshProUGUI targetText = GetTextTarget(target);
        if (targetText == null)
            return;

        targetText.text = text;
    }

    public void StartSwapStoryScreen(
        Dialogue nextStory,
        string displayText,
        System.Action onCompleted = null)
    {
        if (nextStory == null)
        {
            onCompleted?.Invoke();
            return;
        }

        if (shutterTransition == null)
        {
            ApplyNextStory(nextStory, displayText);
            onCompleted?.Invoke();
            return;
        }

        StartCoroutine(shutterTransition.Play(
            onClosed: () => ApplyNextStory(nextStory, displayText),
            onCompleted: onCompleted));
    }

    public void ApplyBackground(string bgData)
    {
        if (string.IsNullOrEmpty(bgData) ||
            string.Equals(bgData, "none", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Image dialogueImage = cardFront.GetComponent<Image>();
        if (dialogueImage == null)
            return;

        if (ColorUtility.TryParseHtmlString(bgData, out Color customColor))
        {
            dialogueImage.sprite = null;
            dialogueImage.color = customColor;
            return;
        }

        Sprite loadedSprite = Resources.Load<Sprite>(bgData);
        if (loadedSprite != null)
        {
            dialogueImage.sprite = loadedSprite;
            dialogueImage.color = Color.white;
        }
    }

    private TextMeshProUGUI GetTextTarget(TextTarget target)
    {
        switch (target)
        {
            case TextTarget.FrontDialogue:
                return front_Dialogue_Text;
            case TextTarget.BackDialogue:
                return back_Dialogue_Text;
            case TextTarget.Option:
                return option_Text;
            default:
                return null;
        }
    }

    private void ApplyNextStory(Dialogue nextStory, string displayText)
    {
        // 셔터 뒤에서 같은 카드의 내용만 교체하므로 UI 위치는 그대로 유지됩니다.
        front_Dialogue_Text.text = displayText;
        ApplyBackground(nextStory.background);
    }
}
