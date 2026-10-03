using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// [역할] 테스트 NPC 손의 기존 대기 궤적 이동과 사용자 애니메이션 미리보기만 담당합니다.
/// [제거] 자동 공격, 목표 영역 이동, 접촉/피해/패링 판정, 공격 경고/튕김 효과.
/// [유지] 손 생성 연결, 뒤쪽 Z 배치, 보드 높이 제한, Catmull-Rom 궤적, 원본 Animator/클립.
/// 이동 중 부모는 검을 위로 세우고 플레이어의 준비 방향과 반대로 마주봅니다. 공격 미리보기에는 보정하지 않습니다.
/// 플레이어 입력/손 조작은 별도 스크립트에서 유지합니다.
/// </summary>
public class NpcDuelStateMachine : MonoBehaviour
{
    // 기존 스크립트/직렬화 호환용 이름과 값입니다. 자동 공격 상태로는 전환하지 않습니다.
    public enum DuelState { Stopped, Patrol, Telegraph, Attack, Return, Blocked }
    public enum AttackKind { Slash, Thrust }
    [Header("References")]
    [SerializeField] private DrawGizmoAreas areas;
    [SerializeField] private DuelMouseHandController playerHand;
    [SerializeField] private PlayerDuelAction playerDefense;
    [SerializeField] private Camera movementCamera;
    [Header("Patrol Path — preserved")]
    [Tooltip("지정하면 월드 포인트에 Path Back Offset Z를 더해 사용합니다. 비우면 아래 NPC 영역 기준 곡선을 사용합니다.")]
    [SerializeField] private Transform[] pathPoints = new Transform[0];
    [Tooltip("NPC 영역 중심 기준 좌표. X/Y=1은 영역 전체 너비/높이입니다.")]
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
    [Header("Your Animator States — preview only")]
    [SerializeField] private string idleState = "Idle";
    [SerializeField] private string slashState = "Slash";
    [SerializeField] private string thrustState = "Thrust";
    [Tooltip("미리보기 클립에 종료 이벤트가 없을 때 Idle로 돌아갈 제한 시간. 공격 판정과 무관합니다.")]
    [SerializeField, Min(0.5f)] private float animationTimeout = 5f;
    [Header("NPC Path Placement (world units)")]
    [Tooltip("NPC 생성 앵커에서 월드 +Z 방향으로 손과 궤적을 함께 옮깁니다. 현재 카메라에서는 양수가 뒤쪽입니다.")]
    [SerializeField] private float pathBackOffsetZ = 1.5f;
    [Tooltip("보드 윗면과 NPC 손 메시 아랫부분 사이 최소 간격. 검 끝은 제한 대상이 아닙니다.")]
    [SerializeField, Min(0f)] private float handBoardClearance = 0.12f;
    [Header("Existing Player Attack Callback — not NPC offense")]
    [Tooltip("플레이어가 기존 NPC 영역에 공격 제스처를 했을 때 호출합니다. NPC의 자동 공격/방어 판정은 없습니다.")]
    [SerializeField] private UnityEvent onNpcHit = new UnityEvent();
    [Header("Runtime State")]
    [SerializeField] private DuelState state = DuelState.Stopped;
    public DuelState State => state;
    /// <summary>미리보기 종류만 나타냅니다. 이 값으로 NPC 공격 판정은 발생하지 않습니다.</summary>
    public AttackKind CurrentAttack => previewKind;

    private Transform hand;
    private Animator handAnimator;
    private DuelBladeHitbox npcBlade, playerBlade;
    private Renderer[] handRenderers;
    private Transform palmWrist, palmIndex, palmPinky;
    private float motionPlaneZ, minimumRootY, boardSurfaceY, elapsed, patrolClock, previewElapsed;
    private Vector3 motionStart, returnPoint;
    private bool previewPlaying;
    private bool holdingSceneStartPose;
    private AttackKind previewKind;

