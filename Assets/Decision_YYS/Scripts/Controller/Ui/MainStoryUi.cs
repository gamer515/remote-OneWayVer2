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
    [SerializeField] private StoryShutterTransition shutterTransition;

    private void Awake()
    {
        if (shutterTransition != null)
            return;

        // 전용 셔터 UI가 없는 현재 씬에서는 기본 셔터를 생성해 바로 연출을 확인합니다.
        shutterTransition = gameObject.AddComponent<StoryShutterTransition>();
        // 셔터는 전체 Canvas가 아니라 960x660 Front_Background 영역 안에서만 보이게 합니다.
        shutterTransition.CreateRuntimeShutter(cardFront);
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

        shutterTransition.Play(
            // 셔터가 닫혀 현재 카드가 가려진 순간에 다음 내용을 적용합니다.
            onClosed: () => ApplyNextStory(nextStory, displayText),
            onCompleted: onCompleted);
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
        // 기존 카드 이동 대신 같은 카드의 내용만 교체하므로 위치와 회전은 유지됩니다.
        front_Dialogue_Text.text = displayText;
        ApplyBackground(nextStory.background);
    }
}
