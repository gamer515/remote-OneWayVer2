using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TempDecisionSceneController : MonoBehaviour
{
    [Header("Scene Settings")]
    // 돌아갈 전투 씬의 이름을 적어주세요. 
    // (이전에 팀원 코드에서 "TempAttackScene"을 썼다면 그대로 두시면 됩니다.)
    public string battleSceneName = "BattleScene";

    void Start()
    {
        // 씬이 시작되자마자 코루틴(타이머)을 작동시킵니다.
        StartCoroutine(WaitAndReturn());
    }

    private IEnumerator WaitAndReturn()
    {
        Debug.Log("Decision 씬 진입 성공! 1초 대기 후 전투 씬으로 돌아갑니다...");

        // 딱 1초 기다립니다.
        yield return new WaitForSeconds(1.0f);

        Debug.Log($"[{battleSceneName}] 씬으로 복귀합니다!");

        // 다시 전투 씬을 불러옵니다.
        SceneManager.LoadScene(battleSceneName);
    }
}