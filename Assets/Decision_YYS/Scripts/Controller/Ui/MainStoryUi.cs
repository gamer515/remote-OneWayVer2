using System.Collections;
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

    public void WriteText(TextTarget target , string text)
    {
        TextMeshProUGUI targetText = GetTextTarget(target);
        if(targetText == null)
        {
            return;
        }

        targetText.text = text;
    }

    public void StartSwapStoryScreen(
        Dialogue nextStory,
        string displayText,
        System.Action onCompleted = null)
    {
        StartCoroutine(SwipeTransition(nextStory, displayText, onCompleted));
    }

    public void ApplyBackground(string bgData)
    {
        if (string.IsNullOrEmpty(bgData) ||
            string.Equals(bgData, "none", System.StringComparison.OrdinalIgnoreCase)) return;

        Image dgImg = cardFront.GetComponent<Image>();
        if (dgImg == null) return;

        Color customColor;
        if (ColorUtility.TryParseHtmlString(bgData, out customColor))
        {
            dgImg.sprite = null;
            dgImg.color = customColor;
        }
        else
        {
            Sprite loadedSprite = Resources.Load<Sprite>(bgData);
            if (loadedSprite != null)
            {
                dgImg.sprite = loadedSprite;
                dgImg.color = Color.white;
            }
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

    private IEnumerator SwipeTransition(
        Dialogue nextStory,
        string displayText,
        System.Action onCompleted)
    {
        back_Dialogue_Text.text = displayText;
        string bgData = nextStory.background;

        if (!string.IsNullOrEmpty(bgData) &&
            !string.Equals(bgData, "none", System.StringComparison.OrdinalIgnoreCase))
        {
            Image dgImg = cardBack.GetComponent<Image>();
            Color customColor;

            if (ColorUtility.TryParseHtmlString(bgData, out customColor))
            {
                Debug.Log($"Color detected: {bgData}, applying color: {customColor}");
                dgImg.sprite = null;
                dgImg.color = customColor;
            }
            else
            {
                Debug.Log($"Not a color, trying to load resource: {bgData}");
                Sprite loadedSprite = Resources.Load<Sprite>(bgData);
                if (loadedSprite != null)
                {
                    dgImg.sprite = loadedSprite;
                    dgImg.color = Color.white;
                }
            }
        }

        float duration = 0.5f;
        float elasped = 0f;
        Vector2 startPos = cardFront.anchoredPosition;
        Quaternion startRot = cardFront.localRotation;

        Vector2 targetPos = startPos + new Vector2(-1000f, -200f);
        Quaternion targetRot = Quaternion.Euler(0f, 0f, 30f);

        while (elasped < duration)
        {
            elasped += Time.deltaTime;
            float t = Mathf.Clamp01(elasped / duration);

            cardFront.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            cardFront.localRotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }

        front_Dialogue_Text.text = displayText;

        Image frontImg = cardFront.GetComponent<Image>();
        Image backImg = cardBack.GetComponent<Image>();

        if (frontImg != null && backImg != null)
        {
            frontImg.sprite = backImg.sprite;
            frontImg.color = backImg.color;
        }

        cardFront.anchoredPosition = startPos;
        cardFront.localRotation = startRot;
        onCompleted?.Invoke();
    }
}
