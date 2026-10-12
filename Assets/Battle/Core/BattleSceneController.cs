using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-50)]
public sealed class BattleSceneController : MonoBehaviour
{
    public BattleStateMachine legacy;
    public PlayerController player;
    public BattleSceneBattleBoxController box;
    public AttackPatternManager attacks;
    public Camera battleCamera;
    public Sprite heart;
    public Sprite mist;

    [Header("B1 내려오는 안개")]
    public Sprite[] b1MistSprites = new Sprite[3];

    // 작을수록 자주 생성됨.
    [Min(0.05f)]
    public float b1MistSpawnInterval = 1.5f;

    // X: 최소 속도, Y: 최대 속도. 단위는 초당 월드 거리.
    public Vector2 b1MistSpeedRange = new Vector2(0.5f, 1.2f);

    // X: 최소 너비, Y: 최대 너비. 단위는 월드 거리.
    public Vector2 b1MistWidthRange = new Vector2(4f, 7f);

    [Range(0f, 1f)]
    public float b1MistOpacity = 0.45f;

    // 시작할 때 화면 위쪽에 미리 배치할 개수.
    [Min(0)]
    public int b1MistInitialCount = 4;

    // 너무 많이 쌓이지 않도록 제한.
    [Min(1)]
    public int b1MistMaxCount = 40;

    public Material flatMaterial;
    public BattleStageData[] stages;
    public BattleStageData ActiveStage { get; private set; }
    public BattleStartData StartData { get; private set; }
    public int DebugPatternStepIndex { get; private set; } = -1;

    public bool DebugContinueFromHere { get; private set; }

    public bool IsPatternPreview =>
        ActiveStage != null &&
        DebugPatternStepIndex >= 0;
    public BattleResult Result { get; private set; } = BattleResult.Running;
    public string CurrentStep { get; set; }
    public BattleContext Context { get; private set; }
    Coroutine run;
    bool cancelled;


    [Header("B1 돌 이미지")]
    public Sprite b1Rock1;
    public Sprite b1Rock2;
    public Sprite b1BrokenRockFragment;

    [Header("B1 돌 크기와 분열")]
    public Vector2 b1DebrisSizeRange = new Vector2(0.15f, 0.8f);

    [Min(0.01f)]
    public float b1LargeRockThreshold = 0.45f;

    [Min(0.05f)]
    public float b1SplitDelay = 1f;

    [Range(0f, 1f)]
    public float b1SplitChance = 0.3f;

    [Header("B1 돌 튕김")]
    public Vector2 b1RockBounceWidthRange = new Vector2(0.3f, 0.7f);
    public Vector2 b1RockBounceDurationRange = new Vector2(0.5f, 0.9f);

    [Range(0f, 0.95f)]
    public float b1RockBounceSlowdown = 0.65f;


