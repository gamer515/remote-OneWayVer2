using UnityEngine.SceneManagement;

/// <summary>
/// Decision 흐름이 Unity의 씬 API에 직접 의존하지 않도록 씬 전환을 캡슐화합니다.
/// </summary>
public sealed class SceneTransitionService
{
    private const string BattleSceneName = "BattleScene";
    private const string MainMenuSceneName = "MainMenuScene";
    public const string FinalBattleSceneName = "3DBattleScene";

    public void LoadFinalBattleScene(OneWay.Battle3D.Battle3DStartData data)
    {
        if (!UnityEngine.Application.CanStreamedLevelBeLoaded(FinalBattleSceneName))
            throw new System.InvalidOperationException("빌드 씬 목록에 3DBattleScene을 추가해야 합니다.");
        OneWay.Battle3D.Battle3DEntry.SetNext(data);
        SceneManager.LoadScene(FinalBattleSceneName);
    }

    public void LoadBattleScene()
    {
        SceneManager.LoadScene(BattleSceneName);
    }

    public void LoadMainMenuScene()
    {
        SceneManager.LoadScene(MainMenuSceneName);
    }
}
