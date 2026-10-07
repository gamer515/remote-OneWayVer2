using TMPro;
using UnityEngine;
using static Constants;

public class MainStoryUi : ParentUi
{
    [SerializeField] private TextMeshProUGUI front_Dialogue_Text;
    [SerializeField] private TextMeshProUGUI option_Text;

    private bool isStoryVisible;
    private bool isOptionVisible;
    private float optionOriginalFontSize;

    public override void SetActivateUi(bool turn)
    {
        // Walking_View는 항상 유지하고 이벤트 텍스트만 상태에 따라 표시합니다.
        isStoryVisible = turn;
        if (front_Dialogue_Text != null)
            front_Dialogue_Text.gameObject.SetActive(turn);
        if (option_Text != null)
            option_Text.gameObject.SetActive(turn && isOptionVisible);
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
        if (targetText != null)
        {
            targetText.text = text;
            if (target == TextTarget.Option)
            {
                if (optionOriginalFontSize <= 0f) optionOriginalFontSize = targetText.fontSize;
                targetText.fontSize = optionOriginalFontSize;
                Rect area = targetText.rectTransform.rect;
                // 긴 선택지도 기존 작은 선택지 칸 안에서 읽히도록 런타임 글자 크기만 조절합니다.
                while (targetText.fontSize > Mathf.Min(18f, optionOriginalFontSize) &&
                    targetText.GetPreferredValues(text, area.width, Mathf.Infinity).y > area.height)
                    targetText.fontSize -= 1f;
            }
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

}
