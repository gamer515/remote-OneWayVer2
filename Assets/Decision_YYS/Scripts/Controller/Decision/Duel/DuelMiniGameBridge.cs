using System;
using JourneyMapKit;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 대련의 생성/종료 흐름과 보드 입력을 사용자가 작성할 실제 대련 코드에 전달합니다.
/// Inspector의 UnityEvent에 사용자 스크립트 함수를 직접 연결하면 됩니다.
/// [흐름] BeginTestDuel → 기사/손 생성 → 마우스·플레이어 방어·NPC 궤적 초기화 → OnDuelSetupReady 사용자 확장.
/// [역할] 수명과 입력 연결만 관리합니다. 판정/애니메이션/승패 규칙은 각각의 대련 스크립트가 담당합니다.
/// 사용자 승패 처리는 CompleteDuel(bool), 결과 없이 종료는 CleanupDuel을 연결합니다.
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
    private bool testInputMode;
    private bool boardInputWasEnabled;

    public bool IsRunning { get; private set; }
    public bool IsTestRunning => IsRunning && testInputMode;
    public GameObject CurrentKnightTarget => setupController != null
        ? setupController.CurrentKnightTarget
        : null;
    public DuelAuthoringReferences AuthoringReferences => authoringReferences;
    /// <summary>대련 생성 담당자가 손/기사 생성 전에 측정한 보드 표면 높이입니다.</summary>
    public float BoardSurfaceY => setupController != null ? setupController.BoardSurfaceY : float.NegativeInfinity;

    public void BeginDuel(Action<bool> completion = null)
    {
        BeginDuelInternal(completion, false);
    }

    private void BeginDuelInternal(Action<bool> completion, bool useTestMouseInput)
    {
        CleanupInternal(false);
        completed = completion;
        testInputMode = useTestMouseInput;

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
            authoringReferences?.Prepare(setupController.CurrentKnightTarget);
            if (npcStateMachine == null) npcStateMachine = GetComponent<NpcDuelStateMachine>();
            if (testInputMode)
            {
                boardInputWasEnabled = boardInput == null || boardInput.InputEnabled;
                boardInput?.SetInputEnabled(false);
                mouseHandController?.Begin(authoringReferences);
                // 기본 기능은 여기서 한 번 시작합니다. UnityEvent는 사용자 확장 코드용입니다.
                GetComponent<PlayerDuelAction>()?.BeginDefense(authoringReferences);
                npcStateMachine?.Begin(authoringReferences);
            }
            else
            {
                BindInput();
            }
            onDuelStarted.Invoke(setupController.CurrentKnightTarget);
            if (authoringReferences != null) onDuelSetupReady.Invoke(authoringReferences);
        });
    }

    /// <summary>Decision 씬의 테스트 버튼에서 결과 콜백 없이 대련만 시작합니다.</summary>
    public void BeginTestDuel() => BeginDuelInternal(null, true);

    /// <summary>사용자 대련 코드가 승패를 판정한 뒤 호출합니다.</summary>
    public void CompleteDuel(bool won) => Finish(won);
    public void CompleteWin() => CompleteDuel(true);
    public void CompleteLoss() => CompleteDuel(false);
    public void NotifyNpcHit() => CompleteWin();

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
        npcStateMachine?.StopDuel();
        GetComponent<PlayerDuelAction>()?.StopDefense();
        UnbindInput();
        mouseHandController?.StopControl();
        if (testInputMode && boardInput != null)
            boardInput.SetInputEnabled(boardInputWasEnabled);
        authoringReferences?.CleanupDuelObjects();
        setupController?.Cleanup();
        IsRunning = false;
        testInputMode = false;
        completed = null;
        if (notifyUserCode && wasRunning) onDuelCleanup.Invoke();
    }

    private void OnDisable() => CleanupInternal(true);
}
