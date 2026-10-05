using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// [역할] 본 게임/테스트 공용 NPC 손의 궤적 → 준비 → 세로 베기(2→3 / 1→4) → 복귀를 담당합니다.
/// [범위] 사용자 Slash 클립의 Begin~중간 타격까지 3D 칼날 접촉 + 실제 방어 모션이면 방어 성공, 실패하면 기사 피격입니다.
/// Vertical Attack Pose가 연결되면 부모를 그 월드 배치로 이동하고, 공격 중 자식 원본 클립만 재생합니다.
/// [유지] 손 생성 연결, 뒤쪽 Z 배치, 보드 높이 제한, Catmull-Rom 궤적, 원본 Animator/클립.
/// 화면 궤적의 X/Y는 Screen Path로 정합니다. NPC Patrol Pose는 그 깊이와 이동 회전을 정합니다.
/// 직접 지정한 월드 Path Points에만 NPC Patrol Pose의 최저점 위치 보정을 적용합니다.
/// 비어 있을 때만 자동으로 검을 세우고 마주보게 합니다. 공격 미리보기에는 보정하지 않습니다.
/// 플레이어 입력/손 조작은 별도 스크립트에서 유지합니다.
/// </summary>
public class NpcDuelStateMachine : MonoBehaviour
{
    // 기존 스크립트/직렬화 이름과 값을 유지합니다.
    public enum DuelState { Stopped, Patrol, Telegraph, Attack, Return, Blocked }
    public enum AttackKind { Slash, Thrust }
    public enum VerticalColumn { Left_2To3, Right_1To4 }
    [Header("References")]
    [SerializeField] private DrawGizmoAreas areas;
    [SerializeField] private DuelMouseHandController playerHand;
    [SerializeField] private PlayerDuelAction playerDefense;
    [SerializeField] private Camera movementCamera;
    [Tooltip("방어 성공 시 실제 칼날 접점에서 재생할 타격 효과 프리팹입니다. 모든 자식 파티클이 끝나면 정리합니다.")]
    [SerializeField] private GameObject blockImpactPrefab;
    // 제작 요청으로 연결하는 고정 효과입니다. 추가 Inspector 조절 항목은 노출하지 않습니다.
    [SerializeField, HideInInspector] private GameObject playerHitBloodPrefab;
    [SerializeField, HideInInspector] private GameObject npcHitBloodPrefab;
    [Header("Patrol Path — preserved")]
    [Tooltip("2개 이상 지정하면 월드 포인트 곡선입니다. 이 경우 NPC Patrol Pose가 연결되면 최저점을 그 위치에 맞춰 평행 이동합니다. 비우면 Screen Path의 화면 위치를 그대로 사용합니다.")]
    [SerializeField] private Transform[] pathPoints = new Transform[0];
    [Tooltip("NPC 화면 영역 중심 기준 곡선의 점입니다. X/Y=1은 영역 전체 너비/높이, 양수 X=오른쪽, 양수 Y=위쪽. NPC Patrol Pose의 X/Y는 더하지 않으므로 보이는 곡선 위치를 여기서 직접 조절합니다.")]
    [SerializeField] private Vector2[] screenPath = {
        new Vector2(-0.8f, 0.15f), new Vector2(-0.5f, -0.65f),
        new Vector2(0f, -0.8f), new Vector2(0.5f, -0.65f), new Vector2(0.8f, 0.15f)
    };
    [SerializeField, Min(0.1f)] private float pathTravelSeconds = 3f;
    [Tooltip("처음 생성된 손이 기존 궤적 시작점으로 이동할 시간입니다.")]
    [SerializeField, Min(0.05f)] private float returnSeconds = 0.5f;
    [SerializeField] private bool waitForPlayerCapture = true;
    [Tooltip("궤적 이동 중 칼날 시작→끝 방향을 월드 +Y로 세웁니다. 생성 대기 자세와 원본 Slash/Thrust 미리보기는 보정하지 않습니다. 자식 Animator/클립은 변경하지 않습니다.")]
    [SerializeField] private bool keepPatrolBladeUpright = true;
    [Tooltip("검을 세운 뒤 NPC 손의 정면(+Z)을 플레이어 준비 자세의 정면과 반대로 맞춥니다. 오른클릭 방어 회전은 따라하지 않고, 궤적/위치/원본 공격 미리보기는 변경하지 않습니다.")]
    [SerializeField] private bool facePlayerDuringPatrol = true;
    [Tooltip("검을 세우고 마주보게 한 뒤 손바닥 쪽으로 기울이는 각도입니다. 0=수직, 양수=손바닥 쪽, 음수=손등 쪽. 생성 대기/공격 미리보기에는 적용하지 않습니다.")]
    [SerializeField, Range(-30f, 30f)] private float patrolPalmTiltDegrees = 10f;
    [Header("Vertical Slash")]
    [Tooltip("손을 잡고 궤적 이동 중 세로 공격을 반복합니다. 꺼 두면 기존 궤적/미리보기만 동작합니다.")]
    [SerializeField] private bool enableVerticalAttacks = true;
    [Tooltip("세로 공격 준비 배치용 Transform입니다. 부모 손의 월드 위치/회전만 이 배치로 이동한 뒤 자식 Slash 클립을 재생합니다. 연결되면 공격 중 칼날 방향/위치/보드 높이를 강제로 보정하지 않습니다. 비우면 기존 화면 영역 보정을 사용합니다. 생성 자세/궤적과 별개입니다.")]
    [SerializeField] private Transform verticalAttackPose;
    [Tooltip("순서대로 반복할 세로 공격입니다. Left=2→3, Right=1→4. 빈 배열이면 공격하지 않습니다.")]
    [SerializeField] private VerticalColumn[] verticalPattern = { VerticalColumn.Left_2To3, VerticalColumn.Right_1To4 };
    [Tooltip("각 공격 사이에 기존 궤적을 따라 이동하는 시간(초)입니다.")]
    [SerializeField, Min(0.1f)] private float attackInterval = 3f;
    [Tooltip("궤적에서 Vertical Attack Pose로 부모 손을 이동/회전하는 시간(초)입니다. 자세 참조가 없을 때만 위쪽 영역으로 칼날을 보정합니다.")]
    [SerializeField, Min(0.05f)] private float telegraphSeconds = 0.65f;
    [Tooltip("애니메이션 이벤트가 없을 때 사용할 베기 구간(클립 비율 0~1). 이벤트가 있으면 클립의 Begin/EndAttackHit 시간을 우선합니다.")]
    [SerializeField] private Vector2 fallbackSlashWindow = new Vector2(0.25f, 0.75f);
    [Tooltip("Vertical Attack Pose가 비어 있을 때만 사용하는 공격 카메라 깊이 오프셋입니다. 자세 참조가 연결되면 적용하지 않습니다.")]
    [SerializeField, Min(0f)] private float verticalAttackDepthOffset = 0.25f;
    [Tooltip("Vertical Attack Pose가 비어 있을 때만 사용하는 준비/종료 검 각도입니다. 카메라 정면이 0도, 음수=위로 들기, 양수=아래로 베기. 자세 참조가 연결되면 적용하지 않습니다.")]
    [SerializeField] private Vector2 verticalBladeAngles = new Vector2(-45f, 80f);
    [Tooltip("베기 시작 때 공격 종류와 1~4 영역 번호를 Console에 한 번 출력합니다. 피해 판정을 뜻하지 않습니다.")]
    [SerializeField] private bool logAttacks = true;
    [Header("Your Animator States — clips preserved")]
    [SerializeField] private string idleState = "Idle";
    [SerializeField] private string slashState = "Slash";
    [SerializeField] private string thrustState = "Thrust";
    [Tooltip("종료 이벤트/상태가 잘못되었을 때 궤적으로 복귀할 최대 시간. 정상 공격은 기존 FinishAttack 이벤트로 끝납니다.")]
    [SerializeField, Min(0.5f)] private float animationTimeout = 5f;
    [Header("Knight Hit Reaction")]
    [Tooltip("복제 기사 Animator에서 재생할 피격 State 이름입니다. 클립 이름이 아닌 State 이름이며, 현재는 HumanArmature_Attacked입니다. 원본 Controller/클립은 변경하지 않습니다.")]
    [SerializeField] private string knightAttackedState = "HumanArmature_Attacked";
    [Tooltip("기사 피격 클립이 끝나지 않을 경우 원래 자세로 복구할 최대 시간(초)입니다. 정상적으로는 클립 1회 완료 후 복구합니다.")]
    [SerializeField, Min(0.1f)] private float knightHitTimeout = 2f;
    [Header("NPC Path Placement (world units)")]
    [Tooltip("NPC Patrol Pose가 비어 있을 때 사용하는 궤적의 월드 +Z 오프셋입니다. 연결된 자세가 있으면 그 Z를 사용합니다. 최초 생성 대기 위치는 변경하지 않습니다.")]
    [SerializeField] private float pathBackOffsetZ = 1.5f;
    [Tooltip("보드 윗면과 NPC 손 메시 아랫부분 사이 최소 간격. 검 끝은 제한 대상이 아닙니다.")]
    [SerializeField, Min(0f)] private float handBoardClearance = 0.12f;
    [Header("Existing Player Attack Callback — not NPC offense")]
    [Tooltip("플레이어가 기존 NPC 영역에 공격 제스처를 했을 때 호출합니다. NPC 공격에 대한 방어 접촉과는 별개입니다.")]
    [SerializeField] private UnityEvent onNpcHit = new UnityEvent();
    [Header("Runtime State")]
    [SerializeField] private DuelState state = DuelState.Stopped;
    public DuelState State => state;
    /// <summary>현재 공격/미리보기 종류. 세로 베기는 Slash입니다.</summary>
    public AttackKind CurrentAttack => previewKind;
    /// <summary>현재 세로 베기의 시작 영역 번호(1 또는 2). 0=공격을 아직 선택하지 않음.</summary>
    public int AttackStartRegion { get; private set; }
    /// <summary>현재 세로 베기의 끝 영역 번호(3 또는 4).</summary>
    public int AttackEndRegion { get; private set; }
    /// <summary>이번 공격에서 실제 방어 모션 중 칼날 접촉으로 방어에 성공했는지 여부입니다.</summary>
    public bool AttackBlocked { get; private set; }

