using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Constants;

public class MainStoryUi : ParentUi
{
    [Tooltip("대화 배경과 텍스트를 묶은 Event_View. 이동 화면에서는 이 영역만 숨깁니다.")]
    [SerializeField] private GameObject eventViewRoot;
    [SerializeField] private RectTransform cardFront;

    [SerializeField] private TextMeshProUGUI front_Dialogue_Text;
    [SerializeField] private TextMeshProUGUI option_Text;
    [SerializeField] private Animator shutterAnimator;

    private StoryShutterTransition shutterTransition;
    private bool isStoryVisible = true;
    private bool isOptionVisible;

    private void Awake()
    {
        // 씬에 배치된 CameraShutter Animator를 일반 전환 객체에 주입합니다.
        if (shutterAnimator != null)
            shutterTransition = new StoryShutterTransition(shutterAnimator);
    }

    public override void SetActivateUi(bool turn)
    {
        // 이동 화면에서는 이야기 카드와 선택 안내만 숨깁니다. 부모와 UI 제어기는 유지합니다.
        isStoryVisible = turn;
        if (eventViewRoot != null) eventViewRoot.SetActive(turn);
        if (cardFront != null) cardFront.gameObject.SetActive(turn);
        if (option_Text != null) option_Text.gameObject.SetActive(turn && isOptionVisible);
    }

    public void SetActiveTextUi(bool isActive)
    {
        isOptionVisible = isActive;
        if (option_Text != null)
            option_Text.gameObject.SetActive(isStoryVisible && isActive);
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
