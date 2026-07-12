using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshPro 사용을 위해 필요

public class PlayerUIManager : MonoBehaviour
{
    [Header("References")]
    public PlayerController player; // 플레이어 스크립트 연결
    public Image hpForeground;      // 노란색 체력바 이미지
    public TextMeshProUGUI hpText;  // 체력 텍스트 (예: 3 / 3)

    void Update()
    {
        // 플레이어가 연결되어 있다면 매 프레임 UI를 업데이트합니다.
        if (player != null)
        {
            // 1. 체력바 게이지 조절 (현재체력 / 최대체력 비율)
            hpForeground.fillAmount = player.currentHp / player.maxHp;

            // 2. 텍스트 업데이트 (소수점 없이 정수로 표시, 0 이하로 내려가지 않게 방지)
            int currentHpInt = Mathf.Max(0, (int)player.currentHp);
            int maxHpInt = (int)player.maxHp;

            hpText.text = $"{currentHpInt} / {maxHpInt}";
        }
    }
}