    public void Begin(DuelAuthoringReferences references)
    {
        StopDuel();
        var bridge = GetComponent<DuelMiniGameBridge>();
        if (bridge != null && !bridge.IsTestRunning) return;
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
        motionPlaneZ = (references.NpcHandSpawnPoint != null ? references.NpcHandSpawnPoint.position.z : hand.position.z) + pathBackOffsetZ;
        // 씬에서 지정한 시작 자세는 손을 잡기 전까지 유지합니다. Z 보정은 이후 궤적에 그대로 사용합니다.
        holdingSceneStartPose = references.HasNpcStartPose && waitForPlayerCapture;
        if (!holdingSceneStartPose)
        {
            Vector3 position = hand.position; position.z = motionPlaneZ; hand.position = position;
        }
        handAnimator = hand.GetComponentInChildren<Animator>();
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
        npcBlade?.ResetHistory(); playerBlade?.ResetHistory();
        areas.RefreshUiAreas();
        motionStart = hand.position; returnPoint = EvaluatePath(0f);
        elapsed = patrolClock = 0f; state = DuelState.Return;
    }
    public void StopDuel()
    {
        npcBlade?.SetMotionActive(false); playerBlade?.SetMotionActive(false);
        npcBlade?.ResetHistory(); playerBlade?.ResetHistory();
        hand = null; handAnimator = null; npcBlade = playerBlade = null; handRenderers = null;
        palmWrist = palmIndex = palmPinky = null;
        minimumRootY = boardSurfaceY = float.NegativeInfinity;
        previewPlaying = false; holdingSceneStartPose = false; state = DuelState.Stopped;
    }
    private void Update()
    {
        if (hand == null || state == DuelState.Stopped) return;
        if (previewPlaying)
        {
            previewElapsed += Time.deltaTime;
            if (previewElapsed >= animationTimeout) FinishAttack();
        }
        if (waitForPlayerCapture && (playerHand == null || !playerHand.IsCaptured)) return;
        holdingSceneStartPose = false;
        areas.RefreshUiAreas(); elapsed += Time.deltaTime;
        if (state == DuelState.Return)
        {
            hand.position = Vector3.Lerp(motionStart, returnPoint, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / returnSeconds)));
            if (elapsed >= returnSeconds) { state = DuelState.Patrol; elapsed = 0f; }
        }
        else
        {
            patrolClock += Time.deltaTime;
            hand.position = EvaluatePath(Mathf.PingPong(patrolClock / Mathf.Max(0.1f, pathTravelSeconds), 1f));
        }
        // Animator 평가가 끝난 LateUpdate에서 이동용 부모에만 검 방향을 보정합니다.
    }
    private void LateUpdate()
    {
        if (hand == null) return;
        // 플레이어의 기존 마우스 방어 자세/잔상 기능은 유지하되 NPC 접촉 판정은 하지 않습니다.
        playerDefense?.ApplyGuardPose(movementCamera); playerHand?.ApplyRecoil();
        AlignPatrolBladeUpright();
        if (!holdingSceneStartPose) KeepHandAboveBoard();
        npcBlade?.RecordMotion(); playerBlade?.RecordMotion();
    }

    /// <summary>이동 중 실제 칼날 마커를 기준으로 부모만 회전합니다. 공격 미리보기의 베기/찌르기 각도는 유지합니다.</summary>
    private void AlignPatrolBladeUpright()
    {
        if (!keepPatrolBladeUpright || holdingSceneStartPose || previewPlaying ||
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
        previewKind = kind; previewElapsed = 0f; previewPlaying = true; PlayState(name);
    }
    // 원본 클립의 이벤트 수신 함수는 유지합니다. 판정 없이 잔상/미리보기 종료만 처리합니다.
    public void BeginAttackHit() { if (hand != null) npcBlade?.SetMotionActive(true); }
    public void EndAttackHit() => npcBlade?.SetMotionActive(false);
    public void FinishAttack()
    {
        EndAttackHit();
        if (!previewPlaying) return;
        previewPlaying = false; PlayState(idleState);
    }
    public void FinishBlocked() => FinishAttack();
    private void PlayState(string name)
    {
        if (handAnimator != null && handAnimator.HasState(0, Animator.StringToHash(name))) handAnimator.Play(name, 0, 0f);
    }
    public void SetPlayerAttackTrail(bool active)
    {
        if (playerBlade != null && (playerDefense == null || playerDefense.State == PlayerDuelAction.DefenseState.Idle))
            playerBlade.SetMotionActive(active);
    }
    public void ReceivePlayerAttack(AttackKind kind, int startArea, int endArea)
    {
        if (hand == null || state == DuelState.Stopped || startArea < 0 || startArea > 3 || endArea < 0 || endArea > 3) return;
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
    private int PathCount => pathPoints != null && pathPoints.Length >= 2 ? pathPoints.Length : screenPath != null ? screenPath.Length : 0;
    private Vector3 PathPoint(int index)
    {
        if (pathPoints != null && pathPoints.Length >= 2)
            return ConstrainPathHeight(pathPoints[index] != null ? pathPoints[index].position + Vector3.forward * pathBackOffsetZ : hand != null ? hand.position : transform.position);
        Rect bounds = areas.GetNpcUiArea(0);
        for (int i = 1; i < 4; i++)
        {
            Rect rect = areas.GetNpcUiArea(i);
            bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, rect.xMin), Mathf.Min(bounds.yMin, rect.yMin),
                Mathf.Max(bounds.xMax, rect.xMax), Mathf.Max(bounds.yMax, rect.yMax));
        }
        return ConstrainPathHeight(ScreenToHandPlane(bounds.center + Vector2.Scale(screenPath[index], bounds.size)));
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
            motionPlaneZ = references.NpcHandSpawnPoint.position.z + pathBackOffsetZ;
            var bridge = GetComponent<DuelMiniGameBridge>();
            minimumRootY = bridge != null ? bridge.BoardSurfaceY + handBoardClearance : float.NegativeInfinity;
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
