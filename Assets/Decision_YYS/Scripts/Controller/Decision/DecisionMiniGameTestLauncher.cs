using UnityEngine;

/// <summary>
/// DecisionScene을 직접 Play할 때만 미니게임 테스트 버튼을 노출합니다.
/// MainMenu를 거친 정상 플레이에서는 테스트 UI가 자동으로 숨겨집니다.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class DecisionMiniGameTestLauncher : MonoBehaviour
{
    [SerializeField] private GameObject testBar;
    [SerializeField] private DecisionManager decisionManager;
    [SerializeField] private TutorialMiniGameController miniGames;
    [SerializeField] private DuelMiniGameBridge duelBridge;
    [SerializeField] private UnityEngine.UI.Button duelButton;
    [SerializeField] private UnityEngine.UI.Button coinHeadsButton;
    [SerializeField] private UnityEngine.UI.Button coinTailsButton;

    private bool directSceneTest;

    private void Awake()
    {
        directSceneTest = FindFirstObjectByType<GameManager>() == null;
        if (testBar != null) testBar.SetActive(directSceneTest);
        if (!directSceneTest)
        {
            enabled = false;
            return;
        }

        // GameManager 없이 DecisionScene을 바로 실행할 때 정상 스토리 초기화를 막습니다.
        if (decisionManager != null) decisionManager.enabled = false;

        duelButton?.onClick.AddListener(StartDuel);
        coinHeadsButton?.onClick.AddListener(StartHeadsCoinToss);
        coinTailsButton?.onClick.AddListener(StartTailsCoinToss);
    }

    private void StartDuel()
    {
        miniGames?.Cleanup();
        duelBridge?.BeginTestDuel();
    }

    private void StartHeadsCoinToss() => StartCoinToss(true);
    private void StartTailsCoinToss() => StartCoinToss(false);

    private void StartCoinToss(bool choseHeads)
    {
        duelBridge?.CleanupDuel();
        if (miniGames == null) return;
        miniGames.StartCoinToss(choseHeads, (won, heads) =>
            Debug.Log($"[MiniGame Test] 금화={(heads ? "앞" : "뒤")}, 결과={(won ? "승리" : "패배")}", this));
    }

    private void OnDestroy()
    {
        duelButton?.onClick.RemoveListener(StartDuel);
        coinHeadsButton?.onClick.RemoveListener(StartHeadsCoinToss);
        coinTailsButton?.onClick.RemoveListener(StartTailsCoinToss);

        if (!directSceneTest) return;
        duelBridge?.CleanupDuel();
        miniGames?.Cleanup();
    }
}
