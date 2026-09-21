using UnityEngine;
using UnityEngine.SceneManagement;

public class BattleDebugController : MonoBehaviour
{
    private bool isReloading;

    // 기존 이름을 유지하므로 Inspector 버튼 연결도 유지됩니다.
    public void SetBattleStage1() => RestartWithStage(1);
    public void SetBattleStage2() => RestartWithStage(2);
    public void SetBattleStage3() => RestartWithStage(3);

    private void RestartWithStage(int stageIndex)
    {
        if (isReloading)
            return;

        isReloading = true;

        var controller =
            FindFirstObjectByType<BattleSceneController>();

        BattleStartData currentData =
            controller != null && controller.StartData != null
                ? controller.StartData
                : BattleStartData.CreateDefault();

        // 성향은 그대로 유지하고 전투 번호만 변경합니다.
        BattleStartData nextData =
            currentData.WithStage(stageIndex);

        // 기존 코루틴, 탄막, 연출 등을 먼저 정리합니다.
        if (controller != null)
            controller.CancelBattle();

        BattleEntry.SetNext(nextData);

        Debug.Log(
            $"[Battle Debug] B{stageIndex}로 재시작합니다. " +
            $"Traits: {nextData.Trait1}, {nextData.Trait2}, " +
            $"{nextData.Trait3}, {nextData.Trait4}",
            this);

        SceneManager.LoadScene(
            SceneManager.GetActiveScene().name);
    }
}