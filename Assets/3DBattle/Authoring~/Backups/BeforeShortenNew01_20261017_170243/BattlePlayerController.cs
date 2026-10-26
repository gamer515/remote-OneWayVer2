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
        public int ComboStep { get; private set; }
        public int NormalAttackStartFrame { get; private set; }
        public bool UsesNewNormalAttack => newNormalDuration > 0f && animator != null && animator.HasState(0, NewNormalAnimation);
        float newNormalDuration, newNormalFrameRate = 60f;
        bool newNormalChain;
        static readonly int NewNormalAnimation = Animator.StringToHash("Base Layer.AttackNew");

        public enum PressState { Idle, Charging, Cancelled, Dash }
        public PressState State { get; private set; }
        public float Charge01 => State == PressState.Charging ? Mathf.Clamp01(heldSeconds / chargeSeconds) : 0f;
        public bool IsDashing => dashRemaining > 0f;
        public bool IsMoving => moving;
        public string Status { get; private set; } = "Right click to move";
        public bool BossInRange => boss != null && boss.IsAlive && boss.PlanarDistance(transform.position) <= attackRange;

        CharacterController motor;
        Vector3 destination, dashDirection;
        float heldSeconds, dashRemaining, nextDash, nextAttack;
        bool moving;
        float attackAnimationRemaining;
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
        static readonly int RunAnimation = Animator.StringToHash("Base Layer.Run");
        static readonly int AttackAnimation = Animator.StringToHash("Base Layer.Attack");
        static readonly int SpecialAnimation = Animator.StringToHash("Base Layer.Special");

        void Awake()
        {
            motor = GetComponent<CharacterController>();
            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == "voltslashnew01")
                {
                    newNormalDuration = clip.length;
                    newNormalFrameRate = Mathf.Max(1f, clip.frameRate);
                }
                for (int i = 0; i < comboDurations.Length; i++)
                    if (clip.name == "VoltSlash0" + (i + 1))
                    {
                        comboDurations[i] = clip.length;
                        comboFrameRates[i] = Mathf.Max(1f, clip.frameRate);
                    }
            }
        }

        void Update()
        {
            var health = GetComponent<BattlePlayerHealth>();
            if (health != null && !health.IsAlive) { CancelInput(); return; }
            Mouse mouse = Mouse.current;
            if (mouse == null || !Application.isFocused)
            {
                CancelInput();
                return;
            }
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            // The IMGUI controls are not part of EventSystem's raycast results.
            overUI |= feedback != null && feedback.IsPointerOverPanel(mouse.position.ReadValue());
            bool hasPoint = ResolvePointer(mouse.position.ReadValue(), out Vector3 point, out bool overBoss);
            ProcessPointerFrame(point, hasPoint, overBoss,
                !overUI && mouse.rightButton.wasPressedThisFrame,
                !overUI && mouse.leftButton.wasPressedThisFrame,
                mouse.leftButton.wasReleasedThisFrame, Time.deltaTime);
            Move(Time.deltaTime);
        }

        public bool ResolvePointer(Vector2 screen, out Vector3 point, out bool overBoss)
        {
            point = default;
            overBoss = false;
            if (viewCamera == null) return false;
            Ray ray = viewCamera.ScreenPointToRay(screen);
            // Ignore the player's own collider when aiming through its silhouette.
            RaycastHit[] hits = Physics.RaycastAll(ray, 200f, ~0, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closest) continue;
                closest = hit.distance;
                overBoss = boss != null && hit.collider.GetComponentInParent<BattleBoss>() == boss;
            }
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        // Shared by live mouse input and the editor's deterministic interaction checks.
        public void ProcessPointerFrame(Vector3 point, bool hasPoint, bool overBoss,
            bool rightDown, bool leftDown, bool leftUp, float deltaTime)
        {
            nextDash = Mathf.Max(0f, nextDash - deltaTime);
            nextAttack = Mathf.Max(0f, nextAttack - deltaTime);
            comboGateRemaining = Mathf.Max(0f, comboGateRemaining - deltaTime);
            comboWindowRemaining = Mathf.Max(0f, comboWindowRemaining - deltaTime);
            if (comboWindowRemaining <= 0f && !queuedNormalAttack) { nextComboIndex = 0; ComboStep = 0; }
            if (State == PressState.Charging)
            {
                if (!BossInRange) CancelCharge();
                else heldSeconds += deltaTime;
            }
            if (rightDown && hasPoint)
            {
                ResetCombo();
                if (State == PressState.Charging) CancelCharge();
                destination = point;
                destination.y = transform.position.y;
                moving = true;
                Status = "Moving";
            }
            if (leftDown && !rightDown && State == PressState.Idle && hasPoint)
            {
                if (IsDashing) State = PressState.Cancelled;
                else if (overBoss && BossInRange)
                {
                    // Eligibility is decided on press, not release. A new press in
                    // recovery starts slash one; a press before the gate may buffer.
                    if ((UsesNewNormalAttack || ComboStep == 1 || ComboStep == 2) && comboGateRemaining <= 0f && !queuedNormalAttack)
                        ResetCombo();
                    moving = false;
                    if (nextAttack > 0f && comboGateRemaining <= 0f) State = PressState.Cancelled;
                    else
                    {
                        State = PressState.Charging;
                        heldSeconds = 0f;
                        Status = "Charging... release to attack";
                        Face(boss.transform.position - transform.position);
                    }
                }
                else
                {
                    ResetCombo();
                    State = PressState.Dash;
                    if (nextDash <= 0f)
                    {
                        Vector3 direction = point - transform.position;
                        direction.y = 0f;
                        if (direction.sqrMagnitude > 0.01f)
                        {
                            dashDirection = direction.normalized;
                            dashRemaining = dashDuration;
                            nextDash = dashCooldown;
                            moving = false;
                            Face(dashDirection);
                            Status = "Dash";
                        }
                    }
                }
            }
            if (leftUp)
            {
                if (State == PressState.Charging && BossInRange)
                {
                    bool special = heldSeconds >= chargeSeconds;
                    if (special) { ResetCombo(); ExecuteAttack(true); }
                    else if (comboGateRemaining > 0f)
                    {
                        // Buffer one follow-up: fast clicking must not skip the visible swing.
                        queuedNormalAttack = true;
                        Status = "Next slash queued";
                    }
                    else ExecuteAttack(false);
                }
                State = PressState.Idle;
                heldSeconds = 0f;
            }
            if (queuedNormalAttack && comboGateRemaining <= 0f && State != PressState.Charging)
            {
                queuedNormalAttack = false;
                if (BossInRange && !moving && !IsDashing) ExecuteAttack(false);
                else ResetCombo();
            }
        }

        void ExecuteAttack(bool special)
        {
            Face(boss.transform.position - transform.position);
            PlayAttackAnimation(special);
            boss.TakeDamage(special ? specialDamage : normalDamage);
            feedback?.ShowAttack(boss.transform.position + Vector3.up, special);
            nextAttack = attackCooldown;
            Status = special ? "SPECIAL ATTACK" : UsesNewNormalAttack ? "Downward slash" : "Sword combo " + ComboStep + " / 4";
        }

        void ResetCombo()
        {
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
            ResetCombo();
            attackAnimationRemaining = 0f;
            State = PressState.Idle;
            heldSeconds = dashRemaining = 0f;
            moving = false;
        }

        public void ResetForBattle(Transform spawn)
        {
            CancelInput();
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
            Status = "Right click to move";
        }

        void OnApplicationFocus(bool focused) { if (!focused) CancelInput(); }
        void OnDisable() => CancelInput();

        public void Move(float deltaTime)
        {
            if (motor == null || !motor.enabled) return;
            Vector3 step = Vector3.zero;
            if (IsDashing)
            {
                float dashStep = Mathf.Min(deltaTime, dashRemaining);
                step = dashDirection * (dashSpeed * dashStep);
                dashRemaining = Mathf.Max(0f, dashRemaining - deltaTime);
            }
            else if (moving)
            {
                Vector3 direction = destination - transform.position;
                direction.y = 0f;
                if (direction.magnitude <= 0.08f) moving = false;
                else
                {
                    step = direction.normalized * Mathf.Min(moveSpeed * deltaTime, direction.magnitude);
                    Face(direction);
                }
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
            if ((flags & CollisionFlags.Sides) != 0) { moving = false; dashRemaining = 0f; slashStepActive = false; }
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
            if (UsesNewNormalAttack)
                return Mathf.Max(attackCooldown, (newNormalDuration - (index == 0 ? 0f : 6f / newNormalFrameRate)) / Mathf.Max(.1f, normalAttackSpeed));
            float point = index == 0 ? 42f / comboFrameRates[index]
                : index == 1 ? 36f / comboFrameRates[index] : comboDurations[index] * .85f;
            return Mathf.Max(attackCooldown, Mathf.Min(comboDurations[index], point));
        }

        void PlayAttackAnimation(bool special)
        {
            if (!special && UsesNewNormalAttack)
            {
                NormalAttackStartFrame = newNormalChain ? 6 : 0;
                float offset = NormalAttackStartFrame / newNormalFrameRate;
                float speed = Mathf.Max(.1f, normalAttackSpeed);
                float remaining = Mathf.Max(.01f, (newNormalDuration - offset) / speed);
                animator.SetFloat("NormalAttackSpeed", speed);
                ComboStep = nextComboIndex + 1;
                nextComboIndex = (nextComboIndex + 1) % 4;
                newNormalChain = true;
                slashStepActive = false;
                comboGateRemaining = Mathf.Max(attackCooldown, remaining);
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
            if (animator == null || animator.runtimeAnimatorController == null) return;
            attackAnimationRemaining = Mathf.Max(0f, attackAnimationRemaining - Time.deltaTime);
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
