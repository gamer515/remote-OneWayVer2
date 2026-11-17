using UnityEngine;
using UnityEngine.InputSystem;

namespace OneWay.Battle3D
{
    // The original player remains usable without this opt-in component.
    [DisallowMultipleComponent]
    public sealed class NewPlayerAttackCombo : MonoBehaviour
    {
        public NewPlayerAttackSet motions;
        [Min(.1f)] public float playbackSpeed = 1f;
        [Min(0)] public float attackRetreatDistance = 1.2f;
        [Min(0)] public float skillDashDistance = 3f;
        [Min(1)] public float jumpHeightMultiplier = 2f;
        public bool readMouseInput = true;
        public bool IsActive { get; private set; }
        public bool Queued { get; private set; }
        public int SequenceIndex { get; private set; }
        public int AttackIndex => motions != null && motions.order.Length > 0 ? motions.order[SequenceIndex] : -1;
        public float Frame { get; private set; }
        public int DamageApplications { get; private set; }
        public bool InPreparation { get; private set; }
        public float ActiveSkillDashDistance { get; private set; }
        public bool SkillDashAssisted { get; private set; }
        public float ActiveAttackDashDistance { get; private set; }
        public bool AttackDashAssisted { get; private set; }
        public float AirborneHeight => (settlingJump || (IsActive && !InPreparation && AttackIndex == 5))
            ? Mathf.Max(0, transform.position.y - groundY) : 0;
        public DebugPlayerCombatFeel CombatFeel => feel != null && feel.isActiveAndEnabled ? feel : null;
        public bool IsRecoveringFromJump => settlingJump;
        public DebugPlayerSpecialCombo SpecialRules => specialRules != null && specialRules.isActiveAndEnabled ? specialRules : null;
        public bool IsSpecialCombo { get; private set; }
        public bool IsHoldingFinisher { get; private set; }
        public float FinisherHoldElapsed { get; private set; }
        public bool FinisherCommitted { get; private set; }
        bool AuthoredFinisher => SpecialRules != null && SpecialRules.finisherPreparation != null &&
            IsSpecialCombo && SequenceIndex == 6 && !InPreparation;
        float AttackDashStart => AuthoredFinisher ? Mathf.Max(27, SpecialRules.finisherDashStartFrame) : 27;
        float AttackDashEnd => AuthoredFinisher ? Mathf.Max(AttackDashStart + .1f, SpecialRules.finisherDashEndFrame) : 30;
        public bool CanEnterSpecial => SpecialRules != null && !IsSpecialCombo && SpecialRules.Ready &&
            ((IsActive && !InPreparation && SequenceIndex == 3 && jumpGateOpened) || (!IsActive && specialWindowRemaining > 0));
        DebugPlayerSpecialCombo specialRules;
        bool attackHeld, jumpGateOpened, specialInterrupted, pendingNormalPress;
        float attackHeldSeconds, specialWindowRemaining;
        bool attackDashPlanned;
        DebugPlayerCombatFeel feel;
        float bufferedAttackUntil = -1, fallVelocity;
        bool settlingJump;
        BattlePlayerController player;
        CharacterController motor;
        NewPlayerLocomotion locomotion;
        NewPlayerAttackSet.Motion current;
        bool[] struck, effects;
        Vector3 direction;
        float groundY, previousHeight, retreated;
        Collider[] jumpObstacles;

