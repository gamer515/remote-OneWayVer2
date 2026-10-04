using System;
using JourneyMapKit;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 본 게임/테스트 공통 대련 수명과 피격 횟수, Health 코인 및 승패를 관리합니다.
/// 목표 횟수는 Story에서 전달하며, 본 게임의 코인 차감/저장은 주입된 콜백에 맡깁니다.
/// 마지막 피격 모션이 끝나면 생성물/입력을 정리하고 결과 Story 콜백을 한 번 호출합니다.
/// 사용자 확장 이벤트와 CompleteDuel(bool), 결과 없는 CleanupDuel 진입점도 유지합니다.
/// </summary>
public sealed class DuelMiniGameBridge : MonoBehaviour
{
    [Header("Required References")]
    [SerializeField] private TutorialMiniGameController setupController;
    [SerializeField] private JourneyBoardInput boardInput;
    [SerializeField] private DuelAuthoringReferences authoringReferences;
    [SerializeField] private DuelMouseHandController mouseHandController;
    [SerializeField] private NpcDuelStateMachine npcStateMachine;

    [Header("Connect Your Duel Script Here")]
    [SerializeField] private UnityEvent<GameObject> onDuelStarted = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<DuelAuthoringReferences> onDuelSetupReady =
        new UnityEvent<DuelAuthoringReferences>();
    [SerializeField] private UnityEvent<int> onGearSelected = new UnityEvent<int>();
    [SerializeField] private UnityEvent onYellowPressed = new UnityEvent();
    [SerializeField] private UnityEvent onDuelCleanup = new UnityEvent();

    private Action<bool> completed;
    private bool inputBound;
    private bool testSession;
    private bool boardInputLocked;
    private bool boardInputWasEnabled;
    private Func<int> consumeHealthCoin;
    private UiController feedbackUi;
    private float finishAt;
    private bool finishWon;

    public int HitTarget { get; private set; } = 3;
    public int NpcHits { get; private set; }
    public int PlayerHits { get; private set; }
    public int HealthCoins { get; private set; }
    public bool IsEnding { get; private set; }

    public bool IsRunning { get; private set; }
    public bool IsTestRunning => IsRunning && testSession;
    public GameObject CurrentKnightTarget => setupController != null
        ? setupController.CurrentKnightTarget
        : null;
    public DuelAuthoringReferences AuthoringReferences => authoringReferences;
    /// <summary>대련 생성 담당자가 손/기사 생성 전에 측정한 보드 표면 높이입니다.</summary>
    public float BoardSurfaceY => setupController != null ? setupController.BoardSurfaceY : float.NegativeInfinity;

    public void BeginDuel(Action<bool> completion = null, int hitTarget = 3,
        int healthCoins = Constants.StartingCoinsPerType, Func<int> consumeHealth = null)
    {
        BeginDuelInternal(completion, false, hitTarget, healthCoins, consumeHealth);
    }

    private void BeginDuelInternal(Action<bool> completion, bool isTestSession, int hitTarget,
        int healthCoins, Func<int> consumeHealth)
    {
        CleanupInternal(false);
        completed = completion;
        testSession = isTestSession;
        HitTarget = Mathf.Max(1, hitTarget);
        NpcHits = PlayerHits = 0;
        HealthCoins = Mathf.Max(0, healthCoins);
        consumeHealthCoin = consumeHealth;
        feedbackUi = FindFirstObjectByType<UiController>();
        if (HealthCoins == 0)
        {
            Debug.Log("[Duel] Health 코인 0개 — 패배", this);
            Finish(false);
            return;
        }

        if (setupController == null)
        {
            Debug.LogError("DuelMiniGameBridge: TutorialMiniGameController 참조가 없습니다.", this);
            Finish(false);
            return;
        }

        setupController.StartDuel(created =>
        {
            if (!created || setupController.CurrentKnightTarget == null)
            {
                Debug.LogError("DuelMiniGameBridge: 대련용 기사 복제품을 생성하지 못했습니다.", this);
                Finish(false);
                return;
            }

            IsRunning = true;
            Debug.Log($"[Duel] 시작 — 목표 {HitTarget}회, Health {HealthCoins}개", this);
            authoringReferences?.Prepare(setupController.CurrentKnightTarget);
            if (npcStateMachine == null) npcStateMachine = GetComponent<NpcDuelStateMachine>();
            // 본 게임과 테스트는 같은 대련을 실행합니다. 테스트 여부는 결과 콜백/UI 구분에만 사용합니다.
            boardInputWasEnabled = boardInput == null || boardInput.InputEnabled;
            boardInputLocked = boardInput != null;
            boardInput?.SetInputEnabled(false);
            mouseHandController?.Begin(authoringReferences);
            GetComponent<PlayerDuelAction>()?.BeginDefense(authoringReferences);
            npcStateMachine?.Begin(authoringReferences);
            if (!testSession) BindInput();
            onDuelStarted.Invoke(setupController.CurrentKnightTarget);
            if (authoringReferences != null) onDuelSetupReady.Invoke(authoringReferences);
        });
    }

