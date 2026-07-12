using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 전투 씬(TempAttackScene)에서 일정 시간 대기 후 다시 이야기 씬으로 돌아오는 기능을 담당합니다.
/// </summary>
public class CombatSceneController : MonoBehaviour
{
    [SerializeField] private float waitTime = 2.0f;
    [SerializeField] private string returnSceneName = "DecisionScene";

    private void Start()
    {
        StartCoroutine(WaitAndReturn());
    }

    private IEnumerator WaitAndReturn()
    {
        Debug.Log($"전투 시작! {waitTime}초 후 이야기 씬으로 돌아갑니다.");
        
        // 2초 동안 전투 연출 대기
        yield return new WaitForSeconds(waitTime);

        Debug.Log("전투 종료! 이야기 씬으로 복귀합니다.");
        
        // 다시 이야기 씬으로 전환
        SceneManager.LoadScene(returnSceneName);
    }
}