        void Awake() { player = GetComponent<BattlePlayerController>(); motor = GetComponent<CharacterController>(); locomotion = GetComponent<NewPlayerLocomotion>(); feel = GetComponent<DebugPlayerCombatFeel>(); specialRules = GetComponent<DebugPlayerSpecialCombo>(); }
        public void ProcessInput()
        {
            if (!readMouseInput) return;
            if (Mouse.current == null || !Application.isFocused) { DropAttackHold(); locomotion?.ReleaseGuard(); return; }
            var follow = player.viewCamera != null ? player.viewCamera.GetComponent<BattleFollowCamera>() : null;
            if (follow != null && !follow.InputCaptured) { DropAttackHold(); locomotion?.ReleaseGuard(); return; }
            if (locomotion != null && locomotion.enabled)
                locomotion.ProcessControlFrame(Mouse.current.scroll.ReadValue().y,
                    Mouse.current.leftButton.wasPressedThisFrame, Mouse.current.rightButton.wasPressedThisFrame,
                    Mouse.current.leftButton.isPressed);
            else
            {
                SetAttackHeld(Mouse.current.leftButton.isPressed);
                if (Mouse.current.leftButton.wasPressedThisFrame) RequestAttack();
            }
        }
        public void SetAttackHeld(bool held)
        {
            bool released = attackHeld && !held;
            attackHeld = held;
            if (!held) attackHeldSeconds = 0;
            if (!released || SpecialRules == null) return;
            if (IsSpecialCombo && !FinisherCommitted)
            {
                specialInterrupted = true;
                Queued = false;
                if (SequenceIndex == 6 || InPreparation) CancelSpecial();
            }
            else if (!IsActive && pendingNormalPress)
            {
                specialWindowRemaining = 0;
                pendingNormalPress = false;
                RequestAttack();
            }
        }
        void DropAttackHold()
        {
            attackHeld = false;
            attackHeldSeconds = 0;
            pendingNormalPress = false;
            specialWindowRemaining = 0;
            if (IsSpecialCombo && !FinisherCommitted) CancelSpecial();
        }
        public void RequestAttack()
        {
            if (!Application.isPlaying || motions == null || player == null || player.IsDefeated) return;
            if (SpecialRules != null)
            {
                if (IsSpecialCombo) return;
                // A fifth click is a potential hold, never an unconditional link to the special.
                if ((IsActive && SequenceIndex == 3) || (!IsActive && specialWindowRemaining > 0 && SpecialRules.Ready))
                { pendingNormalPress = true; return; }
            }
            if (CombatFeel != null && settlingJump) { bufferedAttackUntil = Time.time + CombatFeel.inputBufferSeconds; return; }
            if (locomotion != null && locomotion.enabled)
            {
                if (locomotion.Rolling || locomotion.Parrying || locomotion.KnockedBack) return;
                locomotion.Stop();
            }
            if (IsActive)
            {
                if (!InPreparation && Frame > current.chainFrame)
                {
                    if (CombatFeel != null) bufferedAttackUntil = Time.time + CombatFeel.inputBufferSeconds;
                    return;
                }
                Queued = true;
                return;
            }
            bufferedAttackUntil = -1;
            specialWindowRemaining = 0;
            jumpGateOpened = pendingNormalPress = false;
            SequenceIndex = 0;
            AimAtCamera();
            groundY = transform.position.y;
            previousHeight = retreated = 0;
            IsActive = true;
            Queued = false;
            Begin(motions.entry, true);
        }
        void Begin(NewPlayerAttackSet.Motion motion, bool preparation)
        {
            // Refresh on the actual Skill 03 start, including the middle of the combo.
            // Queue time and preparation time must not lock in an outdated camera heading.
            if (!preparation && AttackIndex == 4)
            {
                AimAtCamera();
                PlanSkillDash();
            }
            current = motion;
            if (!preparation && AttackIndex == 5)
                jumpObstacles = player.boss != null ? player.boss.GetComponentsInChildren<Collider>() : null;
            Frame = 0;
            InPreparation = preparation;
            struck = new bool[motion.damageWindows.Length];
            effects = new bool[struck.Length];
            retreated = 0;
            attackDashPlanned = false;
            ActiveAttackDashDistance = 0;
            AttackDashAssisted = false;
            Sample();
        }
        bool TryStartSpecial()
        {
            if (!CanEnterSpecial || !attackHeld || attackHeldSeconds + .00001f < SpecialRules.holdRecognitionSeconds ||
                settlingJump || (locomotion != null && (locomotion.DefenseMode || locomotion.Rolling || locomotion.Parrying || locomotion.KnockedBack))) return false;
            if (!SpecialRules.TryConsume()) return false;
            bool fromJump = IsActive;
            IsActive = IsSpecialCombo = true;
            specialInterrupted = pendingNormalPress = Queued = jumpGateOpened = false;
            FinisherCommitted = IsHoldingFinisher = false;
            FinisherHoldElapsed = 0;
            bufferedAttackUntil = -1;
            specialWindowRemaining = 0;
            SequenceIndex = 4;
            groundY = transform.position.y;
            previousHeight = 0;
            Begin(fromJump ? motions.links[3] : motions.entry, true);
            return true;
        }
        bool WantsNextAttack()
        {
            if (SpecialRules == null) return Queued;
            if (!IsSpecialCombo) return SequenceIndex < 3 && Queued;
            return SequenceIndex < 6 && attackHeld && !specialInterrupted;
        }
        void CancelSpecial()
        {
            bufferedAttackUntil = -1;
            pendingNormalPress = false;
            specialWindowRemaining = 0;
            Finish(true);
        }
        void AimAtCamera()
        {
            direction = Vector3.ProjectOnPlane(player.CameraForward, Vector3.up).normalized;
            if (direction.sqrMagnitude < .001f) direction = transform.forward;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            player.SyncBodyPosition();
        }
        void Sample()
        {
            if (player.animator == null || current == null) return;
            var clip = current.clip;
            float normalized = Mathf.Clamp01(Frame / Mathf.Max(.001f, current.Frames));
            // Only players with a dedicated preparation clip use the authored ready / pullback poses.
            // The combat frame stays fixed: no rush, hit, or slash effect can start during the hold.
            if (SpecialRules != null && SpecialRules.finisherPreparation != null && IsSpecialCombo &&
                SequenceIndex == 6 && !InPreparation && !FinisherCommitted && Frame >= 13)
            {
                clip = SpecialRules.finisherPreparation;
                normalized = SpecialRules.PreparationTime(Frame, FinisherHoldElapsed, IsHoldingFinisher) /
                    Mathf.Max(.001f, clip.length);
            }
            player.animator.speed = 0;
            player.animator.Play(Animator.StringToHash("Base Layer." + clip.name), 0, normalized);
            player.animator.Update(0);
        }
        void PlanSkillDash()
        {
            ActiveSkillDashDistance = PlanDash(skillDashDistance, out bool assisted);
            SkillDashAssisted = assisted;
        }
        void PlanAttackDash()
        {
            AimAtCamera();
            ActiveAttackDashDistance = PlanDash(retreated * 2f, out bool assisted);
            AttackDashAssisted = assisted;
            attackDashPlanned = true;
        }
        float PlanDash(float maximumDistance, out bool assisted)
        {
            maximumDistance = Mathf.Max(0, maximumDistance);
            assisted = false;
            var boss = player.boss;
            if (boss == null || !boss.IsAlive) return maximumDistance;
            // Range uses the same ground-plane surface distance as damage detection.
            if (player.BossInRange)
            {
                return 0;
            }
            if (player.viewCamera == null || boss.hitbox == null || !boss.hitbox.enabled) return maximumDistance;
            var center = boss.hitbox.bounds.center;
            var viewport = player.viewCamera.WorldToViewportPoint(center);
            // Five vertical screen bands: assist only in the middle band.
            if (viewport.z <= 0 || viewport.x < .4f || viewport.x > .6f || viewport.y < 0 || viewport.y > 1) return maximumDistance;
            var sample = new Vector3(transform.position.x, center.y, transform.position.z);
            var nearest = boss.hitbox.ClosestPoint(sample);
            var toBoss = Vector3.ProjectOnPlane(nearest - sample, Vector3.up);
            float distance = toBoss.magnitude;
            if (distance >= maximumDistance || distance <= .0001f) return maximumDistance;
            direction = toBoss / distance;
            float radius = motor != null ? motor.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) : 0;
            // Stop our body in front of the boss surface instead of aiming for its centre.
            assisted = true;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            player.SyncBodyPosition();
            return Mathf.Max(0, distance - radius - .05f);
        }
        public void Tick(float dt)
        {
            if (dt <= 0 || (CombatFeel != null && CombatFeel.IsHitStopped)) return;
            if (SpecialRules != null)
            {
                if (attackHeld) attackHeldSeconds += dt;
                if (!IsActive && specialWindowRemaining > 0)
                {
                    if (!TryStartSpecial()) specialWindowRemaining = Mathf.Max(0, specialWindowRemaining - dt);
                    if (!IsActive && specialWindowRemaining <= 0 && pendingNormalPress)
                    { pendingNormalPress = false; RequestAttack(); }
                }
                if (IsActive && !IsSpecialCombo && SequenceIndex == 3 && jumpGateOpened) TryStartSpecial();
            }
            if (settlingJump)
            {
                fallVelocity += (CombatFeel != null ? CombatFeel.cancelledJumpGravity : 55f) * dt;
                MoveJump(Vector3.down * Mathf.Min(Mathf.Max(0, transform.position.y - groundY), fallVelocity * dt));
                float landingTolerance = motor != null ? Mathf.Max(.02f, motor.skinWidth + .01f) : .02f;
                if (transform.position.y <= groundY + landingTolerance) { settlingJump = false; CombatFeel?.OnLanding(); }
                player.SyncBodyPosition();
            }
            if (!IsActive && !settlingJump && bufferedAttackUntil >= Time.time &&
                (locomotion == null || (!locomotion.Rolling && !locomotion.Parrying && !locomotion.DefenseMode && !locomotion.KnockedBack)))
                RequestAttack();
            if (locomotion != null && locomotion.enabled && locomotion.Tick(dt)) return;
            if (!IsActive || dt <= 0 || current == null) return;
            float remaining = dt * 30f * Mathf.Max(.1f, playbackSpeed);
            // Consume overshoot across transitions, even at low frame rates.
            for (int guard = 0; guard < 2048 && IsActive && remaining > .00001f; guard++)
            {
                if (SpecialRules != null && IsSpecialCombo && SequenceIndex == 6 && !InPreparation && !FinisherCommitted &&
                    Frame >= Mathf.Clamp(SpecialRules.finisherPoseFrame,13,26.5f) - .00001f)
                {
                    IsHoldingFinisher = true;
                    if (!attackHeld || specialInterrupted) { CancelSpecial(); break; }
                    float baseRate = 30f * Mathf.Max(.1f, playbackSpeed);
                    float used = Mathf.Min(remaining / baseRate, Mathf.Max(0, SpecialRules.finisherHoldSeconds - FinisherHoldElapsed));
                    FinisherHoldElapsed += used;
                    remaining -= used * baseRate;
                    Sample();
                    if (FinisherHoldElapsed + .00001f < SpecialRules.finisherHoldSeconds) break;
                    FinisherCommitted = true;
                    IsHoldingFinisher = false;
                }
                bool chain = !InPreparation && WantsNextAttack();
                bool jumpGate = SpecialRules != null && !IsSpecialCombo && SequenceIndex == 3 && !InPreparation && !jumpGateOpened;
                float boundary = chain || jumpGate ? current.chainFrame : current.Frames;
                float rate = 1, segmentEnd = current.Frames;
                if (CombatFeel != null) rate = CombatFeel.Rate(AttackIndex, Frame, current.Frames, InPreparation, out segmentEnd);
                float next = Mathf.Min(Mathf.Min(boundary, segmentEnd), Frame + Mathf.Min(remaining * rate, .5f));
                if (!InPreparation && AttackIndex == 0)
                {
                    if (Frame < 13) next = Mathf.Min(next, 13);
                    else if (Frame < AttackDashStart) next = Mathf.Min(next, AttackDashStart);
                    else if (Frame < AttackDashEnd) next = Mathf.Min(next, AttackDashEnd);
                    if (SpecialRules != null && IsSpecialCombo && !FinisherCommitted)
                        next = Mathf.Min(next, Mathf.Clamp(SpecialRules.finisherPoseFrame,13,26.5f));
                }
                float consumed = Mathf.Max(0, next - Frame);
                MoveBetween(Frame, next, consumed / (rate * 30f * Mathf.Max(.1f, playbackSpeed)));
                float before = Frame;
                Frame = next;
                // Capture the latest camera heading at the forward dash, before its hit window.
                if (!InPreparation && AttackIndex == 0 && Frame >= AttackDashStart && !attackDashPlanned)
                    PlanAttackDash();
                Sample();
                if (!InPreparation) CheckDamage(before, next);
                remaining -= consumed / rate;
                if (CombatFeel != null && CombatFeel.IsHitStopped) break;
                if (jumpGate && Frame >= current.chainFrame - .0001f)
                {
                    jumpGateOpened = true;
                    TryStartSpecial();
                    continue;
                }
                if (Frame >= boundary - .0001f)
                {
                    if (InPreparation) Begin(motions.attacks[AttackIndex], false);
                    else if (chain)
                    {
                        Queued = false;
                        var link = motions.links[SequenceIndex];
                        SequenceIndex = (SequenceIndex + 1) % motions.order.Length;
                        Begin(link, true);
                    }
                    else
                    {
                        bool endedJump = SpecialRules != null && !IsSpecialCombo && SequenceIndex == 3;
                        bool endedSpecial = IsSpecialCombo;
                        bool pendingAfterJump = pendingNormalPress;
                        bool restart = !endedSpecial && ((CombatFeel != null && bufferedAttackUntil >= Time.time) || (endedJump && pendingNormalPress && !attackHeld));
                        Finish();
                        if (restart) RequestAttack();
                        else
                        {
                            if (endedJump)
                            {
                                specialWindowRemaining = SpecialRules.specialEntryWindow;
                                pendingNormalPress = pendingAfterJump;
                            }
                            break;
                        }
                    }
                }
            }
        }
        void MoveBetween(float a, float b, float seconds)
        {
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            float forward = current.forward.Evaluate(b) - current.forward.Evaluate(a);
            float lateral = current.sideways.Evaluate(b) - current.sideways.Evaluate(a);
            float y = current.jump.Evaluate(b) * jumpHeightMultiplier;
            if (CombatFeel != null)
            {
                forward = InPreparation ? 0 : CombatFeel.Step(AttackIndex, b, current) - CombatFeel.Step(AttackIndex, a, current);
                lateral = 0;
                if (!InPreparation && AttackIndex == 5) y = CombatFeel.Jump(b, current, jumpHeightMultiplier);
                forward = ClampForwardStep(forward);
            }
            if (!InPreparation && AttackIndex == 4)
            {
                forward = ActiveSkillDashDistance * (DashProgress(b / 10f) - DashProgress(a / 10f));
                // Authored lateral root drift must not bypass an assisted or suppressed dash.
                lateral = 0;
            }
            if (!InPreparation && AttackIndex == 0)
            {
                forward = b <= 13f ? -attackRetreatDistance * (DashProgress(b/13f) - DashProgress(a/13f))
                    : a >= AttackDashStart && a < AttackDashEnd ? ActiveAttackDashDistance *
                        (DashProgress((b-AttackDashStart)/(AttackDashEnd-AttackDashStart))-
                         DashProgress((a-AttackDashStart)/(AttackDashEnd-AttackDashStart))) : 0;
                lateral = 0;
            }
            Vector3 start = transform.position;
            Vector3 step = direction * forward + side * lateral;
            // Animation is grounded; only code raises the CharacterController during the jump.
            step.y = y > 0 || previousHeight > 0 ? groundY + y - start.y : -2f * seconds;
            if (!InPreparation && AttackIndex == 5) MoveJump(step);
            else if (motor != null && motor.enabled) motor.Move(step); else transform.position += step;
            if (!InPreparation && AttackIndex == 0 && b <= 13f)
                retreated += Mathf.Max(0, -Vector3.Dot(transform.position-start, direction));
            if (!InPreparation && AttackIndex == 5 && previousHeight > .001f && y <= .001f) CombatFeel?.OnLanding();
            previousHeight = y;
            player.SyncBodyPosition();
        }
        float DashProgress(float t) => CombatFeel != null ? DebugPlayerCombatFeel.Ease(t) : Mathf.Clamp01(t);
        float ClampForwardStep(float distance)
        {
            if (distance <= 0 || player.boss == null || !player.boss.IsAlive || player.boss.hitbox == null) return distance;
            var box = player.boss.hitbox;
            var origin = new Vector3(transform.position.x, box.bounds.center.y, transform.position.z);
            var toBoss = Vector3.ProjectOnPlane(box.ClosestPoint(origin) - origin, Vector3.up);
            if (Vector3.Dot(direction, toBoss) <= 0) return distance;
            float radius = motor != null ? (motor.radius + motor.skinWidth) * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) : .55f;
            return Mathf.Min(distance, Mathf.Max(0, toBoss.magnitude - radius - .05f));
        }
        // Treat the boss body as an infinitely tall footprint during this attack.
        // Its top must never become a landing platform for the CharacterController.
        Vector3 OutsideBoss(Vector3 position)
        {
            if (jumpObstacles == null) return position;
            float clearance = motor != null
                ? (motor.radius + motor.skinWidth) * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) + .05f
                : .55f;
            for (int pass = 0; pass < 8; pass++)
            {
                bool changed = false;
                foreach (var obstacle in jumpObstacles)
                {
                    if (obstacle == null || !obstacle.enabled || !obstacle.gameObject.activeInHierarchy || obstacle.isTrigger) continue;
                    var bounds = obstacle.bounds;
                    // Upright capsules use their exact circular footprint. Other shapes
                    // conservatively use an enclosing circle, including rotated boxes.
                    bool round = obstacle is SphereCollider || (obstacle is CapsuleCollider capsule && capsule.direction == 1
                        && Mathf.Abs(Vector3.Dot(obstacle.transform.up, Vector3.up)) > .999f);
                    float radius = round ? Mathf.Max(bounds.extents.x, bounds.extents.z)
                        : new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
                    var away = Vector3.ProjectOnPlane(position - bounds.center, Vector3.up);
                    float distance = away.magnitude, minimum = radius + clearance;
                    if (distance >= minimum) continue;
                    away = distance > .0001f ? away / distance : -direction;
                    position += away * (minimum - distance);
                    changed = true;
                }
                if (!changed) break;
            }
            return position;
        }
        void MoveJump(Vector3 step)
        {
            // Small horizontal steps cannot tunnel across a body at high playback speed.
            int count = Mathf.Max(1, Mathf.CeilToInt(new Vector2(step.x, step.z).magnitude / .1f));
            var delta = step / count;
            for (int i = 0; i < count; i++)
            {
                // A moving boss can enter our footprint while we are airborne.
                // Resolve that overlap horizontally before descending, never upwards.
                var outside = OutsideBoss(transform.position);
                if ((outside - transform.position).sqrMagnitude > .000001f)
                {
                    bool enabledMotor = motor != null && motor.enabled;
                    if (enabledMotor) motor.enabled = false;
                    transform.position = outside;
                    if (enabledMotor) motor.enabled = true;
                }
                var destination = OutsideBoss(transform.position + delta);
                if (motor != null && motor.enabled) motor.Move(destination - transform.position);
                else transform.position = destination;
            }
        }
        void CheckDamage(float before, float after)
        {
            for (int i = 0; i < struck.Length; i++)
            {
                var window = current.damageWindows[i];
                if (after < window.x || before > window.y) continue;
                if (!effects[i])
                {
                    effects[i] = true;
                    float duration = CombatFeel != null
                        ? CombatFeel.Duration(AttackIndex, window.x, window.y, current.Frames, playbackSpeed)
                        : (window.y-window.x)/(30f*playbackSpeed);
                    bool heavy = AttackIndex == 0 || AttackIndex == 5;
                    GetComponent<BattleSwordSlashEffect>()?.PlayAuthoredSwing(duration,
                        CombatFeel != null ? CombatFeel.slashTailSeconds : .1f,
                        CombatFeel != null ? (heavy ? CombatFeel.heavySlashScale : CombatFeel.lightSlashScale) : 1);
                    CombatFeel?.OnSwing(AttackIndex);
                }
                if (struck[i] || !player.BossInRange) continue;
                Vector3 toBoss = player.boss.transform.position-transform.position; toBoss.y=0;
                if (Vector3.Angle(direction,toBoss)>75f) continue;
                float healthBefore = player.boss.Health;
                player.boss.TakeDamage(player.normalDamage);
                if (player.boss.Health < healthBefore)
                {
                    GetComponent<BattleSwordSlashEffect>()?.PlayHitFlash(player.boss);
                    CombatFeel?.OnHit(AttackIndex);
                }
                player.feedback?.ShowAttack(player.boss.transform.position+Vector3.up,false);
                struck[i]=true;
                DamageApplications++;
            }
        }
        void Finish(bool blendToIdle = false)
        {
            IsSpecialCombo = IsHoldingFinisher = FinisherCommitted = false;
            FinisherHoldElapsed = 0;
            specialInterrupted = jumpGateOpened = pendingNormalPress = false;
            specialWindowRemaining = 0;
            IsActive = Queued = InPreparation = false;
            current = null;
            previousHeight = 0;
            if (player != null && player.animator != null && motions != null)
            {
                player.animator.speed = 1;
                if (blendToIdle && SpecialRules != null)
                    player.animator.CrossFadeInFixedTime("Base Layer.Idle", SpecialRules.cancelBlendSeconds, 0);
                else { player.animator.Play("Base Layer.Idle", 0, 0); player.animator.Update(0); }
            }
        }
        public void Cancel()
        {
            bufferedAttackUntil = -1;
            pendingNormalPress = false;
            specialWindowRemaining = 0;
            attackHeld = false;
            attackHeldSeconds = 0;
            if (!IsActive) return;
            if (IsActive && previousHeight > 0 && motor != null && motor.enabled)
            {
                if (CombatFeel != null) { settlingJump = true; fallVelocity = 0; }
                else MoveJump(Vector3.up*(groundY-transform.position.y));
            }
            Finish();
        }
        public void ResetAirRecovery()
        {
            settlingJump = false;
            previousHeight = fallVelocity = 0;
            bufferedAttackUntil = -1;
            specialWindowRemaining = 0;
            attackHeldSeconds = 0;
            attackHeld = pendingNormalPress = false;
            groundY = transform.position.y;
            CombatFeel?.ResetFeedback();
        }
        void OnDisable()
        {
            if (player != null) Cancel();
            if (settlingJump && motor != null && motor.enabled) MoveJump(Vector3.up * (groundY - transform.position.y));
            settlingJump = false;
        }
    }
}
