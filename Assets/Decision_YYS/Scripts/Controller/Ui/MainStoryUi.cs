using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Constants;

public class MainStoryUi : MonoBehaviour
{
    [SerializeField] private RectTransform cardFront;
    [SerializeField] private RectTransform cardBack;

    [SerializeField] private TextMeshProUGUI front_Dialogue_Text;
    [SerializeField] private TextMeshProUGUI back_Dialogue_Text;
    [SerializeField] private TextMeshProUGUI option_Text;

    public void SetActiveTextUi(bool isActive)
    {
        option_Text.gameObject.SetActive(isActive);
    }

    public void WriteText(string text)
    {
        option_Text.text = text;
    }

    public void StartSwapStoryScreen(Dialogue nextStory)
    {
        StartCoroutine(SwipeTransition(nextStory));
    }

    //private void DisplayCurrentStory()
    //{
    //    if (scenarioData == null || scenarioData.MainStory == null || scenarioData.MainStory.Count == 0) return;

    //    var currentStory = scenarioData.MainStory[storyIndex];
    //    front_Dialogue_Text.text = SanitizeText(currentStory.text);

    //    // [추가] 플레이어가 읽은 지문을 기록 리스트에 추가 (중복 방지: 이미 마지막 항목과 같으면 패스)
    //    if (playedHistory.Count == 0 || playedHistory[playedHistory.Count - 1] != currentStory)
    //    {
    //        playedHistory.Add(currentStory);
    //    }

    //    // 배경 설정 적용
    //    ApplyBackground(cardFront, currentStory.background);

    //    if (currentStory.type == "Choice")
    //    {
    //        EnterChoiceState();
    //    }
    //    else
    //    {
    //        currentState = StoryState.ShowingStory;
    //        option_Text.gameObject.SetActive(false);
    //    }

    //    UpdatePlayerPosition();
    //}

    //private void EnterChoiceState()
    //{
    //    currentState = StoryState.WaitingForChoice;
    //    option_Text.gameObject.SetActive(true);

    //    // [수정] 캐시된 CurrentGear 대신 직접 현재 물리적 위치를 확인하여 즉시 반영
    //    int currentGear = (gearController != null) ? gearController.GetCurrentGearDirectly() : 0;

    //    if (currentGear != 0)
    //    {
    //        ShowOptionText(currentGear);
    //    }
    //    else
    //    {
    //        // [수정] 중앙(0)일 때는 안내 문구로 복구
    //        option_Text.text = "선택지를 선택하세요.";
    //    }
    //}

    //public void ShowOptionText(int gear)
    //{
    //    if (scenarioData == null || scenarioData.MainStory == null || storyIndex < 0 || storyIndex >= scenarioData.MainStory.Count) return;
    //    if (scenarioData.MainStory[storyIndex].type != "Choice") return;

    //    // [추가] 기어가 중앙(0)이면 안내 문구로 복구
    //    if (gear == 0)
    //    {
    //        option_Text.text = "선택지를 선택하세요.";
    //        return;
    //    }

    //    int index = GetOptionIndexFromGear(gear);
    //    if (index >= 0 && index < scenarioData.MainStory[storyIndex].option.Length)
    //    {
    //        option_Text.text = scenarioData.MainStory[storyIndex].option[index];
    //    }
    //}

    private IEnumerator SwipeTransition(Dialogue nextStory)
    {
        back_Dialogue_Text.text = nextStory.text;
        string bgData = nextStory.background;

        if (!string.IsNullOrEmpty(bgData) && bgData.ToLower() != "none")
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

        front_Dialogue_Text.text = nextStory.text;

        Image frontImg = cardFront.GetComponent<Image>();
        Image backImg = cardBack.GetComponent<Image>();

        if (frontImg != null && backImg != null)
        {
            frontImg.sprite = backImg.sprite;
            frontImg.color = backImg.color;
        }

        cardFront.anchoredPosition = startPos;
        cardFront.localRotation = startRot;

        //DisplayCurrentStory();
    }
}
