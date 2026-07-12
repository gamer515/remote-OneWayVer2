using UnityEngine;

public class CancelPanelController : MonoBehaviour
{
    public GameObject currentPanel;   // 현재 닫을 패널
    public GameObject previousPanel;  // 돌아갈 이전 패널 (주로 메인 메뉴)

    // 뒤로가기 버튼에 연결할 함수
    public virtual void OnClickBack()
    {
        if (currentPanel != null)
            currentPanel.SetActive(false);

        if (previousPanel != null)
            previousPanel.SetActive(true);

        Debug.Log($"{gameObject.name} 패널이 닫히고 이전 화면으로 돌아갑니다.");
    }
}