    /// <summary>Decision 씬의 테스트 버튼에서 같은 규칙의 대련을 실행하되 실제 저장은 변경하지 않습니다.</summary>
    public void BeginTestDuel(Action<bool> completion = null)
    {
        // 독립 테스트도 같은 규칙을 쓰되 실제 저장에는 쓰지 않습니다.
        var inventory = FindFirstObjectByType<CoinDropController>();
        if (inventory != null) inventory.InitializeInventory(new GameProgress().remainingCoins);
        RefreshTestHealthStack(Constants.StartingCoinsPerType);
        BeginDuelInternal(completion, true, 3, Constants.StartingCoinsPerType, inventory == null ? null :
            () =>
            {
                inventory.TryConsumeCoin(0);
                int remaining = inventory.RemainingCoins[0];
                RefreshTestHealthStack(remaining);
                return remaining;
            });
    }

    private static void RefreshTestHealthStack(int count)
    {
        foreach (JourneyCoinStack stack in FindObjectsByType<JourneyCoinStack>(FindObjectsSortMode.None))
            if (stack.name == "Supply_1") stack.SetCount(count);
    }

    /// <summary>사용자 대련 코드가 승패를 판정한 뒤 호출합니다.</summary>
    public void CompleteDuel(bool won) => Finish(won);
    public void CompleteWin() => CompleteDuel(true);
    public void CompleteLoss() => CompleteDuel(false);
    public void NotifyNpcHit()
    {
        if (!IsRunning || IsEnding) return;
        NpcHits++;
        Debug.Log($"[Duel] NPC 피격 {NpcHits}/{HitTarget}", this);
        if (NpcHits >= HitTarget) QueueFinish(true);
    }

    public void NotifyPlayerHit()
    {
        if (!IsRunning || IsEnding) return;
        PlayerHits++;
        HealthCoins = Mathf.Max(0, consumeHealthCoin != null ? consumeHealthCoin() : HealthCoins - 1);
        feedbackUi?.FlashPlayerHit();
        Debug.Log($"[Duel] 플레이어 피격 {PlayerHits}/{HitTarget} — Health {HealthCoins}개", this);
        if (PlayerHits >= HitTarget || HealthCoins == 0) QueueFinish(false);
    }

    private void QueueFinish(bool won)
    {
        IsEnding = true;
        finishWon = won;
        finishAt = Time.time + 0.25f;
        npcStateMachine?.SuspendForRoundEnd();
        GetComponent<PlayerDuelAction>()?.ReleaseDefense();
        mouseHandController?.SetMovementSpeedMultiplier(0f);
        Debug.Log($"[Duel] 종료 확정 — {(won ? "승리" : "패배")}", this);
    }

    private void Update()
    {
        if (IsEnding && Time.time >= finishAt &&
            (GetComponent<PlayerDuelAction>() == null || !GetComponent<PlayerDuelAction>().IsAttacking) &&
            (npcStateMachine == null || !npcStateMachine.IsKnightReacting)) Finish(finishWon);
    }

    /// <summary>결과 처리 없이 생성물과 입력 연결만 정리합니다.</summary>
    public void CleanupDuel() => CleanupInternal(true);

    private void BindInput()
    {
        if (inputBound || boardInput == null) return;
        boardInput.onGearSelected.AddListener(ForwardGearSelection);
        boardInput.onYellowPressed.AddListener(ForwardYellowPressed);
        inputBound = true;
    }

    private void UnbindInput()
    {
        if (!inputBound || boardInput == null) return;
        boardInput.onGearSelected.RemoveListener(ForwardGearSelection);
        boardInput.onYellowPressed.RemoveListener(ForwardYellowPressed);
        inputBound = false;
    }

    private void ForwardGearSelection(int selectedIndex)
    {
        if (IsRunning) onGearSelected.Invoke(selectedIndex);
    }

    private void ForwardYellowPressed()
    {
        if (IsRunning) onYellowPressed.Invoke();
    }

    private void Finish(bool won)
    {
        Action<bool> callback = completed;
        CleanupInternal(true);
        callback?.Invoke(won);
    }

    private void CleanupInternal(bool notifyUserCode)
    {
        bool wasRunning = IsRunning;
        IsRunning = IsEnding = false;
        consumeHealthCoin = null;
        feedbackUi?.ClearPlayerHitFlash();
        npcStateMachine?.StopDuel();
        GetComponent<PlayerDuelAction>()?.StopDefense();
        UnbindInput();
        mouseHandController?.StopControl();
        if (boardInputLocked && boardInput != null)
            boardInput.SetInputEnabled(boardInputWasEnabled);
        boardInputLocked = false;
        authoringReferences?.CleanupDuelObjects();
        setupController?.Cleanup();
        testSession = false;
        completed = null;
        if (notifyUserCode && wasRunning) onDuelCleanup.Invoke();
    }

    private void OnDisable() => CleanupInternal(true);
}