    void Awake()
    {
        if (legacy != null) legacy.sequenceController = this;
    }
    void Start()
    {
        if (legacy == null || player == null || box == null ||
            attacks == null || battleCamera == null ||
            heart == null || mist == null || flatMaterial == null ||
            stages == null || stages.Length == 0)
        {
            Debug.LogError("BattleScene has missing stage references.", this);
            enabled = false;
            return;
        }

        // 전달값을 받는 곳은 여기 한 곳뿐입니다.
        StartData = BattleEntry.TakeOrDefault();
        int requestedStep = BattleDebugController.TakePatternStep(
    out int requestedStage,
    out bool continueFromHere);

        // 배열 순서와 관계없이 전투 번호로 데이터를 찾는다.
        foreach (var stage in stages)
        {
            if (stage == null || stage.stageIndex != StartData.StageIndex)
                continue;
            if (ActiveStage != null)
            {
                Debug.LogError($"{StartData.DisplayName} 데이터가 중복 등록되었습니다.", this);
                enabled = false;
                return;
            }
            ActiveStage = stage;
        }

        if (ActiveStage == null ||
            ActiveStage.stageIndex != StartData.StageIndex)
        {
            Debug.LogError(
                $"{StartData.DisplayName}의 스테이지 설정을 확인해주세요.",
                this);
            enabled = false;
            return;
        }

        if (requestedStage == StartData.StageIndex &&
    requestedStep >= 0)
        {
            var definitions = ActiveStage.flow.steps;

            if (requestedStep < definitions.Count &&
                BattlePatternDebugPanel.IsSelectable(
                    definitions[requestedStep]))
            {
                DebugPatternStepIndex = requestedStep;
                DebugContinueFromHere = continueFromHere;
            }
            else
            {
                Debug.LogWarning(
                    "패턴 선택값이 올바르지 않아 전체 전투를 실행합니다.",
                    this);
            }
        }

        // 같은 데이터 객체를 다른 전투 클래스에도 전달합니다.
        legacy.Initialize(StartData);
        attacks.Initialize(StartData);

        Debug.Log(
            $"[Battle Start] {StartData.DisplayName}, " +
            $"Traits: {StartData.Trait1}, {StartData.Trait2}, " +
            $"{StartData.Trait3}, {StartData.Trait4}",
            this);

        if (attacks.enemyObject != null)
            attacks.enemyObject.SetActive(false);

        box.HideUI();

        Context = new BattleContext(this);
        player.Configure(Context, heart);
        player.Died += OnDeath;
        player.SetVisible(false);
        player.Lock(true);

        if (ActiveStage.stageIndex == 2 ||
    ActiveStage.stageIndex == 4)
        {
            var debug =
                FindFirstObjectByType<BattleDebugController>();

            if (debug == null)
                debug = gameObject.AddComponent<BattleDebugController>();

            BattlePatternDebugPanel.Create(this, debug);
        }

        run = StartCoroutine(Execute());
    }
    IEnumerator Execute()
    {
        IBattleFlow flow;
        switch (ActiveStage.stageIndex)
        {
            case 1: flow = new B1BattleFlow(); break;
            case 2: flow = new B2BattleFlow(); break;
            case 3: flow = new B3BattleFlow(); break;
            case 4: flow = new BDBattleFlow(); break;
            default:
                Debug.LogError("등록되지 않은 전투 실행 방식입니다.", this);
                yield break;
        }
        yield return flow.Run(Context);
        if (Result != BattleResult.Running) yield break;
        Result = BattleResult.Completed;
        player.Lock(true);
        attacks.CancelSequence();
        Context.Cleanup.Clear();
        if (IsPatternPreview)
        {
            string message = DebugContinueFromHere
                ? "선택한 단계부터의 연속 실행이 끝났습니다."
                : "선택한 패턴의 단독 실행이 끝났습니다.";

            Context.ShowMessage(
                message + "\n왼쪽 위 버튼으로 다시 선택할 수 있습니다.",
                false);

            yield break;
        }
        if (ActiveStage.returnToDecision)
        {
            yield return new WaitForSecondsRealtime(1f);
            
            CancelBattle();
            SceneManager.LoadScene("DecisionScene");
        }
        else Context.ShowMessage("전투 완료", false);
    }
    void OnDeath()
    {
        if (Result != BattleResult.Running) return;
        Result = BattleResult.Failed;
        if (run != null) StopCoroutine(run);
        attacks.CancelSequence();
        box.CancelAnimations();
        legacy.gaugeManager?.CancelGauge();
        Context.Cleanup.Clear();
        Context.Clock.Paused = false;
        player.SetPaused(false);
        player.Lock(true);
        player.SetVisible(false);
        Context.SlashLine.gameObject.SetActive(false);
        Context.ShowMessage("쓰러졌다.\nB1 · B2 · B3 · BD 버튼으로 다시 시작할 수 있다.", true);
    }
    public void CancelBattle()
    {
        if (cancelled) return;
        cancelled = true;
        StopAllCoroutines();
        if (Result == BattleResult.Running) Result = BattleResult.Cancelled;
        if (attacks != null) attacks.CancelSequence();
        if (box != null) box.CancelAnimations();
        if (legacy != null) legacy.gaugeManager?.CancelGauge();
        if (player != null) { player.Died -= OnDeath; player.Lock(true); player.SetPaused(false); }
        if (Context != null)
        {
            Context.Clock.Paused = false;
            Context.Cleanup.Clear();
            if (Context.RuntimeRoot != null) Destroy(Context.RuntimeRoot.gameObject);
        }
    }
    void OnDestroy() => CancelBattle();
}
