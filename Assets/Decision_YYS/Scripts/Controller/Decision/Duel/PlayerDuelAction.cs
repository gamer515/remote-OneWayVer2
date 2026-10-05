using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// [역할] 마우스 클릭/드래그를 공격·방어 입력으로 분류하는 스크립트입니다.
/// [흐름] 손 잡기 확인 → 왼버튼 클릭/드래그는 공격, 오른버튼 유지는 영역과 무관하게 방어.
/// 칼날 충돌 계산은 여기서 하지 않습니다. NPC 영역 드래그는 기존 화면 기반 공격입니다.
/// [수정] 클릭/슬래시 거리와 쿨다운은 Mouse Gesture, 방어 자세/클립도 이 컴포넌트에서 수정합니다.
/// </summary>
[DefaultExecutionOrder(100)]
public class PlayerDuelAction : MonoBehaviour
{
    [SerializeField] private DrawGizmoAreas gizmoAreas;
    [SerializeField] private DuelMiniGameBridge duelBridge;
    [SerializeField] private DuelMouseHandController mouseHandController;
    [SerializeField] private NpcDuelStateMachine npcStateMachine;
    [Header("Mouse Gesture (screen pixels)")]
    [SerializeField, Min(1f)] private float minimumSlashDistance = 45f;
    [SerializeField, Min(0f)] private float clickTolerance = 15f;
    [SerializeField, Min(0.05f)] private float maximumClickSeconds = 0.5f;
    [SerializeField, Min(0f)] private float attackCooldown = 0.25f;
    [Tooltip("왼버튼 드래그 베기. 영역 밖이면 시작/끝 인덱스는 -1입니다. NPC 피해는 별도로 ReceivePlayerAttack에서 제한합니다.")]
    [SerializeField] private UnityEngine.Events.UnityEvent<int, int> onSlash = new UnityEngine.Events.UnityEvent<int, int>();
    [SerializeField] private UnityEngine.Events.UnityEvent<int> onThrust = new UnityEngine.Events.UnityEvent<int>();
    [Tooltip("위치와 무관한 오른버튼 방어 입력입니다. 전달 인덱스는 항상 -1입니다.")]
    [SerializeField] private UnityEngine.Events.UnityEvent<int> onGuardGesture = new UnityEngine.Events.UnityEvent<int>();
    private bool tracking;
    private int startArea;
    private Vector2 startPosition;
    private float startedAt, largestDistance, nextAttackTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (npcStateMachine == null) npcStateMachine = GetComponent<NpcDuelStateMachine>();
        if (gizmoAreas == null)
            gizmoAreas = GetComponent<DrawGizmoAreas>();
        if (duelBridge == null)
            duelBridge = gizmoAreas != null
                ? gizmoAreas.GetComponent<DuelMiniGameBridge>()
                : GetComponent<DuelMiniGameBridge>();
        if (mouseHandController == null)
            mouseHandController = gizmoAreas != null
                ? gizmoAreas.GetComponent<DuelMouseHandController>()
                : GetComponent<DuelMouseHandController>();
    }

    private void Update()
    {
        UpdateAttackMotion();
        if (duelBridge != null && duelBridge.IsEnding)
        {
            tracking = false;
            ReleaseDefense();
            return;
        }
        AdvanceDefenseFatigue(Time.deltaTime);
        UpdateDefense();
        // 실행 중 스크립트 재컴파일 후에도 비어 있는 참조를 복구합니다.
        ResolveReferences();
        if (gizmoAreas == null || !gizmoAreas.isActiveAndEnabled ||
            duelBridge == null || !duelBridge.IsRunning ||
            mouseHandController == null || !mouseHandController.isActiveAndEnabled || !mouseHandController.IsCaptured)
        {
            tracking = false;
            npcStateMachine?.SetPlayerAttackTrail(false);
            ReleaseDefense();
            return;
        }
        // 손을 잡은 첫 클릭은 공격이나 방어로 처리하지 않습니다.
        if (mouseHandController.CaptureFrame == Time.frameCount) return;

        #if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) { tracking = false; ReleaseDefense(); return; }
        bool pressed = Mouse.current.leftButton.wasPressedThisFrame;
        bool released = Mouse.current.leftButton.wasReleasedThisFrame;
        bool defensePressed = Mouse.current.rightButton.wasPressedThisFrame;
        bool defenseReleased = Mouse.current.rightButton.wasReleasedThisFrame;
        bool defenseHeld = Mouse.current.rightButton.isPressed;
        #else
        bool pressed = Input.GetMouseButtonDown(0);
        bool released = Input.GetMouseButtonUp(0);
        bool defensePressed = Input.GetMouseButtonDown(1);
        bool defenseReleased = Input.GetMouseButtonUp(1);
        bool defenseHeld = Input.GetMouseButton(1);
        #endif

        // 커서는 중앙에 잠겨 있으므로 손 이동에 쓰는 가상 화면 좌표로 판정합니다.
        Vector2 mousePosition = mouseHandController.PointerScreenPosition;

        // 클릭 시점의 좌표로 판정하도록 화면 영역을 갱신합니다.
        gizmoAreas.RefreshUiAreas();

        ProcessMouseInput(pressed, released, defensePressed, defenseReleased, defenseHeld, mousePosition);
    }

    // 입력을 분리해 실제 버튼 입력과 테스트가 같은 경로를 사용하도록 합니다.
    private void ProcessMouseInput(bool pressed, bool released, bool defensePressed, bool defenseReleased, bool defenseHeld, Vector2 mousePosition)
    {
        if (defenseReleased || !defenseHeld) ReleaseDefense();
        if (defensePressed)
        {
            bool incoming = npcStateMachine != null && (npcStateMachine.State == NpcDuelStateMachine.DuelState.Telegraph ||
                npcStateMachine.State == NpcDuelStateMachine.DuelState.Attack);
            PressDefense(incoming ? npcStateMachine.CurrentAttack : NpcDuelStateMachine.AttackKind.Slash);
            onGuardGesture.Invoke(-1); // -1은 영역 지정 없는 오른클릭 방어입니다.
        }
        // 양 버튼을 동시에 누르면 방어를 우선합니다. 왼버튼을 놓아도 오른버튼 방어는 유지됩니다.
        if (defenseHeld && guardHeld)
        {
            tracking = false;
            npcStateMachine?.SetPlayerAttackTrail(false);
            return;
        }
        // 재생 중에는 중복 베기 입력을 받지 않습니다. 오른클릭 방어는 위에서 공격을 중단할 수 있습니다.
        if (IsAttacking) return;
        if (pressed)
        {
            if (Time.time < nextAttackTime) { tracking = false; return; }
            startArea = FindArea(mousePosition);
            tracking = true;
            npcStateMachine?.SetPlayerAttackTrail(tracking);
            startPosition = mousePosition;
            startedAt = Time.time;
            largestDistance = 0f;
        }
        if (!tracking) return;
        largestDistance = Mathf.Max(largestDistance, Vector2.Distance(startPosition, mousePosition));
        int endArea = FindArea(mousePosition);
        // 명시적인 세로 구역 입력은 이동 거리와 무관하게 베기입니다.
        // 경계 가까이 2→3/1→4를 입력해도 짧은 클릭(찌르기)으로 분류하지 않습니다.
        // NPC 배열은 우상/좌상/우하/좌하 순서이므로 사용자 번호 2→3=1→3, 1→4=0→2입니다.
        bool verticalSlash = (startArea == 1 && endArea == 3) || (startArea == 0 && endArea == 2);
        // 세로 베기는 버튼을 놓기 전에 다음 구역에 진입하는 순간 시작합니다.
        // tracking을 소비하므로 계속 누르거나 나중에 놓아도 같은 드래그를 다시 공격으로 집계하지 않습니다.
        if (!verticalSlash && !released) return;
        tracking = false;
        npcStateMachine?.SetPlayerAttackTrail(false);
        bool slash = verticalSlash || largestDistance >= minimumSlashDistance;
        bool thrust = largestDistance <= clickTolerance &&
            Time.time - startedAt <= maximumClickSeconds;
        if (!slash && !thrust) return;
        nextAttackTime = Time.time + attackCooldown;
        // 영역 밖도 사용자 공격 모션 이벤트는 호출합니다(-1 인덱스).
        // NPC 피해 결과는 ReceivePlayerAttack에서 기존 유효 영역 제스처로 제한합니다.
        if (verticalSlash)
        {
            PlaySlashAttack();
            if (IsAttacking)
                Debug.Log($"[Duel] 플레이어 세로 베기 {(startArea == 1 ? "2→3" : "1→4")} — {SlashAttackState} 재생", this);
        }
        if (slash) onSlash.Invoke(startArea, endArea); else onThrust.Invoke(endArea);
        if (verticalSlash && IsAttacking)
        {
            pendingSlashHit = true;
            slashHitStartArea = startArea;
            slashHitEndArea = endArea;
        }
        else npcStateMachine?.ReceivePlayerAttack(slash ? NpcDuelStateMachine.AttackKind.Slash :
            NpcDuelStateMachine.AttackKind.Thrust, startArea, endArea);
    }

    private int FindArea(Vector2 position)
    {
        for (int i = 0; i < 4; i++)
            if (gizmoAreas.GetNpcUiArea(i).Contains(position)) return i;
        return -1;
    }

    private void OnDisable() { tracking = false; npcStateMachine?.SetPlayerAttackTrail(false); StopDefense(); }

    public enum DefenseState { Idle, Guard, Parry }
    [Header("Animator State Names")]
    [SerializeField] private string idleState = "Duel_Idle";
    [SerializeField] private string guardHoldState = "GuardHold";
    [SerializeField] private string parryState = "ParryThrust";
    [Header("Your Animation Clips")]
    [Tooltip("네가 만든 수평 가드 클립을 넣으세요. GuardHold 상태의 슬롯을 런타임에 교체합니다.")]
    [SerializeField] private AnimationClip guardHoldClip;
    [Tooltip("네가 만든 쳐내기 클립을 넣고 컴포넌트 메뉴의 Connect Defense Clips And Events를 실행하세요.")]
    [SerializeField] private AnimationClip parryThrustClip;
    [Header("Timing (seconds)")]
    [SerializeField, Min(0f)] private float guardBlendSeconds = 0.1f;
    [Tooltip("기본은 BeginParry~EndParry 이벤트 구간 전체를 사용합니다. 켜면 아래 시간으로 추가 제한합니다.")]
    [SerializeField] private bool limitParryWindow;
    [SerializeField, Min(0.01f)] private float maximumParryWindow = 0.2f;
    [SerializeField, Min(0f)] private float parryCooldown = 0.3f;
    [SerializeField, Min(0.5f)] private float animationTimeout = 5f;
    [Header("Screen Guard Pose — fallback without a clip")]
    [Tooltip("가드 클립이 없을 때만 사용하는 화면 각도 보정입니다. 가드 클립이 있으면 부모 추가 회전 없이 원본 모션을 사용합니다.")]
    [SerializeField] private bool alignGuardBlade = true;
    [Tooltip("화면 기준 가드 칼날 각도. 0=수평, 90=수직. 좌우 중 현재 자세에서 가까운 쪽으로 정렬합니다.")]
    [SerializeField] private float guardBladeAngle;
    [Header("Feedback")]
    [SerializeField] private UnityEngine.Events.UnityEvent onGuardSuccess = new UnityEngine.Events.UnityEvent();
    [SerializeField] private UnityEngine.Events.UnityEvent onParrySuccess = new UnityEngine.Events.UnityEvent();
    [Header("Runtime State")]
    [SerializeField] private DefenseState state;
    /// <summary>현재 플레이어 방어 상태입니다. 입력과 애니메이션 이벤트에 따라 변경됩니다.</summary>
    public DefenseState State => state;
    /// <summary>패링 상태이며 BeginParry~EndParry 이벤트와 시간 제한을 만족하는지 여부입니다. 칼날 접촉은 별도로 검사합니다.</summary>
    public bool IsParryWindowOpen => state == DefenseState.Parry && parryWindow && Time.time <= parryWindowEnds;
    /// <summary>오른버튼으로 방어를 유지하는지 여부입니다. 화면 영역이나 공격 종류 조건은 없습니다.</summary>
    public bool IsDefenseHeld => guardHeld;

    private Animator animator;
    private DuelBladeHitbox blade;
    private RuntimeAnimatorController originalController;
    private AnimatorOverrideController runtimeController;
    private DuelMouseHandController mouseHand;
    private bool guardHeld, parryWindow, consumed, warnedGuard;
    private float pressedAt, parryWindowEnds, nextParryAt;
    private Vector3 modelRestLocalPosition;
    private bool usingGuardMotion, guardPositionOverride;
    private const float MaximumDefenseFatigue = 100f;
    private const float FatiguePerBlockedAttack = 45f;
    private const float GuardRecoveryPerSecond = 3f;
    private const float ReleasedRecoveryPerSecond = 20f;
    private const float DefenseBreakSeconds = 2f;
    public float DefenseFatigue { get; private set; }
    public bool IsDefenseBroken => defenseBreakRemaining > 0f;
    private float defenseBreakRemaining;
    private bool needsDefenseRelease;
    private const string SlashAttackState = "Slach_Attack";
    private float attackStartedAt;
    private bool warnedSlashAttack;
    private bool attackPositionOverride;
    private Vector3 attackModelLocalPosition;
    private bool pendingSlashHit;
    private int slashHitStartArea, slashHitEndArea;
    public bool IsAttacking { get; private set; }

    private void PlaySlashAttack()
    {
        if (animator == null || !animator.HasState(0, Animator.StringToHash(SlashAttackState)))
        {
            if (!warnedSlashAttack) Debug.LogWarning("플레이어 Slach_Attack Animator 상태가 없습니다.", this);
            warnedSlashAttack = true;
            return;
        }
        // 클립 파일은 그대로 둡니다. 위치는 마우스로 움직이는 부모가 담당하고,
        // 자식은 재생 직전 위치를 유지하며 원본 회전/손가락 모션만 표시합니다.
        RestoreModelPosition();
        attackModelLocalPosition = animator.transform.localPosition;
        attackPositionOverride = true;
        guardPositionOverride = usingGuardMotion = false;
        IsAttacking = true;
        attackStartedAt = Time.time;
        animator.Play(SlashAttackState, 0, 0f);
        blade?.SetMotionActive(true);
        npcStateMachine?.SetPlayerAttackTrail(true);
    }

    private void UpdateAttackMotion()
    {
        if (!IsAttacking) return;
        if (animator == null || mouseHand == null || !mouseHand.IsCaptured)
        {
            ReturnIdle();
            return;
        }
        var info = animator.GetCurrentAnimatorStateInfo(0);
        if ((info.IsName(SlashAttackState) && info.normalizedTime >= 1f) ||
            Time.time - attackStartedAt >= animationTimeout)
        {
            // 한 프레임에 중간 타격 단계와 종료를 함께 지난 경우에도 타격을 누락하지 않습니다.
            RestoreModelPosition();
            ResolveSlashHit();
            ReturnIdle();
        }
    }

    private void ResolveSlashHit()
    {
        if (!pendingSlashHit || !IsAttacking || animator == null || mouseHand == null || !mouseHand.IsCaptured) return;
        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName(SlashAttackState) || info.normalizedTime < 0.5f) return;
        pendingSlashHit = false;
        npcStateMachine?.ReceivePlayerAttack(NpcDuelStateMachine.AttackKind.Slash, slashHitStartArea, slashHitEndArea);
    }

    private void AdvanceDefenseFatigue(float deltaTime)
    {
        float dt = Mathf.Max(0f, deltaTime);
        // 자세 유지에는 비용이 없습니다. 가드 중에도 회복하며, 풀면 더 빨리 회복합니다.
        DefenseFatigue = Mathf.Max(0f, DefenseFatigue -
            (guardHeld ? GuardRecoveryPerSecond : ReleasedRecoveryPerSecond) * dt);
        bool wasBroken = IsDefenseBroken;
        defenseBreakRemaining = Mathf.Max(0f, defenseBreakRemaining - dt);
        if (wasBroken && !IsDefenseBroken)
        {
            mouseHand?.SetMovementSpeedMultiplier(1f);
            Debug.Log("[Duel] 방어 회복 — 오른클릭을 놓았다가 다시 누르면 방어 가능", this);
        }
    }

    private void AddDefenseFatigue()
    {
        DefenseFatigue = Mathf.Min(MaximumDefenseFatigue, DefenseFatigue + FatiguePerBlockedAttack);
        Debug.Log($"[Duel] 방어 피로도 {DefenseFatigue:0.#}/{MaximumDefenseFatigue:0}", this);
        if (DefenseFatigue < MaximumDefenseFatigue) return;
        defenseBreakRemaining = DefenseBreakSeconds;
        needsDefenseRelease = true;
        ReturnIdle();
        mouseHand?.SetMovementSpeedMultiplier(0.5f);
        Debug.Log("[Duel] 방어 한도 초과 — 2초간 방어 불가, 손 이동 속도 50%", this);
    }

    public void BeginDefense(DuelAuthoringReferences references)
    {
        StopDefense();
        ResolveReferences();
        mouseHand = mouseHandController;
        if (references == null || references.SpawnedPlayerHand == null) return;
        // 위치 제어용 HandRoot가 아니라 Animator가 실제 달린 자식을 사용합니다.
        foreach (var candidate in references.SpawnedPlayerHand.GetComponentsInChildren<Animator>(true))
            if (candidate.runtimeAnimatorController != null) { animator = candidate; break; }
        if (animator != null)
        {
            modelRestLocalPosition = animator.transform.localPosition;
            originalController = animator.runtimeAnimatorController;
            runtimeController = new AnimatorOverrideController(originalController);
            var overrides = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
            runtimeController.GetOverrides(overrides);
            for (int i = 0; i < overrides.Count; i++)
            {
                var pair = overrides[i];
                if (pair.Key.name == "PlayerGuardSlot" && guardHoldClip != null)
                    overrides[i] = new System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>(pair.Key, guardHoldClip);
                else if (pair.Key.name == "PlayerParrySlot" && parryThrustClip != null)
                    overrides[i] = new System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>(pair.Key, parryThrustClip);
            }
            runtimeController.ApplyOverrides(overrides);
            animator.runtimeAnimatorController = runtimeController;
        }
        foreach (var receiver in references.SpawnedPlayerHand.GetComponentsInChildren<DuelAnimationEvents>(true))
            receiver.Initialize(this);
        blade = references.SpawnedPlayerHand.GetComponentInChildren<DuelBladeHitbox>();
        nextParryAt = 0f;
        warnedGuard = false;
        warnedSlashAttack = false;
    }

    /// <summary>위치와 무관한 새 오른클릭. 사용 가능한 쳐내기 클립이 있으면 찌르기에 재생하고, 그 외에는 유지 가드입니다.</summary>
    public void PressDefense(NpcDuelStateMachine.AttackKind incoming)
    {
        if (IsDefenseBroken || needsDefenseRelease) return;
        if (IsAttacking) ReturnIdle();
        guardHeld = true;
        pressedAt = Time.time;
        consumed = false;
        if (incoming == NpcDuelStateMachine.AttackKind.Thrust && state != DefenseState.Parry &&
            Time.time >= nextParryAt && HasMotion(parryState))
        {
            if (guardPositionOverride || attackPositionOverride) animator.transform.localPosition = modelRestLocalPosition;
            attackPositionOverride = false;
            guardPositionOverride = usingGuardMotion = false;
            parryWindow = false;
            state = DefenseState.Parry;
            pressedAt = Time.time;
            nextParryAt = Time.time + parryCooldown;
            animator.Play(parryState, 0, 0f);
            blade?.SetMotionActive(true);
            return;
        }
        StartGuardPose();
    }
    private void StartGuardPose()
    {
        attackPositionOverride = false;
        parryWindow = false;
        state = DefenseState.Guard;
        usingGuardMotion = HasMotion(guardHoldState);
        guardPositionOverride = usingGuardMotion;
        if (!usingGuardMotion)
        {
            if (!warnedGuard) Debug.LogWarning("가드 클립이 없어 임시 자세만 표시합니다. 방어 모션이 없으므로 방어 성공은 판정하지 않습니다.", this);
            warnedGuard = true;
        }
        else animator.CrossFadeInFixedTime(guardHoldState, guardBlendSeconds, 0, 0f);
        blade?.SetMotionActive(true);
    }

    public void ReleaseDefense()
    {
        needsDefenseRelease = false;
        guardHeld = false;
        // 쳐내기는 클릭을 놓아도 클립 끝까지 진행합니다.
        if (state == DefenseState.Guard) ReturnIdle();
    }

    public bool CanBlock(NpcDuelStateMachine.AttackKind kind)
    {
        if (IsDefenseBroken) return false;
        if (mouseHand == null || !mouseHand.IsCaptured || !guardHeld || animator == null || !animator.isActiveAndEnabled) return false;
        if (state == DefenseState.Guard) return usingGuardMotion && IsPlayingDefenseMotion(guardHoldState);
        return IsParryWindowOpen && IsPlayingDefenseMotion(parryState);
    }
    private bool IsPlayingDefenseMotion(string name)
    {
        if (animator.GetCurrentAnimatorStateInfo(0).IsName(name))
            foreach (var clip in animator.GetCurrentAnimatorClipInfo(0))
                if (clip.clip != null && clip.clip.length > 0.01f && clip.weight > 0.001f) return true;
        if (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(name))
            foreach (var clip in animator.GetNextAnimatorClipInfo(0))
                if (clip.clip != null && clip.clip.length > 0.01f && clip.weight > 0.001f) return true;
        return false;
    }

    public void NotifySuccessfulDefense(NpcDuelStateMachine.AttackKind kind)
    {
        if (IsDefenseBroken || !guardHeld) return;
        if (!consumed && IsParryWindowOpen)
        {
            consumed = true;
            parryWindow = false;
            onParrySuccess.Invoke();
        }
        else onGuardSuccess.Invoke();
        AddDefenseFatigue();
    }

    // Animator가 있는 자식의 DuelAnimationEvents가 전달합니다.
    public void BeginParry()
    {
        if (state != DefenseState.Parry || consumed) return;
        parryWindow = true;
        parryWindowEnds = limitParryWindow ? Time.time + maximumParryWindow : float.PositiveInfinity;
    }
    public void EndParry() => parryWindow = false;
    public void FinishParry()
    {
        if (state != DefenseState.Parry) return;
        if (guardHeld) StartGuardPose(); else ReturnIdle();
    }
    private void LateUpdate()
    {
        RestoreModelPosition();
        ResolveSlashHit();
    }
    private void RestoreModelPosition()
    {
        if (animator == null || (!guardPositionOverride && !attackPositionOverride)) return;
        // Animator 평가 뒤, 칼날 접촉/궤적 샘플링 전에도 같은 보정을 적용합니다.
        // 원본 Position 키는 수정하지 않으며 Idle 복귀 블렌드까지 위치 밀림을 막습니다.
        animator.transform.localPosition = attackPositionOverride ? attackModelLocalPosition : modelRestLocalPosition;
        if (!IsAttacking && state == DefenseState.Idle && !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).IsName(idleState))
            guardPositionOverride = attackPositionOverride = false;
    }
    /// <summary>가드 클립이 있으면 위치 키 밀림만 방지하고 회전은 원본을 유지합니다. 클립이 없을 때만 부모 수평 보정을 사용합니다.</summary>
    public void ApplyGuardPose(Camera camera)
    {
        RestoreModelPosition();
        if (state == DefenseState.Guard && guardHeld && usingGuardMotion)
        {
            if (mouseHand != null && mouseHand.ControlledHand != null)
                mouseHand.ControlledHand.rotation = mouseHand.RestRotation;
            return;
        }
        if (!alignGuardBlade || state != DefenseState.Guard || !guardHeld || camera == null ||
            mouseHand == null || mouseHand.ControlledHand == null || blade == null) return;
        var root = mouseHand.ControlledHand;
        root.rotation = mouseHand.RestRotation;
        float blend = guardBlendSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - pressedAt) / guardBlendSeconds);
        Vector2 desired = new Vector2(Mathf.Cos(guardBladeAngle * Mathf.Deg2Rad), Mathf.Sin(guardBladeAngle * Mathf.Deg2Rad));
        for (int i = 0; i < 2; i++)
        {
            if (!blade.TryGetScreenSegment(camera, out var a, out var b)) break;
            Vector2 direction = b - a;
            if (Vector2.Dot(direction, desired) < 0f) desired = -desired;
            Vector3 midpoint = blade.Midpoint;
            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, midpoint.z));
            Vector2 center = camera.WorldToScreenPoint(midpoint);
            Ray rayA = camera.ScreenPointToRay(center-desired*50f), rayB = camera.ScreenPointToRay(center+desired*50f);
            if (!plane.Raycast(rayA, out float distanceA) || !plane.Raycast(rayB, out float distanceB)) break;
            Quaternion correction = Quaternion.FromToRotation(blade.BladeTip.position-blade.BladeBase.position,
                rayB.GetPoint(distanceB)-rayA.GetPoint(distanceA));
            root.rotation = Quaternion.Slerp(Quaternion.identity, correction, blend) * root.rotation;
        }
    }
    public void StopDefense()
    {
        tracking = false;
        nextAttackTime = 0f;
        DefenseFatigue = defenseBreakRemaining = 0f;
        needsDefenseRelease = false;
        mouseHand?.SetMovementSpeedMultiplier(1f);
        ReturnIdle();
        if ((guardPositionOverride || attackPositionOverride) && animator != null) animator.transform.localPosition = modelRestLocalPosition;
        attackPositionOverride = false;
        guardPositionOverride = usingGuardMotion = false;
        if (animator != null && originalController != null) animator.runtimeAnimatorController = originalController;
        if (runtimeController != null) Destroy(runtimeController);
        runtimeController = null;
        originalController = null;
        animator = null;
        mouseHand = null;
        blade = null;
        nextParryAt = 0f;
    }

    private void UpdateDefense()
    {
        if (state == DefenseState.Idle) return;
        if (mouseHand == null || !mouseHand.IsCaptured) { ReturnIdle(); return; }
        if (state == DefenseState.Parry && Time.time - pressedAt >= animationTimeout)
        {
            Debug.LogWarning("FinishParry 이벤트가 없어 기본 자세로 복귀했습니다.", this);
            if (guardHeld) StartGuardPose(); else ReturnIdle();
        }
    }
    private bool HasMotion(string name)
    {
        if (animator == null || animator.runtimeAnimatorController == null || !animator.HasState(0, Animator.StringToHash(name))) return false;
        AnimationClip assigned = name == guardHoldState ? guardHoldClip : parryThrustClip;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
            if (assigned != null ? clip == assigned :
                clip.name == (name == guardHoldState ? "PlayerGuardSlot" : "PlayerParrySlot") && clip.length > 0.01f) return true;
        return false;
    }
    #if UNITY_EDITOR
    [ContextMenu("Connect Defense Clips And Events")]
    private void ConnectDefenseClipsAndEvents()
    {
        if (Application.isPlaying) { Debug.LogWarning("Play 모드를 종료한 뒤 연결하세요.", this); return; }
        if (guardHoldClip != null)
        {
            UnityEditor.Undo.RecordObject(guardHoldClip, "Configure guard clip");
            var settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(guardHoldClip);
            settings.loopTime = true;
            UnityEditor.AnimationUtility.SetAnimationClipSettings(guardHoldClip, settings);
            UnityEditor.EditorUtility.SetDirty(guardHoldClip);
        }
        if (parryThrustClip != null)
        {
            UnityEditor.Undo.RecordObject(parryThrustClip, "Connect parry events");
            var settings = UnityEditor.AnimationUtility.GetAnimationClipSettings(parryThrustClip);
            settings.loopTime = false;
            UnityEditor.AnimationUtility.SetAnimationClipSettings(parryThrustClip, settings);
            var events = new System.Collections.Generic.List<AnimationEvent>(parryThrustClip.events);
            float length = Mathf.Max(0.1f, parryThrustClip.length);
            AddEventIfMissing(events, "BeginParry", length * 0.35f);
            AddEventIfMissing(events, "EndParry", length * 0.55f);
            AddEventIfMissing(events, "FinishParry", length - 0.02f);
            events.Sort((a, b) => a.time.CompareTo(b.time));
            UnityEditor.AnimationUtility.SetAnimationEvents(parryThrustClip, events.ToArray());
            UnityEditor.EditorUtility.SetDirty(parryThrustClip);
        }
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log("방어 클립 설정·이벤트 연결 완료. 실제 쳐내기 순간에 맞춰 이벤트 시간을 조절하세요.", this);
    }
    private static void AddEventIfMissing(System.Collections.Generic.List<AnimationEvent> events, string name, float time)
    {
        foreach (var entry in events) if (entry.functionName == name) return;
        events.Add(new AnimationEvent { functionName = name, time = time });
    }
    #endif
    private void ReturnIdle()
    {
        // 중간 타격 전에 방어 전환/손 놓기/대련 종료로 취소되면 피해도 취소합니다.
        pendingSlashHit = false;
        if (IsAttacking)
        {
            IsAttacking = false;
            npcStateMachine?.SetPlayerAttackTrail(false);
        }
        guardHeld = parryWindow = consumed = false;
        state = DefenseState.Idle;
        usingGuardMotion = false;
        if (mouseHand != null && mouseHand.ControlledHand != null) mouseHand.ControlledHand.rotation = mouseHand.RestRotation;
        blade?.SetMotionActive(false);
        if (animator != null && animator.HasState(0, Animator.StringToHash(idleState)))
            animator.CrossFadeInFixedTime(idleState, guardBlendSeconds, 0, 0f);
    }

}
