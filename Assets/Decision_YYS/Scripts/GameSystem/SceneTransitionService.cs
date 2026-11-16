using UnityEngine.SceneManagement;
using OneWay.Loading;

/// <summary>
/// Decision 흐름이 Unity의 씬 API에 직접 의존하지 않도록 씬 전환을 캡슐화합니다.
/// </summary>
public sealed class SceneTransitionService
{
    private const string BattleSceneName = "BattleScene";
    public const string FinalBattleSceneName = "3DBattleScene";

    public void LoadFinalBattleScene(OneWay.Battle3D.Battle3DStartData data)
    {
        if (!OneWay.Battle3D.Battle3DEntry.OpenThroughLoading(data))
            throw new System.InvalidOperationException("3D 전투 로딩 요청이 거절되었습니다.");
    }

    public void LoadBattleScene()
    {
        SceneManager.LoadScene(BattleSceneName);
    }

    public void LoadDecisionScene() => Open(LoadingDestination.Decision);

    public void LoadMainMenuScene() => Open(LoadingDestination.MainMenu);

    private static void Open(LoadingDestination destination)
    {
        if (!LoadingScreenController.Open(destination))
            throw new System.InvalidOperationException(destination + " 로딩 요청이 거절되었습니다.");
    }
}
