using UnityEngine;
using UnityEngine.SceneManagement;

public class BattleDebugController : MonoBehaviour
{
    private bool isReloading;

    private static int pendingStage = -1;
    private static int pendingStep = -1;
    private static bool pendingContinue;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPatternSelection()
    {
        pendingStage = -1;
        pendingStep = -1;
        pendingContinue = false;
    }

    public static int TakePatternStep(
        out int stageIndex,
        out bool continueFromHere)
    {
        int selected = pendingStep;
        stageIndex = pendingStage;
        continueFromHere = pendingContinue;

        // 저장한 선택값은 다음 씬에서 한 번만 사용한다.
        ResetPatternSelection();

        return selected;
    }

    public void SelectPattern(
        int stageIndex,
        int stepIndex,
        bool continueFromHere)
    {
        RestartWithStage(
            stageIndex,
            stepIndex,
            continueFromHere);
    }

    // 기존 B2 버튼 연결도 계속 사용할 수 있도록 유지한다.
    public void SelectB2Pattern(int stepIndex)
    {
        SelectPattern(2, stepIndex, false);
    }

    public void SelectB2PatternFromHere(int stepIndex)
    {
        SelectPattern(2, stepIndex, true);
    }

    public void SetBattleStage1() => RestartWithStage(1);
    public void SetBattleStage2() => RestartWithStage(2);
    public void SetBattleStage3() => RestartWithStage(3);
    public void SetBattleStageBD() => RestartWithStage(4);

    private void RestartWithStage(
        int stageIndex,
        int stepIndex = -1,
        bool continueFromHere = false)
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

        BattleStartData nextData =
            currentData.WithStage(stageIndex);

        // 실행 중인 탄막과 연출을 정리한 뒤 씬을 다시 연다.
        if (controller != null)
            controller.CancelBattle();

        pendingStage = stageIndex;
        pendingStep = stepIndex;
        pendingContinue = stepIndex >= 0 && continueFromHere;

        BattleEntry.SetNext(nextData);

        SceneManager.LoadScene(
            SceneManager.GetActiveScene().name);
    }
}