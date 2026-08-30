using UnityEngine.SceneManagement;

/// <summary>
/// Decision 흐름이 Unity의 씬 API에 직접 의존하지 않도록 씬 전환을 캡슐화합니다.
/// </summary>
public sealed class SceneTransitionService
{
    private const string BattleSceneName = "BattleScene";
    private const string MainMenuSceneName = "MainMenuScene";

    public void LoadBattleScene()
    {
        SceneManager.LoadScene(BattleSceneName);
    }

    public void LoadMainMenuScene()
    {
        SceneManager.LoadScene(MainMenuSceneName);
    }
}
