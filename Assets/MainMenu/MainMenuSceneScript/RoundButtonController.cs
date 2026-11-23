using UnityEngine;

//biography에서 각 회차별로 볼수 있게하는 회차 선택 버튼

public class RoundButtonController : MonoBehaviour
{
    public TMPro.TextMeshProUGUI btnText;
    public void Setup(RoundData data, BiographyController controller)
    {
        btnText.text = $"{data.roundNumber}round ({data.playDate})";
        var button = GetComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(() => controller.ShowDetail(data));
        UiInteractionAudio.Bind(button);
    }
}