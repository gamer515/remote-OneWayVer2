using UnityEngine;

namespace OneWay.Battle3D
{
    public enum B2CombatState { Idle, Run, ApproachDash, Attack, HitReaction, PhaseTransition, Dead, SmokePhase }

    // B2 owns its decisions; B1's jump/phase-entry gimmick is deliberately independent.
    [DefaultExecutionOrder(20), DisallowMultipleComponent, RequireComponent(typeof(BattleBoss))]
    public sealed class B2CombatController : MonoBehaviour
    {
        public Battle3DSceneController battle;
        public Animator animator;
        public B2AttackStepMovement movement;
        public B2PhaseSmokeAttack smokePhase;
        public BossWeaponAttackPattern[] attacks = new BossWeaponAttackPattern[0];
        public AnimationClip idleClip, runClip, approachClip, hitClip, phaseClip, deathClip;
        public AnimationCurve approachDistance = AnimationCurve.Linear(0, 1, 0, 1);
        [Header("Distance to the boss body surface (world units)")]
        [Min(.1f)] public float attackRange = 2.5f;
        [Min(.1f)] public float dashStartDistance = 12f;
        [Min(0f)] public float runSpeed = 6f;
        [Min(0f)] public float turnSpeed = 360f;
        [Min(0f)] public float dashCooldown = 6f;
        [Min(0f)] public float dashDistanceMultiplier = 1f;
        [Header("Combat timing")]
        [Min(0f)] public float afterAttackWait = 1f;
        [Min(0f)] public float startWait = 1f;
        [Min(0f)] public float transitionSeconds = .12f;
        public bool randomAttackOrder = true;
        [Tooltip("B1처럼 체력 2/3, 1/3 통과 시 피격 동작을 각각 한 번 예약합니다.")]
        public bool reactAtHealthThresholds = true;
        public B2CombatState State { get; private set; }
        public bool PhaseTransitionCompleted { get; private set; }
        public int PhaseTransitionCount { get; private set; }
        public float TargetDistance { get; private set; }
        public bool PendingHit => pendingHit;
        public bool PendingPhase => pendingPhase;
        public bool ControlsAnimation => isActiveAndEnabled && initialized;
        BattleBoss boss;
        BossPatternRunner runner;
        BossWeaponHit weaponHit;
        bool initialized, pendingHit, pendingPhase, phaseConsumed;
        float idleRemaining, dashRemaining, previousDashDistance;
        int lastAttack = -1;
        string activeState;
        BattlePlayerHealth Target => battle != null && battle.player != null ? battle.player.GetComponent<BattlePlayerHealth>() : null;

