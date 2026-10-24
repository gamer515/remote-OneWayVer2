using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace OneWay.Battle3D
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class BattlePlayerController : MonoBehaviour
    {
        public BattleBoss boss;
        public Camera viewCamera;
        public BattleCombatFeedback feedback;
        [Header("Shield and roll")]
        public GameObject shield;
        [Min(.1f)] public float shieldAnimationSpeed = 1f;
        [Min(.1f)] public float rollAnimationSpeed = 2f;
        [Min(0f)] public float rollDistance = 4.32f;
        public bool IsDefenseMode { get; private set; }
        public bool IsBlocking => IsDefenseMode && blockRemaining > 0f;
        float blockRemaining, shieldClipSeconds = 2.333333f, rollClipSeconds = 1.966667f;
        float activeRollDuration;
        static readonly int ShieldAnimation = Animator.StringToHash("Base Layer.MeshyShieldPushLeft_Player");
        static readonly int RollAnimation = Animator.StringToHash("Base Layer.MeshyGroundRoll_Player");
        static readonly int DeathAnimation = Animator.StringToHash("Base Layer.MeshyCrouchPick_Player");
        public bool IsDefeated { get; private set; }
        float deathElapsed, deathClipSeconds = 2f, animatorSpeedBeforeDeath = 1f;
        [Header("Knight animation (existing clips)")]
        public Animator animator;
        public float normalAnimationSeconds = 0.56f;
        public float specialAnimationSeconds = 0.94f;
        [Min(0.1f)] public float moveSpeed = 6f;
        [Min(0.1f)] public float attackRange = 3.5f;
        [Min(0.05f)] public float chargeSeconds = 0.7f;
        [Min(0.05f)] public float attackCooldown = 0.3f;
        [Min(0.1f)] public float dashSpeed = 24f;
        [Min(0.01f)] public float dashDuration = 0.18f;
        [Min(0f)] public float dashCooldown = 0.45f;
        public float normalDamage = 25f;
        public float specialDamage = 100f;
        [Header("Sword combo")]
        [Min(0.1f)] public float normalAttackSpeed = 1.2f; //공속
        [Min(0.1f)] public float comboResetSeconds = 0.65f;
        [Min(0f)] public float firstSlashStepDistance = 0.55f;
        [Tooltip("Travel distance during hit 1 when the boss is outside attack range.")]
        [Min(0f)] public float firstAttackDashDistance = 4f;
        public bool IsFirstAttackDashing => firstAttackDash && attackAnimationRemaining > 0f;
        bool firstAttackDash;
        float firstAttackDashElapsed, firstAttackDashSeconds, firstAttackDashTravel;
        public int ComboStep { get; private set; }
        public int NormalAttackStartFrame { get; private set; }
        public bool UsesFiveHitCombo => fiveHitReady;
        public const float FiveHitRecoverySeconds = 18f / 60f;
        readonly float[] fiveHitDurations = new float[5];
        bool fiveHitReady;
        static readonly int[] FiveHitAnimations = {
            Animator.StringToHash("Base Layer.MeshyCombo01"), Animator.StringToHash("Base Layer.MeshyCombo02"),
            Animator.StringToHash("Base Layer.MeshyCombo03"), Animator.StringToHash("Base Layer.MeshyCombo04"),
            Animator.StringToHash("Base Layer.MeshyCombo05") };
        public bool UsesNewNormalAttack => !UsesFiveHitCombo && newNormalDuration > 0f && animator != null && animator.HasState(0, NewNormalAnimation);
        float newNormalDuration, newNormalFrameRate = 60f;
        int normalRepeatStartFrame = 6;
        bool newNormalChain;
        static readonly int NewNormalAnimation = Animator.StringToHash("Base Layer.AttackNew");

        public enum PressState { Idle, Charging, Cancelled, Dash }
        public PressState State { get; private set; }
        public float Charge01 => State == PressState.Charging ? Mathf.Clamp01(heldSeconds / chargeSeconds) : 0f;
        public bool IsDashing => dashRemaining > 0f;
        public bool IsMoving => moving;
        public string Status { get; private set; } = "Attack mode";
        public bool BossInRange => boss != null && boss.IsAlive && boss.PlanarDistance(transform.position) <= attackRange;

        CharacterController motor;
        Vector3 destination, dashDirection;
        float heldSeconds, dashRemaining, nextDash, nextAttack;
        bool moving;
        float attackAnimationRemaining;
        Vector3 attackPosition;
        Quaternion attackRotation;
        bool attackDirectionHeld;
        public bool IsAttackLocked => attackAnimationRemaining > 0f && !IsDashing;
        float comboGateRemaining, comboWindowRemaining;
        int nextComboIndex;
        bool queuedNormalAttack;
        float slashStepTime, slashStepDuration;
        bool slashStepActive;
        Vector3 slashStepDirection;
        readonly float[] comboDurations = { 0.483333f, 0.5f, 0.65f, 0.8f };
        readonly float[] comboFrameRates = { 60f, 60f, 60f, 60f };
        static readonly int[] ComboAnimations = {
            Animator.StringToHash("Base Layer.Attack"), Animator.StringToHash("Base Layer.Attack2"),
            Animator.StringToHash("Base Layer.Attack3"), Animator.StringToHash("Base Layer.Attack4") };
        static readonly int IdleAnimation = Animator.StringToHash("Base Layer.Idle");
        static readonly int RunAnimation = Animator.StringToHash("Base Layer.MeshyRun_Player");
        static readonly int AttackAnimation = Animator.StringToHash("Base Layer.Attack");
        static readonly int SpecialAnimation = Animator.StringToHash("Base Layer.Special");

        void Awake()
        {
            motor = GetComponent<CharacterController>();
            SetDefenseMode(false);
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == "MeshyCrouchPick_Player") deathClipSeconds = clip.length;
                if (clip.name == "MeshyShieldPushLeft_Player_3x") shieldClipSeconds = clip.length;
                if (clip.name == "MeshyGroundRoll_Player") rollClipSeconds = clip.length;
                for (int i = 0; i < fiveHitDurations.Length; i++)
                    if (clip.name == "MeshyCombo0" + (i + 1)) fiveHitDurations[i] = clip.length;
                if (clip.name == "voltslashnew01" || clip.name == "MeshyWeaponCombo_Player")
                {
                    newNormalDuration = clip.length;
                    newNormalFrameRate = Mathf.Max(1f, clip.frameRate);
                    normalRepeatStartFrame = clip.name == "MeshyWeaponCombo_Player" ? 0 : 6;
                }
                for (int i = 0; i < comboDurations.Length; i++)
                    if (clip.name == "VoltSlash0" + (i + 1))
                    {
                        comboDurations[i] = clip.length;
                        comboFrameRates[i] = Mathf.Max(1f, clip.frameRate);
                    }
            }
            fiveHitReady = true;
            for (int i = 0; i < fiveHitDurations.Length; i++)
                fiveHitReady &= fiveHitDurations[i] > FiveHitRecoverySeconds && animator.HasState(0, FiveHitAnimations[i]);
        }

        void Update()
        {
            var health = GetComponent<BattlePlayerHealth>();
            if (health != null && !health.IsAlive) { PlayDefeat(); return; }
            Mouse mouse = Mouse.current;
            if (mouse == null || !Application.isFocused)
            {
                CancelInput();
                return;
            }
            var follow = viewCamera != null ? viewCamera.GetComponent<BattleFollowCamera>() : null;
            if (follow != null && !follow.InputCaptured) { CancelInput(); return; }
            ProcessControlFrame(mouse.scroll.ReadValue().y, mouse.leftButton.wasPressedThisFrame,
                mouse.rightButton.wasPressedThisFrame, Time.deltaTime);
            Move(Time.deltaTime);
        }

        public Vector3 CameraForward
        {
            get
            {
                Vector3 direction = viewCamera != null ? viewCamera.transform.forward : transform.forward;
                direction.y = 0f;
                return direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
            }
        }

        public bool BackwardDashPending => false;

        void SetDefenseMode(bool defense)
        {
            IsDefenseMode = defense;
            if (shield != null) shield.SetActive(defense);
            if (!defense) blockRemaining = 0f;
            Status = defense ? "Defense mode" : "Attack mode";
        }

        void StartBlock()
        {
            ResetCombo();
            moving = false;
            attackAnimationRemaining = 0f;
            attackDirectionHeld = false;
            Face(CameraForward);
            attackPosition = transform.position;
            attackRotation = transform.rotation;
            blockRemaining = shieldClipSeconds / Mathf.Max(.1f, shieldAnimationSpeed);
            animator.SetFloat("ShieldSpeed", Mathf.Max(.1f, shieldAnimationSpeed));
            animator.Play(ShieldAnimation, 0, 0f);
            Status = "Shield block";
        }

        public void ProcessControlFrame(float scroll, bool attackDown, bool dashDown, float deltaTime)
        {
            if (IsDefeated) return;
            nextAttack = Mathf.Max(0f, nextAttack - deltaTime);
            comboGateRemaining = Mathf.Max(0f, comboGateRemaining - deltaTime);
            comboWindowRemaining = Mathf.Max(0f, comboWindowRemaining - deltaTime);
            if (comboWindowRemaining <= 0f && !queuedNormalAttack && !IsAttackLocked)
            {
                ResetCombo();
                attackDirectionHeld = false;
            }
            if (scroll != 0f)
            {
                SetDefenseMode(scroll < 0f);
                if (IsDefenseMode) queuedNormalAttack = false;
            }
            // A dash cancels attack recovery immediately. No post-dash input lock.
            if (dashDown && !IsDashing)
            {
                attackDirectionHeld = false;
                dashDirection = CameraForward;
                blockRemaining = 0f;
                moving = true;
                ResetCombo();
                attackAnimationRemaining = 0f;
                nextAttack = 0f;
                activeRollDuration = rollClipSeconds / Mathf.Max(.1f, rollAnimationSpeed);
                dashRemaining = activeRollDuration;
                animator.SetFloat("RollSpeed", Mathf.Max(.1f, rollAnimationSpeed));
                animator.Play(RollAnimation, 0, 0f);
                State = PressState.Dash;
                Face(CameraForward);
                Status = "Roll then auto run";
            }
            else if (attackDown && !IsDashing && IsDefenseMode)
            {
                // Blocking interrupts the current attack and clears its queued combo.
                if (!IsBlocking) StartBlock();
            }
            else if (attackDown && !IsDashing)
            {
                moving = false;
                if ((UsesFiveHitCombo || UsesNewNormalAttack || ComboStep == 1 || ComboStep == 2) && comboGateRemaining <= 0f)
                    ResetCombo();
                if (comboGateRemaining > 0f) queuedNormalAttack = true;
                else if (nextAttack <= 0f) ExecuteAttack(false);
            }
            if (queuedNormalAttack && comboGateRemaining <= 0f && !IsDashing && !IsDefenseMode)
            {
                queuedNormalAttack = false;
                ExecuteAttack(false);
            }
        }

        // Compatibility for existing editor tooling; live input uses ProcessControlFrame.
        public void ProcessPointerFrame(Vector3 point, bool hasPoint, bool overBoss,
            bool rightDown, bool leftDown, bool leftUp, float deltaTime)
            => ProcessControlFrame(0f, leftDown, rightDown, deltaTime);

        void ExecuteAttack(bool special)
        {
            bool dashOnFirst = !special && nextComboIndex == 0 && !BossInRange;
            if (IsFirstAttackDashing)
            {
                attackPosition = transform.position;
                attackRotation = transform.rotation;
            }
            firstAttackDash = false;
            // Keep aim across the animation-end / next-click boundary, too.
            // Animation combo resets must not reset the direction of an attack sequence.
            if (!attackDirectionHeld || dashOnFirst)
            {
                Face(CameraForward);
                attackPosition = transform.position;
                attackRotation = transform.rotation;
                attackDirectionHeld = true;
            }
            transform.SetPositionAndRotation(attackPosition, attackRotation);
            moving = false;
            PlayAttackAnimation(special);
            if (!special) GetComponent<BattleSwordSlashEffect>()?.PlaySwing(ComboStep, normalAttackSpeed);
            slashStepActive = false;
            firstAttackDash = dashOnFirst;
            firstAttackDashElapsed = 0f;
            firstAttackDashSeconds = Mathf.Max(.01f, comboGateRemaining);
            firstAttackDashTravel = Mathf.Max(0f, firstAttackDashDistance);
            if (BossInRange)
            {
                Vector3 direction = boss.transform.position - transform.position;
                direction.y = 0f;
                if (Vector3.Angle(transform.forward, direction) <= 75f)
                {
                    boss.TakeDamage(special ? specialDamage : normalDamage);
                    feedback?.ShowAttack(boss.transform.position + Vector3.up, special);
                }
            }
            nextAttack = !special && (UsesFiveHitCombo || UsesNewNormalAttack) ? Mathf.Min(attackCooldown, comboGateRemaining) : attackCooldown;
            Status = "Normal attack";
        }

        void ResetCombo()
        {
            firstAttackDash = false;
            newNormalChain = false;
            nextComboIndex = 0;
            ComboStep = 0;
            comboGateRemaining = comboWindowRemaining = 0f;
            queuedNormalAttack = false;
            slashStepActive = false;
        }

        void CancelCharge()
        {
            ResetCombo();
            State = PressState.Cancelled;
            heldSeconds = 0f;
            Status = "Charge cancelled";
        }

        public void CancelInput()
        {
            attackDirectionHeld = false;
            ResetCombo();
            attackAnimationRemaining = 0f;
            State = PressState.Idle;
            heldSeconds = dashRemaining = 0f;
            moving = false;
            blockRemaining = 0f;
        }

        public void PlayDefeat()
        {
            if (IsDefeated) return;
            CancelInput();
            IsDefeated = true;
            deathElapsed = 0f;
            Status = "Defeated";
            if (animator == null || animator.runtimeAnimatorController == null) return;
            animatorSpeedBeforeDeath = animator.speed;
            animator.speed = 0f;
            animator.Play(DeathAnimation, 0, 0f);
            animator.Update(0f);
        }

        public void ResetForBattle(Transform spawn)
        {
            if (IsDefeated && animator != null) animator.speed = animatorSpeedBeforeDeath;
            IsDefeated = false;
            deathElapsed = 0f;
            CancelInput();
            SetDefenseMode(false);
            nextDash = nextAttack = 0f;
            var controller = GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false;
            if (spawn != null) transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            if (wasEnabled) controller.enabled = true;
            GetComponent<BattlePlayerHealth>()?.ResetHealth();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }
            Status = "Attack mode";
        }

        void OnApplicationFocus(bool focused) { if (!focused) CancelInput(); }
        void OnDisable() => CancelInput();

        // Preserve attack direction while accepting external body collision correction.
        public void SyncBodyPosition() => attackPosition = transform.position;

        public void Move(float deltaTime)
        {
            if (IsDefeated) return;
            if (IsFirstAttackDashing)
            {
                Vector3 forward = CameraForward;
                Face(forward);
                float before = FirstAttackDashProgress(firstAttackDashElapsed / firstAttackDashSeconds);
                firstAttackDashElapsed = Mathf.Min(firstAttackDashSeconds, firstAttackDashElapsed + deltaTime);
                float after = FirstAttackDashProgress(firstAttackDashElapsed / firstAttackDashSeconds);
                if (motor != null && motor.enabled)
                    motor.Move(forward * (firstAttackDashTravel * (after - before)) + Vector3.down * (2f * deltaTime));
                attackPosition = transform.position;
                attackRotation = transform.rotation;
                return;
            }
            if (IsAttackLocked || IsBlocking)
            {
                transform.SetPositionAndRotation(attackPosition, attackRotation);
                return;
            }
            if (motor == null || !motor.enabled) return;
            Vector3 step = Vector3.zero;
            if (IsDashing)
            {
                float dashStep = Mathf.Min(deltaTime, dashRemaining);
                step = dashDirection * (rollDistance / Mathf.Max(.01f, activeRollDuration) * dashStep);
                dashRemaining = Mathf.Max(0f, dashRemaining - deltaTime);
                if (!IsDashing)
                {
                    State = PressState.Idle;
                    if (moving) step += CameraForward * (moveSpeed * Mathf.Max(0f, deltaTime - dashStep));
                }
            }
            else if (moving)
            {
                Vector3 direction = CameraForward;
                step = direction * (moveSpeed * deltaTime);
                Face(direction);
            }
            else if (slashStepActive)
            {
                float before = SlashStepProgress(slashStepTime / slashStepDuration);
                slashStepTime = Mathf.Min(slashStepTime + deltaTime, slashStepDuration);
                float after = SlashStepProgress(slashStepTime / slashStepDuration);
                step = slashStepDirection * (firstSlashStepDistance * (after - before));
                if (slashStepTime >= slashStepDuration) slashStepActive = false;
            }
            CollisionFlags flags = motor.Move(step + Vector3.down * (2f * deltaTime));
            if ((flags & CollisionFlags.Sides) != 0) slashStepActive = false;
        }

        // Integral of speed 1.5 * (1 - t*t): starts fast, decelerates to zero.
        // Normalized to preserve the configured distance and duration.
        static float FirstAttackDashProgress(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);
            return 1.5f * t - .5f * t * t * t;
        }

        // Same smooth forward-travel curve as the Blender reference action.
        static float SlashStepProgress(float normalizedTime)
        {
            float t = Mathf.InverseLerp(0.10f, 0.85f, normalizedTime);
            return t * t * (3f - 2f * t);
        }

        void Face(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
        }

        public float ComboChainDelay(int index)
        {
            if (UsesFiveHitCombo)
            {
                index = Mathf.Clamp(index, 0, 4);
                // The fifth hit completes recovery before beginning a new sequence.
                return Mathf.Max(.01f, (fiveHitDurations[index] - (index < 4 ? FiveHitRecoverySeconds : 0f)) / Mathf.Max(.1f, normalAttackSpeed));
            }
            if (UsesNewNormalAttack)
                return Mathf.Max(.01f, (newNormalDuration - (index == 0 ? 0f : normalRepeatStartFrame / newNormalFrameRate)) / Mathf.Max(.1f, normalAttackSpeed));
            float point = index == 0 ? 42f / comboFrameRates[index]
                : index == 1 ? 36f / comboFrameRates[index] : comboDurations[index] * .85f;
            return Mathf.Max(attackCooldown, Mathf.Min(comboDurations[index], point));
        }

        void PlayAttackAnimation(bool special)
        {
            if (!special && UsesFiveHitCombo)
            {
                int fiveIndex = nextComboIndex;
                float speed = Mathf.Max(.1f, normalAttackSpeed);
                bool restartingDuringRecovery = fiveIndex == 0 && attackAnimationRemaining > 0f;
                animator.SetFloat("NormalAttackSpeed", speed);
                ComboStep = fiveIndex + 1;
                nextComboIndex = (fiveIndex + 1) % 5;
                NormalAttackStartFrame = 0;
                slashStepActive = false;
                comboGateRemaining = ComboChainDelay(fiveIndex);
                attackAnimationRemaining = fiveHitDurations[fiveIndex] / speed;
                comboWindowRemaining = attackAnimationRemaining + comboResetSeconds;
                if (restartingDuringRecovery) animator.CrossFadeInFixedTime(FiveHitAnimations[fiveIndex], .06f / speed, 0, 0f);
                else animator.Play(FiveHitAnimations[fiveIndex], 0, 0f);
                return;
            }
            if (!special && UsesNewNormalAttack)
            {
                NormalAttackStartFrame = newNormalChain ? normalRepeatStartFrame : 0;
                float offset = NormalAttackStartFrame / newNormalFrameRate;
                float speed = Mathf.Max(.1f, normalAttackSpeed);
                float remaining = Mathf.Max(.01f, (newNormalDuration - offset) / speed);
                animator.SetFloat("NormalAttackSpeed", speed);
                ComboStep = nextComboIndex + 1;
                nextComboIndex = (nextComboIndex + 1) % 4;
                newNormalChain = true;
                slashStepActive = false;
                comboGateRemaining = remaining;
                comboWindowRemaining = remaining + comboResetSeconds;
                attackAnimationRemaining = remaining;
                animator.Play(NewNormalAnimation, 0, offset / newNormalDuration);
                return;
            }
            int index = nextComboIndex;
            int state = special ? SpecialAnimation : ComboAnimations[index];
            bool hasCombo = animator != null && animator.runtimeAnimatorController != null &&
                animator.HasState(0, ComboAnimations[3]);
            float duration = special ? specialAnimationSeconds : hasCombo ? comboDurations[index] : normalAnimationSeconds;
            slashStepActive = !special && hasCombo && index == 0;
            slashStepTime = 0f;
            // Recovery extends the clip, not the first slash's authored travel curve.
            slashStepDuration = Mathf.Max(.01f, slashStepActive ? 42f / comboFrameRates[0] : duration);
            slashStepDirection = transform.forward;
            if (!special)
            {
                ComboStep = hasCombo ? index + 1 : 1;
                nextComboIndex = hasCombo ? (index + 1) % 4 : 0;
                comboGateRemaining = hasCombo ? ComboChainDelay(index) : Mathf.Max(attackCooldown, duration * .85f);
                comboWindowRemaining = duration + comboResetSeconds;
            }
            attackAnimationRemaining = duration;
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (!hasCombo && !special) state = AttackAnimation;
            if (!animator.HasState(0, state)) return;
            if (!special && index > 0) animator.CrossFadeInFixedTime(state, 0.045f, 0, 0f);
            else animator.Play(state, 0, 0f);
        }

        void LateUpdate()
        {
            if (IsDefeated)
            {
                // Sample explicitly so the last pose stays held even if the clip loops.
                deathElapsed = Mathf.Min(deathClipSeconds, deathElapsed + Time.deltaTime);
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    animator.Play(DeathAnimation, 0, Mathf.Clamp01(deathElapsed / Mathf.Max(.01f, deathClipSeconds)));
                    animator.Update(0f);
                }
                return;
            }
            if (IsAttackLocked || IsBlocking)
                transform.SetPositionAndRotation(attackPosition, attackRotation);
            blockRemaining = Mathf.Max(0f, blockRemaining - Time.deltaTime);
            attackAnimationRemaining = Mathf.Max(0f, attackAnimationRemaining - Time.deltaTime);
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (IsDashing || IsBlocking) return;
            // Movement is always responsive, including immediately after an attack.
            if (IsMoving || IsDashing) attackAnimationRemaining = 0f;
            if (attackAnimationRemaining > 0f) return;
            int state = IsMoving || IsDashing ? RunAnimation : IdleAnimation;
            if (!animator.HasState(0, state)) return;
            AnimatorStateInfo current = animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            if (current.fullPathHash != state) animator.CrossFadeInFixedTime(state, 0.12f);
        }

        void OnValidate()
        {
            chargeSeconds = Mathf.Max(0.05f, chargeSeconds);
            attackRange = Mathf.Max(0.1f, attackRange);
        }
    }
}