    private Transform hand;
    private DuelMiniGameBridge roundBridge;
    private bool offenseSuspended;
    public bool IsKnightReacting => knightHitPlaying;

    public void SuspendForRoundEnd()
    {
        offenseSuspended = true;
        // 마지막 피격을 확정해도 이미 진행 중인 베기는 끝까지 보여줍니다.
        if (handAnimator != null && state != DuelState.Attack) handAnimator.speed = 0f;
    }
    private Animator handAnimator;
    private DuelBladeHitbox npcBlade, playerBlade;
    private Renderer[] handRenderers;
    private Transform palmWrist, palmIndex, palmPinky;
    private float motionPlaneZ, minimumRootY, boardSurfaceY, elapsed, patrolClock, previewElapsed;
    private Vector3 motionStart, returnPoint;
    private Transform patrolPose;
    private Vector3 patrolPathOffset;
    private bool previewPlaying;
    private bool holdingSceneStartPose;
    private AttackKind previewKind;
    private int patternIndex;
    private Vector2 preparationStart;
    private float preparationDepth;
    private Vector3 preparationDirection;
    private float slashLength, slashStart, slashEnd;
    private bool attackLogged, slashEnded;
    private bool endAttackPending, finishAttackPending, attackResolved, contactHistoryValid, previouslyDefending;
    private DuelBladeHitbox.Pose previousNpcPose, previousPlayerPose;
    private float previousContactTime;
    private GameObject blockContactEffect;
    private ParticleSystem blockContactParticles;
    private float animatorPlaybackSpeed = 1f;
    private bool attackUsesPose;
    private Vector3 preparationRootPosition, attackRootPosition;
    private Quaternion preparationRootRotation, attackRootRotation;
    private bool IsPoseAttack => attackUsesPose && !previewPlaying &&
        (state == DuelState.Telegraph || state == DuelState.Attack);
    private Animator knightAnimator;
    private bool knightAnimatorWasEnabled, knightRootMotionWasEnabled, knightHitPlaying, knightHitPlayed;
    private float knightAnimatorSpeed, knightHitElapsed;
    private KnightPose[] knightRestPose;
    private const string KnightIdleState = "HumanArmature_New_Idle_swordRight";
    private bool knightIdleAvailable;
    private KnightPose knightRootPose;
    private Collider knightHitBody;
    private readonly System.Collections.Generic.List<GameObject> bloodEffects = new System.Collections.Generic.List<GameObject>();
    public bool HasPendingHitEffects => bloodEffects.Exists(effect => effect != null && HasLivingParticles(effect));
    private struct KnightPose
    {
        public Transform target;
        public Vector3 position, scale;
        public Quaternion rotation;
    }

