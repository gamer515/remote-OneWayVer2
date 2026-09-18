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
        if (legacy == null || player == null || box == null || attacks == null ||
            battleCamera == null || heart == null || mist == null || flatMaterial == null ||
            stages == null || stages.Length != 3)
        { Debug.LogError("BattleScene has missing stage references.", this); enabled = false; return; }
        ActiveStage = stages[Mathf.Clamp(BattleStateMachine.BattleIndex - 1, 0, 2)];
        if (ActiveStage == null) { enabled = false; return; }
        if (attacks.enemyObject != null) attacks.enemyObject.SetActive(false);
        box.HideUI();
        Context = new BattleContext(this);
        player.Configure(Context, heart);
        player.Died += OnDeath;
        player.SetVisible(false);
        player.Lock(true);
        run = StartCoroutine(Execute());
    }
    IEnumerator Execute()
    {
        IBattleFlow flow = ActiveStage.stageIndex == 1 ? (IBattleFlow)new B1BattleFlow() :
            ActiveStage.stageIndex == 2 ? new B2BattleFlow() : new B3BattleFlow();
        yield return flow.Run(Context);
        if (Result != BattleResult.Running) yield break;
        Result = BattleResult.Completed;
        player.Lock(true);
        attacks.CancelSequence();
        Context.Cleanup.Clear();
        if (ActiveStage.returnToDecision)
        {
            yield return new WaitForSecondsRealtime(1f);
            BattleStateMachine.BattleIndex = ActiveStage.stageIndex % 3 + 1;
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
        Context.ShowMessage("쓰러졌다.\nB1 · B2 · B3 버튼으로 다시 시작할 수 있다.", true);
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
