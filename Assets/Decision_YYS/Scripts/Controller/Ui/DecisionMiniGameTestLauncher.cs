using UnityEngine;
using JourneyMapKit;
using TMPro;

/// <summary>
/// DecisionScene 직접 실행용 Duel/도박 두 모드 선택 UI입니다.
/// Duel은 프로젝트 DuelMiniGameBridge, 도박은 기존 TutorialMiniGameController.StartCoinToss를 재사용합니다.
/// 모드를 바꾸면 생성물/커서/입력을 정리하고, MainMenu를 거친 정상 플레이에서는 UI를 숨깁니다.
/// 별도 테스트 게임 로직이나 저장 데이터는 만들지 않습니다.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class DecisionMiniGameTestLauncher : MonoBehaviour
{
    public enum TestMode { None, Duel, Gamble }
    [SerializeField] private GameObject testBar;
    [SerializeField] private DecisionManager decisionManager;
    [SerializeField] private TutorialMiniGameController miniGames;
    [SerializeField] private DuelMiniGameBridge duelBridge;
    [SerializeField] private JourneyBoardInput boardInput;
    [Header("Two Mode Buttons")]
    [SerializeField] private UnityEngine.UI.Button duelButton;
    [SerializeField] private UnityEngine.UI.Button gambleButton;
    [Header("Gamble Mode — choose a face")]
    [SerializeField] private GameObject gamblePanel;
    [SerializeField] private UnityEngine.UI.Button coinHeadsButton;
    [SerializeField] private UnityEngine.UI.Button coinTailsButton;
    [SerializeField] private TMP_Text gambleStatus;
    private bool directSceneTest, gambleInputLocked, boardInputWasEnabled;
    public TestMode CurrentMode { get; private set; }
    public bool IsCoinTossRunning { get; private set; }

    private void Awake()
    {
        directSceneTest = FindFirstObjectByType<GameManager>() == null;
        if (testBar != null) testBar.SetActive(directSceneTest);
        if (gamblePanel != null) gamblePanel.SetActive(false);
        if (!directSceneTest) { enabled = false; return; }
        // 스토리 초기화/저장 없이 현재 DecisionScene의 실제 미니게임 구성만 검사합니다.
        if (decisionManager != null) decisionManager.enabled = false;
        if (boardInput == null) boardInput = FindFirstObjectByType<JourneyBoardInput>();
        if (duelButton != null) duelButton.onClick.AddListener(StartDuel);
        if (gambleButton != null) gambleButton.onClick.AddListener(StartGamble);
        if (coinHeadsButton != null) coinHeadsButton.onClick.AddListener(StartHeadsCoinToss);
        if (coinTailsButton != null) coinTailsButton.onClick.AddListener(StartTailsCoinToss);
    }
    public void StartDuel()
    {
        if (!directSceneTest) return;
        StopTest();
        CurrentMode = TestMode.Duel;
        if (duelBridge != null) duelBridge.BeginTestDuel();
    }
    public void StartGamble()
    {
        if (!directSceneTest) return;
        StopTest();
        CurrentMode = TestMode.Gamble;
        if (boardInput != null)
        {
            boardInputWasEnabled = boardInput.InputEnabled;
            gambleInputLocked = true;
            boardInput.SetInputEnabled(false);
        }
        if (gamblePanel != null) gamblePanel.SetActive(true);
        SetChoicesEnabled(true);
        SetGambleStatus("앞 또는 뒤를 고르세요.");
    }
    public void StartHeadsCoinToss() => StartCoinToss(true);
    public void StartTailsCoinToss() => StartCoinToss(false);
    private void StartCoinToss(bool choseHeads)
    {
        if (!directSceneTest || CurrentMode != TestMode.Gamble || IsCoinTossRunning || miniGames == null) return;
        IsCoinTossRunning = true;
        SetChoicesEnabled(false);
        SetGambleStatus($"{(choseHeads ? "앞" : "뒤")} 선택 - 동전을 던집니다...");
        miniGames.StartCoinToss(choseHeads, (won, heads) =>
        {
            if (this == null || CurrentMode != TestMode.Gamble) return;
            IsCoinTossRunning = false;
            SetChoicesEnabled(true);
            SetGambleStatus($"{(heads ? "앞" : "뒤")}! {(won ? "승리" : "패배")}\n다시 선택하세요.");
            Debug.Log($"[도박 Test] 금화={(heads ? "앞" : "뒤")}, 결과={(won ? "승리" : "패배")}", this);
        });
    }
    /// <summary>모드 전환/종료 시 공통 정리입니다. 프로젝트 입력의 원래 활성 상태를 복원합니다.</summary>
    public void StopTest()
    {
        if (!directSceneTest) return;
        if (duelBridge != null) duelBridge.CleanupDuel();
        if (miniGames != null) miniGames.Cleanup();
        if (gambleInputLocked && boardInput != null) boardInput.SetInputEnabled(boardInputWasEnabled);
        gambleInputLocked = false;
        IsCoinTossRunning = false;
        CurrentMode = TestMode.None;
        if (gamblePanel != null) gamblePanel.SetActive(false);
    }
    private void SetChoicesEnabled(bool value)
    {
        if (coinHeadsButton != null) coinHeadsButton.interactable = value;
        if (coinTailsButton != null) coinTailsButton.interactable = value;
    }
    private void SetGambleStatus(string value) { if (gambleStatus != null) gambleStatus.text = value; }
    private void OnDisable() => StopTest();
    private void OnDestroy()
    {
        if (duelButton != null) duelButton.onClick.RemoveListener(StartDuel);
        if (gambleButton != null) gambleButton.onClick.RemoveListener(StartGamble);
        if (coinHeadsButton != null) coinHeadsButton.onClick.RemoveListener(StartHeadsCoinToss);
        if (coinTailsButton != null) coinTailsButton.onClick.RemoveListener(StartTailsCoinToss);
        StopTest();
    }
}
