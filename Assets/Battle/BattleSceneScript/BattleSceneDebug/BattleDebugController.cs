using UnityEngine;
using UnityEngine.SceneManagement;

public class BattleDebugController : MonoBehaviour
{
    // 버튼 1을 누르면 실행할 함수 (B1로 강제 변경)
    public void SetBattleStage1()
    {
        BattleStateMachine.BattleIndex = 1;
        Debug.LogWarning("[디버그] 현재 스테이지를 B1으로 강제로 변경했습니다. 씬을 재시작합니다.");
        ReloadCurrentScene();
    }

    // 버튼 2를 누르면 실행할 함수 (B2로 강제 변경)
    public void SetBattleStage2()
    {
        BattleStateMachine.BattleIndex = 2;
        Debug.LogWarning("[디버그] 현재 스테이지를 B2으로 강제로 변경했습니다. 씬을 재시작합니다.");
        ReloadCurrentScene();
    }

    // 버튼 3를 누르면 실행할 함수 (B3로 강제 변경)
    public void SetBattleStage3()
    {
        BattleStateMachine.BattleIndex = 3;
        Debug.LogWarning("[디버그] 현재 스테이지를 B3으로 강제로 변경했습니다. 씬을 재시작합니다.");
        ReloadCurrentScene();
    }

    // 현재 전투 씬을 처음부터 다시 깔끔하게 불러오는 함수
    private void ReloadCurrentScene()
    {
        // 현재 활성화된 씬의 이름을 가져와서 다시 로드합니다.
        string currentSceneName = SceneManager.GetActiveScene().name;
        SceneManager.LoadScene(currentSceneName);
    }
}