    public void Begin(DuelAuthoringReferences references)
    {
        StopDuel();
        var bridge = GetComponent<DuelMiniGameBridge>();
        roundBridge = bridge;
        if (bridge != null && !bridge.IsRunning) return;
        if (areas == null) areas = GetComponent<DrawGizmoAreas>();
        if (playerHand == null) playerHand = GetComponent<DuelMouseHandController>();
        if (playerDefense == null) playerDefense = GetComponent<PlayerDuelAction>();
        if (movementCamera == null) movementCamera = Camera.main;
        hand = references != null && references.SpawnedNpcHand != null ? references.SpawnedNpcHand.transform : null;
        if (hand == null || areas == null || movementCamera == null)
        {
            Debug.LogWarning("NPC 손 프리팹, 궤적 영역, 카메라 참조를 확인하세요.", this);
            hand = null; return;
        }
        patrolPose = references.NpcHandPatrolPose;
        motionPlaneZ = patrolPose != null ? patrolPose.position.z :
            (references.NpcHandSpawnPoint != null ? references.NpcHandSpawnPoint.position.z : hand.position.z) + pathBackOffsetZ;
        // 최초 생성은 항상 Prepare의 배치 그대로 유지합니다. 이동 궤적의 Z/Y 보정은 잡은 뒤 시작합니다.
        holdingSceneStartPose = waitForPlayerCapture;
        if (!holdingSceneStartPose)
        {
            Vector3 position = hand.position; position.z = motionPlaneZ; hand.position = position;
        }
        handAnimator = hand.GetComponentInChildren<Animator>();
        if (handAnimator != null) animatorPlaybackSpeed = handAnimator.speed;
        InitializeKnightReaction(references.KnightTarget);
        npcBlade = hand.GetComponentInChildren<DuelBladeHitbox>();
        // 현재 왼손 리그의 손목과 두 손가락 뿌리로 실제 손바닥 면을 구합니다.
        // 이동 부모의 forward(플레이어 방향)와 손바닥 방향은 서로 다른 기준입니다.
        foreach (Transform bone in hand.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name == "DEF-hand.L") palmWrist = bone;
            else if (bone.name == "DEF-f_index.01.L") palmIndex = bone;
            else if (bone.name == "DEF-f_pinky.01.L") palmPinky = bone;
        }
        playerBlade = references.SpawnedPlayerHand != null ? references.SpawnedPlayerHand.GetComponentInChildren<DuelBladeHitbox>() : null;
        ConfigureBoardLimit(references);
        if (!holdingSceneStartPose) KeepHandAboveBoard();
        foreach (var receiver in hand.GetComponentsInChildren<DuelAnimationEvents>(true)) receiver.Initialize(this);
        PlayState(idleState);
        if (patrolPose != null)
        {
            // 생성 대기 자세의 메시 높이를 이동 자세의 높이 제한으로 잘못 사용하지 않습니다.
            Quaternion startRotation = hand.rotation;
            hand.rotation = patrolPose.rotation;
            if (TryGetHandBottom(out float bottom))
                minimumRootY = boardSurfaceY + handBoardClearance - (bottom - hand.position.y);
            hand.rotation = startRotation;
        }
        npcBlade?.ResetHistory(); playerBlade?.ResetHistory();
        areas.RefreshUiAreas();
        // 화면 경로는 화면 영역 자체를 기준으로 합니다. 이동 자세의 X/Y를 더하면
        // Inspector에서 정한 화면 곡선이 왼쪽/아래로 밀리므로 월드 경로에만 보정합니다.
        patrolPathOffset = UsesWorldPath && patrolPose != null ? patrolPose.position - LowestRawPathPoint() : Vector3.zero;
        motionStart = hand.position; returnPoint = EvaluatePath(0f);
        elapsed = patrolClock = 0f; patternIndex = 0; state = DuelState.Return;
    }
    public void StopDuel()
    {
        offenseSuspended = false;
        if (blockContactEffect != null) Destroy(blockContactEffect);
        blockContactEffect = null;
        blockContactParticles = null;
        foreach (var effect in bloodEffects) if (effect != null) Destroy(effect);
        bloodEffects.Clear();
        FinishKnightReaction();
        if (knightAnimator != null)
        {
            knightAnimator.enabled = knightAnimatorWasEnabled;
            knightAnimator.speed = knightAnimatorSpeed;
            knightAnimator.applyRootMotion = knightRootMotionWasEnabled;
        }
        knightAnimator = null; knightRestPose = null; knightHitPlayed = false;
        knightIdleAvailable = false;
        knightHitBody = null;
        knightRootPose = default;
        if (handAnimator != null) handAnimator.speed = animatorPlaybackSpeed;
        npcBlade?.SetLiveTrail(false); playerBlade?.SetLiveTrail(false);
        npcBlade?.SetMotionActive(false); playerBlade?.SetMotionActive(false);
        npcBlade?.ResetHistory(); playerBlade?.ResetHistory();
        hand = null; handAnimator = null; npcBlade = playerBlade = null; handRenderers = null;
        palmWrist = palmIndex = palmPinky = null;
        patrolPose = null; patrolPathOffset = Vector3.zero;
        minimumRootY = boardSurfaceY = float.NegativeInfinity;
        previewPlaying = false; holdingSceneStartPose = false; state = DuelState.Stopped;
        AttackStartRegion = AttackEndRegion = 0; attackLogged = slashEnded = false;
        attackUsesPose = false;
        AttackBlocked = endAttackPending = finishAttackPending = attackResolved = contactHistoryValid = previouslyDefending = false;
    }
    private void Update()
    {
        if (hand == null || state == DuelState.Stopped) return;
        if (offenseSuspended)
        {
            UpdateKnightReaction(false);
            if (handAnimator != null && state == DuelState.Attack && AttackClipTime >= slashLength) handAnimator.speed = 0f;
            return;
        }
        bool waiting = waitForPlayerCapture && (playerHand == null || !playerHand.IsCaptured) && !previewPlaying;
        UpdateKnightReaction(waiting);
        if (handAnimator != null) handAnimator.speed = waiting ? 0f : animatorPlaybackSpeed;
        if (waiting) return;
        if (previewPlaying)
        {
            previewElapsed += Time.deltaTime;
            if (previewElapsed >= animationTimeout) FinishAttack();
            return;
        }
        holdingSceneStartPose = false;
        areas.RefreshUiAreas(); elapsed += Time.deltaTime;
        if (state == DuelState.Return)
        {
            hand.position = Vector3.Lerp(motionStart, returnPoint, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / returnSeconds)));
            if (elapsed >= returnSeconds) { state = DuelState.Patrol; elapsed = 0f; }
        }
        else if (state == DuelState.Patrol)
        {
            patrolClock += Time.deltaTime;
            hand.position = EvaluatePath(Mathf.PingPong(patrolClock / Mathf.Max(0.1f, pathTravelSeconds), 1f));
            if (enableVerticalAttacks && elapsed >= attackInterval) PrepareVerticalAttack();
        }
        else if (state == DuelState.Telegraph && elapsed >= telegraphSeconds) StartVerticalAttack();
        else if (state == DuelState.Attack)
        {
            // Animator 평가/칼날 접촉 뒤 LateUpdate에서 종료합니다. 조기 피격이나 마지막 접촉 누락을 막습니다.
            if (elapsed >= animationTimeout) FinishAttack();
        }
        // 지정 자세 공격에서는 부모 배치와 자식 애니메이션을 분리합니다.
    }
    private void LateUpdate()
    {
        RestoreKnightRoot();
        for (int i = bloodEffects.Count - 1; i >= 0; i--)
            if (bloodEffects[i] == null || !HasLivingParticles(bloodEffects[i]))
            {
                if (bloodEffects[i] != null) Destroy(bloodEffects[i]);
                bloodEffects.RemoveAt(i);
            }
        // 검/손에 붙이지 않은 접점 효과입니다. 방어 종료나 입력 대기 중에도 수명을 정리합니다.
        if (blockContactEffect != null && blockContactParticles != null && !blockContactParticles.IsAlive(true))
        {
            Destroy(blockContactEffect);
            blockContactEffect = null;
            blockContactParticles = null;
        }
        if (hand == null || offenseSuspended) return;
        // 부모/원본 방어 모션 보정을 끝낸 실제 칼날 자세로 검사합니다.
        playerDefense?.ApplyGuardPose(movementCamera); playerHand?.ApplyRecoil();
        // 공격 이벤트와 무관한 시각화: 순찰, 준비, 방어, 마우스 이동도 실제 칼끝에서 기록합니다.
        bool playerCaptured = playerHand != null && playerHand.IsCaptured;
        npcBlade?.SetLiveTrail(previewPlaying || !waitForPlayerCapture || playerCaptured);
        playerBlade?.SetLiveTrail(playerCaptured);
        if (!previewPlaying && waitForPlayerCapture && !playerCaptured) { contactHistoryValid = false; return; }
        if (IsPoseAttack)
        {
            // 준비 때만 부모를 이동합니다. Attack에서는 칼날/손목에 코드를 덧씌우지 않습니다.
            if (state == DuelState.Telegraph)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / telegraphSeconds));
                hand.SetPositionAndRotation(Vector3.Lerp(preparationRootPosition, attackRootPosition, t),
                    Quaternion.Slerp(preparationRootRotation, attackRootRotation, t));
            }
        }
        else
        {
            AlignPatrolBladeUpright();
            if (!previewPlaying && (state == DuelState.Telegraph || state == DuelState.Attack)) ApplyVerticalBladePosition();
            if (!holdingSceneStartPose) KeepHandAboveBoard();
        }
        CheckDefenseContact();
        npcBlade?.RecordMotion(); playerBlade?.RecordMotion();
        if (finishAttackPending && !previewPlaying && !offenseSuspended && state == DuelState.Attack)
        {
            finishAttackPending = false;
            ReturnToPatrol();
        }
    }

    private void CheckDefenseContact()
    {
        if (previewPlaying || state != DuelState.Attack) { contactHistoryValid = false; return; }
        float clipTime = AttackClipTime;
        // 기존 구역 피격은 유지하되 원본 Begin~End 전체에서 실제 칼날 방어를 허용합니다.
        // 중간에 피해를 확정하면 아직 내려오는 검을 막기도 전에 방어가 닫히므로,
        // End를 포함한 마지막 접촉 검사 뒤에만 실패/피 효과/피격 모션을 확정합니다.
        float impactTime = slashEnd;
        if (!attackLogged && clipTime >= slashStart) BeginAttackHit();
        if (!slashEnded && clipTime >= slashEnd) EndAttackHit();
        if (clipTime >= slashLength) finishAttackPending = true;
        if (npcBlade != null && npcBlade.IsValid && playerBlade != null && playerBlade.IsValid)
        {
            var npcPose = npcBlade.CapturePose();
            var playerPose = playerBlade.CapturePose();
            bool defending = playerDefense != null && playerDefense.CanBlock(CurrentAttack);
            float startTime = contactHistoryValid ? previousContactTime : clipTime;
            float duration = clipTime - startTime;
            float from = duration > 0f ? Mathf.Clamp01((slashStart - startTime) / duration) : 1f;
            float to = duration > 0f ? Mathf.Clamp01((impactTime - startTime) / duration) : 1f;
            // 방어 시작 프레임은 현재 자세만 검사합니다. 아직 방어하지 않던 과거로 소급하지 않습니다.
            if (!contactHistoryValid || !previouslyDefending) from = 1f;
            if (!AttackBlocked && !attackResolved && attackLogged && defending &&
                clipTime >= slashStart && startTime <= impactTime &&
                (duration > 0f || clipTime <= impactTime) && from <= to &&
                npcBlade.TrySweepBladeContact(contactHistoryValid ? previousNpcPose : npcPose, npcPose, playerBlade,
                    contactHistoryValid ? previousPlayerPose : playerPose, playerPose, from, to, out var contact))
            {
                AttackBlocked = true;
                SpawnBlockContactEffect(contact);
                Debug.Log($"방어 성공: {AttackStartRegion}, {AttackEndRegion} 구역 방어 성공", this);
                playerDefense.NotifySuccessfulDefense(CurrentAttack);
            }
            previousNpcPose = npcPose; previousPlayerPose = playerPose;
            previousContactTime = clipTime; previouslyDefending = defending; contactHistoryValid = true;
        }
        if (!attackResolved && attackLogged && clipTime >= impactTime)
        {
            attackResolved = true;
            if (!AttackBlocked)
            {
                Debug.Log($"방어 실패: {AttackStartRegion}, {AttackEndRegion} 구역 — 플레이어 피격", this);
                Vector3 contact = knightHitBody != null && npcBlade != null && npcBlade.IsValid
                    ? Physics.ClosestPoint(npcBlade.Midpoint, knightHitBody, knightHitBody.transform.position, knightHitBody.transform.rotation)
                    : areas.KnightWorldCenter;
                SpawnBloodEffect(playerHitBloodPrefab, contact);
                ReceiveNpcHit();
                roundBridge?.NotifyPlayerHit();
            }
        }
        if (endAttackPending || finishAttackPending)
        {
            endAttackPending = false;
            slashEnded = true;
            npcBlade?.SetMotionActive(false);
        }
    }
    private static bool HasLivingParticles(GameObject effect)
    {
        foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            if (particles.IsAlive(false)) return true;
        return false;
    }
    private void SpawnBloodEffect(GameObject prefab, Vector3 contact)
    {
        if (prefab == null) return;
        // 칼/기사의 자식이 아닌 월드 타격 위치에 남겨 재생합니다. 원본 효과 설정은 수정하지 않습니다.
        var effect = Instantiate(prefab, contact, prefab.transform.rotation, transform);
        var systems = effect.GetComponentsInChildren<ParticleSystem>(true);
        if (systems.Length == 0) { Destroy(effect); return; }
        foreach (var particles in systems)
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play(false);
        }
        bloodEffects.Add(effect);
    }
    private void SpawnBlockContactEffect(Vector3 contact)
    {
        if (blockContactEffect != null) Destroy(blockContactEffect);
        blockContactEffect = null;
        blockContactParticles = null;
        if (blockImpactPrefab == null) return;
        // 프리팹 원본의 크기/색/방출 설정을 그대로 사용하고, 실제 3D 접점에 고정합니다.
        blockContactEffect = Instantiate(blockImpactPrefab, contact, Quaternion.identity, transform);
        blockContactParticles = blockContactEffect.GetComponent<ParticleSystem>();
        if (blockContactParticles == null)
        {
            Destroy(blockContactEffect);
            blockContactEffect = null;
            return;
        }
        blockContactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        blockContactParticles.Play(true);
    }

    /// <summary>이동 중 실제 칼날 마커를 기준으로 부모만 회전합니다. 공격 미리보기의 베기/찌르기 각도는 유지합니다.</summary>
    private void AlignPatrolBladeUpright()
    {
        if (patrolPose != null && !holdingSceneStartPose && !previewPlaying &&
            (state == DuelState.Return || state == DuelState.Patrol))
        {
            // 지정한 이동 자세를 자동 방향 보정으로 덮어쓰지 않습니다. 자식 Animator는 그대로 평가됩니다.
            hand.rotation = patrolPose.rotation;
            if (TryGetHandBottom(out float patrolBottom))
                minimumRootY = boardSurfaceY + handBoardClearance - (patrolBottom - hand.position.y);
            return;
        }
        if (patrolPose != null) return;
        if (!keepPatrolBladeUpright || holdingSceneStartPose || previewPlaying || state == DuelState.Attack ||
            npcBlade == null || !npcBlade.IsValid) return;
        Vector3 direction = npcBlade.BladeTip.position - npcBlade.BladeBase.position;
        if (direction.sqrMagnitude < 0.000001f) return;
        hand.rotation = Quaternion.FromToRotation(direction.normalized, Vector3.up) * hand.rotation;
        if (facePlayerDuringPatrol && playerHand != null && playerHand.ControlledHand != null)
        {
            // 칼을 세우는 회전만으로는 앞뒤가 정해지지 않습니다.
            // 플레이어의 준비 정면과 반대가 되도록 수직축으로만 회전해 검의 +Y 방향은 유지합니다.
            Vector3 targetFacing = Vector3.ProjectOnPlane(-(playerHand.RestRotation * Vector3.forward), Vector3.up);
            Vector3 currentFacing = Vector3.ProjectOnPlane(hand.forward, Vector3.up);
            if (targetFacing.sqrMagnitude > 0.0001f && currentFacing.sqrMagnitude > 0.0001f)
                hand.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(currentFacing, targetFacing, Vector3.up), Vector3.up) * hand.rotation;
        }

        if (palmWrist != null && palmIndex != null && palmPinky != null)
        {
            // 왼손: 손목→검지와 손목→새끼의 외적이 손바닥 바깥쪽입니다.
            Vector3 palmNormal = Vector3.Cross(palmIndex.position - palmWrist.position,
                palmPinky.position - palmWrist.position);
            Vector3 tiltDirection = Vector3.ProjectOnPlane(palmNormal, Vector3.up);
            if (tiltDirection.sqrMagnitude > 0.00000001f)
                hand.rotation = Quaternion.AngleAxis(patrolPalmTiltDegrees,
                    Vector3.Cross(Vector3.up, tiltDirection.normalized)) * hand.rotation;
        }

        // 눕힌 손을 세우면 메시 바닥 높이도 달라집니다. 기존 궤적의 높이 제한만 갱신합니다.
        if (TryGetHandBottom(out float bottom))
            minimumRootY = boardSurfaceY + handBoardClearance - (bottom - hand.position.y);
    }
    [ContextMenu("Preview Original Slash (Play Mode)")]
    public void PreviewOriginalSlash() => PreviewAnimation(AttackKind.Slash, slashState);
    [ContextMenu("Preview Original Thrust (Play Mode)")]
    public void PreviewOriginalThrust() => PreviewAnimation(AttackKind.Thrust, thrustState);
    private void PreviewAnimation(AttackKind kind, string name)
    {
        if (!Application.isPlaying || handAnimator == null || !handAnimator.HasState(0, Animator.StringToHash(name))) return;
        if (state == DuelState.Attack || state == DuelState.Telegraph) ReturnToPatrol();
        handAnimator.speed = animatorPlaybackSpeed;
        previewKind = kind; previewElapsed = 0f; previewPlaying = true; PlayState(name);
    }
    /// <summary>원본 클립 이벤트: 베기 접촉 구간/로그를 시작합니다. 실제 접촉은 Animator 평가 후 검사합니다.</summary>
    public void BeginAttackHit()
    {
        if (hand == null || (!previewPlaying && state != DuelState.Attack)) return;
        if (!previewPlaying && !attackLogged)
        {
            attackLogged = true;
            if (logAttacks) Debug.Log($"NPC: {AttackStartRegion}, {AttackEndRegion} 영역을 베는 세로 공격 ({AttackStartRegion}→{AttackEndRegion})", this);
        }
        if (!slashEnded || previewPlaying) npcBlade?.SetMotionActive(true);
    }
    public void EndAttackHit()
    {
        if (!previewPlaying && state == DuelState.Attack && !attackResolved) { endAttackPending = true; return; }
        slashEnded = true; npcBlade?.SetMotionActive(false);
    }
    public void FinishAttack()
    {
        if (previewPlaying) { EndAttackHit(); previewPlaying = false; ReturnToPatrol(); return; }
        if (state != DuelState.Attack) return;
        finishAttackPending = endAttackPending = true;
    }
    private void ReturnToPatrol()
    {
        EndAttackHit();
        PlayState(idleState);
        motionStart = hand.position;
        returnPoint = EvaluatePath(Mathf.PingPong(patrolClock / Mathf.Max(0.1f, pathTravelSeconds), 1f));
        elapsed = 0f; state = DuelState.Return;
    }
    public void FinishBlocked() => FinishAttack();
    private void InitializeKnightReaction(GameObject knight)
    {
        if (knight == null) return;
        foreach (var candidate in knight.GetComponentsInChildren<Animator>(true))
            if (candidate.runtimeAnimatorController != null) { knightAnimator = candidate; break; }
        if (knightAnimator == null) return;
        knightAnimatorWasEnabled = knightAnimator.enabled;
        knightAnimatorSpeed = knightAnimator.speed;
        knightRootMotionWasEnabled = knightAnimator.applyRootMotion;
        var root = knight.transform;
        knightRootPose = new KnightPose { target = root, position = root.localPosition, rotation = root.localRotation, scale = root.localScale };
        knightHitBody = knight.GetComponent<Collider>();
        knightIdleAvailable = knightAnimator.HasState(0, Animator.StringToHash(KnightIdleState));
        if (!knightIdleAvailable) Debug.LogWarning($"기사 Idle State '{KnightIdleState}'를 Animator에서 확인하세요.", this);
        StartKnightIdle();
    }
    private void StartKnightIdle()
    {
        if (!knightIdleAvailable || knightAnimator == null) return;
        knightAnimator.enabled = true;
        knightAnimator.applyRootMotion = false;
        knightAnimator.speed = knightAnimatorSpeed > 0f ? knightAnimatorSpeed : 1f;
        knightAnimator.Play(KnightIdleState, 0, 0f);
        knightAnimator.Update(0f);
        RestoreKnightRoot();
    }
    private void RestoreKnightRoot()
    {
        if (knightRootPose.target == null) return;
        knightRootPose.target.SetLocalPositionAndRotation(knightRootPose.position, knightRootPose.rotation);
        knightRootPose.target.localScale = knightRootPose.scale;
    }
    /// <summary>복제 기사 피격 모션만 재생합니다. 공격당 한 번이며, 원본 미리보기에는 반응하지 않습니다. 추후 실제 피격 판정에서도 호출할 수 있습니다.</summary>
    public void ReceiveNpcHit()
    {
        if (!Application.isPlaying || state == DuelState.Stopped || previewPlaying || AttackBlocked || knightHitPlayed || knightAnimator == null) return;
        // 복제 기사만 활성화하고 원본 Controller의 피격 State를 재생합니다.
        knightAnimator.enabled = true;
        int hash = Animator.StringToHash(knightAttackedState);
        if (!knightAnimator.HasState(0, hash))
        {
            StartKnightIdle();
            Debug.LogWarning($"기사 피격 State '{knightAttackedState}'를 Animator에서 확인하세요.", this);
            knightHitPlayed = true;
            return;
        }
        var targets = knightAnimator.GetComponentsInChildren<Transform>(true);
        knightRestPose = new KnightPose[targets.Length];
        for (int i = 0; i < targets.Length; i++)
            knightRestPose[i] = new KnightPose { target = targets[i], position = targets[i].localPosition,
                rotation = targets[i].localRotation, scale = targets[i].localScale };
        knightHitPlayed = knightHitPlaying = true; knightHitElapsed = 0f;
        knightAnimator.applyRootMotion = false;
        knightAnimator.speed = knightAnimatorSpeed > 0f ? knightAnimatorSpeed : 1f;
        knightAnimator.Play(hash, 0, 0f);
        // 같은 프레임에서 기본 Idle이 먼저 적용되는 것을 막습니다.
        knightAnimator.Update(0f);
        RestoreKnightRoot();
    }
    private void UpdateKnightReaction(bool waiting)
    {
        if (knightAnimator == null) return;
        if (!knightHitPlaying)
        {
            // 원본 클립의 Loop 설정은 변경하지 않고 대련 코드에서만 반복합니다.
            if (knightIdleAvailable)
            {
                var idle = knightAnimator.GetCurrentAnimatorStateInfo(0);
                if (!idle.IsName(KnightIdleState) || (!idle.loop && idle.normalizedTime >= 1f)) StartKnightIdle();
            }
            return;
        }
        knightAnimator.speed = waiting ? 0f : (knightAnimatorSpeed > 0f ? knightAnimatorSpeed : 1f);
        if (waiting) return;
        knightHitElapsed += Time.deltaTime;
        var info = knightAnimator.GetCurrentAnimatorStateInfo(0);
        if ((info.IsName(knightAttackedState) && info.normalizedTime >= 1f) || knightHitElapsed >= knightHitTimeout)
            FinishKnightReaction();
    }
    private void FinishKnightReaction()
    {
        if (!knightHitPlaying) return;
        knightHitPlaying = false;
        if (knightAnimator != null)
        {
            knightAnimator.enabled = false;
            knightAnimator.speed = knightAnimatorSpeed;
            knightAnimator.applyRootMotion = knightRootMotionWasEnabled;
        }
        // 피격 자세를 정리한 뒤 요청한 전투 Idle로 복귀합니다. 생성 루트는 별도로 고정합니다.
        if (knightRestPose != null)
            foreach (var pose in knightRestPose)
                if (pose.target != null)
                {
                    pose.target.SetLocalPositionAndRotation(pose.position, pose.rotation);
                    pose.target.localScale = pose.scale;
                }
        knightRestPose = null;
        StartKnightIdle();
    }
    private void PlayState(string name)
    {
        if (handAnimator != null && handAnimator.HasState(0, Animator.StringToHash(name))) handAnimator.Play(name, 0, 0f);
    }
    private float AttackClipTime => handAnimator == null ? elapsed :
        Mathf.Max(0f, handAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime) * slashLength;

    /// <summary>반시계 번호에서 왼쪽 열은 2→3, 오른쪽 열은 1→4. 다른 공격은 선택하지 않습니다.</summary>
    private void PrepareVerticalAttack()
    {
        if (!areas.HasKnightUiAreas || verticalPattern == null || verticalPattern.Length == 0) return;
        if (npcBlade == null || !npcBlade.IsValid || handAnimator == null || !handAnimator.HasState(0, Animator.StringToHash(slashState)))
        {
            Debug.LogWarning("NPC 세로 베기: BladeBase/BladeTip와 Slash Animator State를 연결하세요.", this);
            enableVerticalAttacks = false; return;
        }
        VerticalColumn column = verticalPattern[patternIndex++ % verticalPattern.Length];
        AttackStartRegion = column == VerticalColumn.Left_2To3 ? 2 : 1;
        AttackEndRegion = column == VerticalColumn.Left_2To3 ? 3 : 4;
        previewKind = AttackKind.Slash;
        attackUsesPose = verticalAttackPose != null;
        if (attackUsesPose)
        {
            // 공격 시작 때 배치를 고정해 자식의 Position/Rotation 곡선이 그대로 보이게 합니다.
            preparationRootPosition = hand.position;
            preparationRootRotation = hand.rotation;
            attackRootPosition = verticalAttackPose.position;
            attackRootRotation = verticalAttackPose.rotation;
        }
        preparationStart = movementCamera.WorldToScreenPoint(npcBlade.Midpoint);
        preparationDepth = movementCamera.WorldToScreenPoint(npcBlade.Midpoint).z;
        preparationDirection = (npcBlade.BladeTip.position - npcBlade.BladeBase.position).normalized;
        elapsed = 0f; state = DuelState.Telegraph;
    }
    private void StartVerticalAttack()
    {
        if (attackUsesPose) hand.SetPositionAndRotation(attackRootPosition, attackRootRotation);
        state = DuelState.Attack; elapsed = 0f; attackLogged = slashEnded = false; knightHitPlayed = false;
        AttackBlocked = endAttackPending = finishAttackPending = attackResolved = contactHistoryValid = previouslyDefending = false;
        PlayState(slashState);
        // 현재 State의 실제 클립을 읽습니다. 이름을 하드코딩하거나 클립을 수정하지 않습니다.
        handAnimator.Update(0f);
        var clips = handAnimator.GetCurrentAnimatorClipInfo(0);
        AnimationClip clip = clips.Length > 0 ? clips[0].clip : null;
        slashLength = clip != null ? Mathf.Max(0.01f, clip.length) : 1f;
        slashStart = Mathf.Clamp(fallbackSlashWindow.x, 0f, 0.99f) * slashLength;
        slashEnd = Mathf.Clamp(fallbackSlashWindow.y, slashStart / slashLength + 0.001f, 1f) * slashLength;
        if (clip != null)
            foreach (AnimationEvent evt in clip.events)
            {
                if (evt.functionName == nameof(BeginAttackHit)) slashStart = evt.time;
                if (evt.functionName == nameof(EndAttackHit)) slashEnd = evt.time;
            }
        slashEnd = Mathf.Max(slashStart + 0.001f, slashEnd);
    }
    /// <summary>Animator 평가 뒤 부모의 검 방향/칼날 중심을 화면 수직면에 배치합니다. 공격 중만 기사 깊이로 접근하며 복귀 시 기존 궤적 Z로 돌아갑니다.</summary>
    private void ApplyVerticalBladePosition()
    {
        if (!areas.HasKnightUiAreas || npcBlade == null || !npcBlade.IsValid) return;
        Vector2 top = areas.GetKnightRegion(AttackStartRegion).center;
        Vector2 bottom = areas.GetKnightRegion(AttackEndRegion).center;
        float prepareT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / telegraphSeconds));
        float attackT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(slashStart, slashEnd, AttackClipTime));
        Vector3 readyDirection = Quaternion.AngleAxis(verticalBladeAngles.x, movementCamera.transform.right) * movementCamera.transform.forward;
        Vector3 direction = state == DuelState.Telegraph ? Vector3.Slerp(preparationDirection, readyDirection, prepareT)
            : Quaternion.AngleAxis(Mathf.Lerp(verticalBladeAngles.x, verticalBladeAngles.y, attackT), movementCamera.transform.right) * movementCamera.transform.forward;
        // 원본 애니메이션의 손가락/손목/롤은 유지하고, 부모만 돌려 베는 평면을 화면 세로 방향에 맞춥니다.
        Vector3 currentDirection = npcBlade.BladeTip.position - npcBlade.BladeBase.position;
        if (currentDirection.sqrMagnitude > 0.000001f)
            hand.rotation = Quaternion.FromToRotation(currentDirection.normalized, direction) * hand.rotation;
        Vector2 screen = state == DuelState.Telegraph
            ? Vector2.Lerp(preparationStart, top, prepareT) : Vector2.Lerp(top, bottom, attackT);
        Vector3 bladeCenter = npcBlade.Midpoint;
        float targetDepth = movementCamera.WorldToScreenPoint(areas.KnightWorldCenter).z + verticalAttackDepthOffset;
        float depth = state == DuelState.Telegraph ? Mathf.Lerp(preparationDepth, targetDepth, prepareT) : targetDepth;
        hand.position += movementCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth)) - bladeCenter;
    }
    public void SetPlayerAttackTrail(bool active)
    {
        if (playerBlade != null && (playerDefense == null || playerDefense.State == PlayerDuelAction.DefenseState.Idle))
            playerBlade.SetMotionActive(active);
    }
    public void ReceivePlayerAttack(AttackKind kind, int startArea, int endArea)
    {
        if (hand == null || state == DuelState.Stopped || previewPlaying || offenseSuspended ||
            (roundBridge != null && (!roundBridge.IsRunning || roundBridge.IsEnding)) ||
            startArea < 0 || startArea > 3 || endArea < 0 || endArea > 3) return;
        if (playerBlade != null && playerBlade.IsValid)
        {
            Vector2 targetScreen = (areas.GetNpcUiArea(startArea).center + areas.GetNpcUiArea(endArea).center) * 0.5f;
            Ray ray = movementCamera.ScreenPointToRay(targetScreen);
            var plane = new Plane(Vector3.forward, playerBlade.Midpoint);
            Vector3 target = plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : playerBlade.Midpoint;
            Vector3 a = playerBlade.BladeBase.position, edge = playerBlade.BladeTip.position - a;
            Vector3 contact = a + edge * (edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(target - a, edge) / edge.sqrMagnitude) : 0f);
            SpawnBloodEffect(npcHitBloodPrefab, contact);
        }
        // 기존 UnityEvent의 Bridge 연결이 있는 경우 중복 집계하지 않습니다.
        bool bridgeConnected = false;
        for (int index = 0; index < onNpcHit.GetPersistentEventCount(); index++)
            if (onNpcHit.GetPersistentTarget(index) == roundBridge &&
                onNpcHit.GetPersistentMethodName(index) == nameof(DuelMiniGameBridge.NotifyNpcHit) &&
                onNpcHit.GetPersistentListenerState(index) != UnityEventCallState.Off)
                bridgeConnected = true;
        if (!bridgeConnected) roundBridge?.NotifyNpcHit();
        onNpcHit.Invoke();
    }
    private void ConfigureBoardLimit(DuelAuthoringReferences references)
    {
        var bridge = GetComponent<DuelMiniGameBridge>();
        boardSurfaceY = bridge != null ? bridge.BoardSurfaceY : float.NegativeInfinity;
        if (float.IsNegativeInfinity(boardSurfaceY) && references.KnightTarget != null)
            foreach (var renderer in references.KnightTarget.GetComponentsInChildren<Renderer>())
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    boardSurfaceY = float.IsNegativeInfinity(boardSurfaceY) ? renderer.bounds.min.y - 0.04f : Mathf.Min(boardSurfaceY, renderer.bounds.min.y - 0.04f);
        var meshes = new System.Collections.Generic.List<Renderer>();
        foreach (var renderer in hand.GetComponentsInChildren<Renderer>())
            if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && (npcBlade == null || !renderer.transform.IsChildOf(npcBlade.transform)))
                meshes.Add(renderer);
        handRenderers = meshes.ToArray();
        float bottomOffset = TryGetHandBottom(out float bottom) ? bottom - hand.position.y : 0f;
        minimumRootY = boardSurfaceY + handBoardClearance - bottomOffset;
    }
    private bool TryGetHandBottom(out float bottom)
    {
        bottom = float.PositiveInfinity;
        if (handRenderers == null) return false;
        foreach (var renderer in handRenderers)
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                bottom = Mathf.Min(bottom, renderer.bounds.min.y);
        return !float.IsPositiveInfinity(bottom);
    }
    private void KeepHandAboveBoard()
    {
        if (float.IsNegativeInfinity(boardSurfaceY) || !TryGetHandBottom(out float bottom)) return;
        float correction = boardSurfaceY + handBoardClearance - bottom;
        if (correction > 0f) hand.position += Vector3.up * correction;
    }
    private Vector3 ConstrainPathHeight(Vector3 point) { point.y = Mathf.Max(point.y, minimumRootY); return point; }
    private Vector3 ScreenToHandPlane(Vector2 screen)
    {
        Ray ray = movementCamera.ScreenPointToRay(screen);
        var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, motionPlaneZ));
        return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : hand != null ? hand.position : transform.position;
    }
    private bool UsesWorldPath => pathPoints != null && pathPoints.Length >= 2;
    private int PathCount => UsesWorldPath ? pathPoints.Length : screenPath != null ? screenPath.Length : 0;
    private Vector3 RawPathPoint(int index)
    {
        if (UsesWorldPath)
            return pathPoints[index] != null ? pathPoints[index].position + Vector3.forward * (patrolPose != null ? 0f : pathBackOffsetZ) : hand != null ? hand.position : transform.position;
        Rect bounds = areas.GetNpcUiArea(0);
        for (int i = 1; i < 4; i++)
        {
            Rect rect = areas.GetNpcUiArea(i);
            bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, rect.xMin), Mathf.Min(bounds.yMin, rect.yMin),
                Mathf.Max(bounds.xMax, rect.xMax), Mathf.Max(bounds.yMax, rect.yMax));
        }
        return ScreenToHandPlane(bounds.center + Vector2.Scale(screenPath[index], bounds.size));
    }
    private Vector3 PathPoint(int index) => ConstrainPathHeight(RawPathPoint(index) + patrolPathOffset);
    private Vector3 LowestRawPathPoint()
    {
        Vector3 lowest = RawPathPoint(0);
        for (int i = 1; i < PathCount; i++)
        {
            Vector3 point = RawPathPoint(i);
            if (point.y < lowest.y) lowest = point;
        }
        return lowest;
    }
    private Vector3 EvaluatePath(float t)
    {
        int count = PathCount;
        if (count < 2) return hand != null ? hand.position : transform.position;
        float position = Mathf.Clamp01(t) * (count - 1);
        int index = Mathf.Min(Mathf.FloorToInt(position), count - 2);
        float u = position - index;
        Vector3 a = PathPoint(Mathf.Max(index - 1, 0)), b = PathPoint(index);
        Vector3 c = PathPoint(index + 1), d = PathPoint(Mathf.Min(index + 2, count - 1));
        return ConstrainPathHeight(0.5f * ((2f * b) + (-a + c) * u + (2f * a - 5f * b + 4f * c - d) * u * u + (-a + 3f * b - 3f * c + d) * u * u * u));
    }
    private void OnDrawGizmosSelected()
    {
        if (movementCamera == null) movementCamera = Camera.main;
        if (areas == null) areas = GetComponent<DrawGizmoAreas>();
        if (areas == null || movementCamera == null) return;
        if (hand == null)
        {
            var references = GetComponent<DuelAuthoringReferences>();
            if (references == null || references.NpcHandSpawnPoint == null) return;
            patrolPose = references.NpcHandPatrolPose;
            motionPlaneZ = patrolPose != null ? patrolPose.position.z : references.NpcHandSpawnPoint.position.z + pathBackOffsetZ;
            var bridge = GetComponent<DuelMiniGameBridge>();
            minimumRootY = bridge != null ? bridge.BoardSurfaceY + handBoardClearance : float.NegativeInfinity;
            areas.RefreshUiAreas();
            patrolPathOffset = UsesWorldPath && patrolPose != null ? patrolPose.position - LowestRawPathPoint() : Vector3.zero;
        }
        if (PathCount < 2) return;
        areas.RefreshUiAreas(); Gizmos.color = Color.red;
        Vector3 previous = EvaluatePath(0f);
        for (int i = 1; i <= 64; i++)
        {
            Vector3 next = EvaluatePath(i / 64f); Gizmos.DrawLine(previous, next); previous = next;
        }
        for (int i = 0; i < PathCount; i++) Gizmos.DrawWireSphere(PathPoint(i), 0.08f);
    }
    private void OnDisable() => StopDuel();
}