        void Awake() => Cache();
        void Cache()
        {
            if (boss == null) boss = GetComponent<BattleBoss>();
            if (runner == null) runner = GetComponent<BossPatternRunner>();
            if (weaponHit == null) weaponHit = GetComponent<BossWeaponHit>();
            if (animator == null && boss != null) animator = boss.animator;
            if (movement == null) movement = GetComponent<B2AttackStepMovement>();
            if (smokePhase == null) smokePhase = GetComponent<B2PhaseSmokeAttack>();
        }
        void OnEnable() { if (Application.isPlaying) ResetCombat(); }
        public bool CanForcePhaseTwo
        {
            get
            {
                Cache();
                return Application.isPlaying && isActiveAndEnabled && boss != null && boss.IsAlive &&
                    battle != null && battle.CurrentStage != null && battle.CurrentStage.boss == boss &&
                    Target != null && Target.IsAlive && battle.GetComponent<BattleIntroWalkDebug>()?.IsRunning != true &&
                    smokePhase != null && smokePhase.isActiveAndEnabled && !smokePhase.IsRunning &&
                    animator != null && animator.HasState(0, Animator.StringToHash("Base Layer.PhaseRecovery")) &&
                    State != B2CombatState.PhaseTransition && State != B2CombatState.SmokePhase;
            }
        }
        // Inspector test entry: bypass the health threshold and interrupt the current action.
        // Normal health-triggered transitions still wait for attacks and reactions to finish.
        public bool ForcePhaseTwo()
        {
            if (!CanForcePhaseTwo) return false;
            if (!initialized) ResetCombat();
            runner?.StopExecution(); weaponHit?.End(); movement?.ResetTracking();
            smokePhase.Cancel();
            pendingHit = pendingPhase = false; phaseConsumed = true;
            PhaseTransitionCompleted = false; PhaseTransitionCount++;
            idleRemaining = previousDashDistance = 0;
            runner?.SetTarget(Target);
            FaceTarget(0, true);
            Enter(B2CombatState.PhaseTransition, "PhaseRecovery", true);
            return true;
        }
        void OnDisable()
        {
            smokePhase?.Cancel();
            runner?.StopExecution(); weaponHit?.End(); movement?.ResetTracking();
            initialized = false;
            if (boss != null) boss.PatternControlsAnimation = false;
        }
        public void ResetCombat()
        {
            Cache(); runner?.StopExecution(); weaponHit?.End(); movement?.ResetTracking();
            smokePhase?.Cancel();
            pendingHit = pendingPhase = phaseConsumed = false;
            PhaseTransitionCompleted = false; PhaseTransitionCount = 0;
            lastAttack = -1; dashRemaining = previousDashDistance = 0;
            initialized = true; boss.patrolEnabled = false;
            EnterIdle(startWait, true);
        }
        public void OnDamage(float before, float after)
        {
            if (!isActiveAndEnabled || !initialized || after >= before) return;
            if (after <= 0) { EnterDeath(); return; }
            if (reactAtHealthThresholds &&
                ((before > boss.maxHealth * (2f / 3f) && after <= boss.maxHealth * (2f / 3f)) ||
                 (before > boss.maxHealth / 3f && after <= boss.maxHealth / 3f))) pendingHit = true;
            if (!phaseConsumed && after < boss.maxHealth * .5f) pendingPhase = true;
        }
        void Update() => Tick(Time.deltaTime);
        public void Tick(float delta)
        {
            if (!initialized) ResetCombat();
            delta = Mathf.Max(0, delta);
            if (!boss.IsAlive) { EnterDeath(); return; }
            if (!phaseConsumed && boss.Health < boss.maxHealth * .5f) pendingPhase = true;
            if (State == B2CombatState.Dead) return;
            var target = Target;
            bool selected = battle != null && battle.CurrentStage != null && battle.CurrentStage.boss == boss;
            bool intro = battle != null && battle.GetComponent<BattleIntroWalkDebug>()?.IsRunning == true;
            if (!selected || target == null || !target.IsAlive || intro)
            {
                smokePhase?.Cancel();
                if (State != B2CombatState.Idle) { runner?.StopExecution(); EnterIdle(startWait); }
                return;
            }
            runner.SetTarget(target);
            TargetDistance = boss.PlanarDistance(target.transform.position);
            dashRemaining = Mathf.Max(0, dashRemaining - delta);
            if (State == B2CombatState.SmokePhase)
            {
                if (smokePhase != null && smokePhase.IsRunning) return;
                PhaseTransitionCompleted = smokePhase != null && smokePhase.Completed;
                dashRemaining = dashCooldown;
                EnterIdle(.15f); return;
            }
            if (State == B2CombatState.Attack)
            {
                if (runner.IsRunning) return;
                EnterIdle(afterAttackWait);
            }
            if (State == B2CombatState.HitReaction || State == B2CombatState.PhaseTransition || State == B2CombatState.ApproachDash)
            {
                if (!AnimationFinished()) return;
                if (State == B2CombatState.PhaseTransition)
                {
                    if (smokePhase != null && smokePhase.isActiveAndEnabled && smokePhase.Begin(target))
                    { State = B2CombatState.SmokePhase; return; }
                    PhaseTransitionCompleted = true;
                }
                if (State == B2CombatState.ApproachDash) AdvanceDash();
                EnterIdle(.15f);
            }
            // Damage reactions and the one-time recovery wait for the previous full action.
            if (pendingHit)
            {
                pendingHit = false; Enter(B2CombatState.HitReaction, "HitReaction"); return;
            }
            if (pendingPhase)
            {
                // The authored collapse starts at Idle_02; settle into that exact pose first.
                if (State != B2CombatState.Idle) { EnterIdle(.25f); return; }
                if (idleRemaining > 0) { idleRemaining = Mathf.Max(0, idleRemaining - delta); return; }
                pendingPhase = false; phaseConsumed = true; PhaseTransitionCount++;
                FaceTarget(delta, true); Enter(B2CombatState.PhaseTransition, "PhaseRecovery"); return;
            }
            if (State == B2CombatState.Idle && idleRemaining > 0)
            { idleRemaining = Mathf.Max(0, idleRemaining - delta); FaceTarget(delta, false); return; }
            FaceTarget(delta, State != B2CombatState.Run);
            if (TargetDistance <= attackRange)
            {
                if (!BeginAttack()) EnterIdle(.25f);
                return;
            }
            if (TargetDistance >= Mathf.Max(attackRange + .5f, dashStartDistance) && dashRemaining <= 0)
            {
                FaceTarget(delta, true); previousDashDistance = approachDistance.Evaluate(0);
                dashRemaining = dashCooldown; Enter(B2CombatState.ApproachDash, "ApproachDash"); return;
            }
            if (State != B2CombatState.Run) Enter(B2CombatState.Run, "Run");
            float step = Mathf.Min(runSpeed * delta, Mathf.Max(0, TargetDistance - attackRange + .01f));
            movement?.MoveWorldDelta(transform.forward * step);
        }
        void LateUpdate() { if (State == B2CombatState.ApproachDash && boss != null && boss.IsAlive) AdvanceDash(); }
        void AdvanceDash()
        {
            if (approachClip == null || animator == null || Target == null) return;
            var state = RelevantState();
            if (!state.IsName("ApproachDash")) return;
            float value = approachDistance.Evaluate(Mathf.Clamp01(state.normalizedTime) * approachClip.length);
            float delta = Mathf.Max(0, value - previousDashDistance) * Mathf.Abs(animator.transform.lossyScale.z) * dashDistanceMultiplier;
            previousDashDistance = value; // Consume blocked travel, never accumulate a catch-up teleport.
            float gap = boss.PlanarDistance(Target.transform.position);
            delta = Mathf.Min(delta, Mathf.Max(0, gap - attackRange));
            movement?.MoveWorldDelta(transform.forward * delta);
        }
        bool BeginAttack()
        {
            if (attacks == null || attacks.Length == 0) return false;
            int next = (lastAttack + 1) % attacks.Length;
            if (randomAttackOrder && attacks.Length > 1)
            {
                next = lastAttack < 0 ? Random.Range(0, attacks.Length) : Random.Range(0, attacks.Length - 1);
                if (lastAttack >= 0 && next >= lastAttack) next++;
            }
            var attack = attacks[next];
            if (attack == null || !attack.IsValid(runner)) return false;
            State = B2CombatState.Attack; activeState = attack.animationState;
            boss.PatternControlsAnimation = true;
            if (!runner.RunExecution(attack)) return false;
            lastAttack = next; return true;
        }
        AnimatorStateInfo RelevantState() => animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        bool AnimationFinished()
        {
            var state = RelevantState();
            return state.fullPathHash == Animator.StringToHash(activeState) && state.normalizedTime >= 1f;
        }
        void FaceTarget(float delta, bool immediate)
        {
            if (Target == null) return;
            Vector3 direction = Target.transform.position - transform.position; direction.y = 0;
            if (direction.sqrMagnitude < .0001f) return;
            Quaternion facing = Quaternion.LookRotation(direction);
            transform.rotation = immediate ? facing : Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * delta);
        }
        void EnterIdle(float delay, bool immediate = false)
        { idleRemaining = delay; Enter(B2CombatState.Idle, "Idle", immediate); }
        void Enter(B2CombatState state, string name, bool immediate = false)
        {
            weaponHit?.End(); movement?.ResetTracking();
            State = state; activeState = "Base Layer." + name; boss.PatternControlsAnimation = true;
            if (animator == null || !animator.HasState(0, Animator.StringToHash(activeState))) return;
            if (immediate) animator.Play(activeState, 0, 0);
            else animator.CrossFadeInFixedTime(activeState, transitionSeconds, 0, 0);
        }
        void EnterDeath()
        {
            if (State == B2CombatState.Dead) return;
            smokePhase?.Cancel();
            runner?.StopExecution(); pendingHit = pendingPhase = false;
            Enter(B2CombatState.Dead, "Dead");
        }
    }
}
