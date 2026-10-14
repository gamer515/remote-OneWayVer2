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
        static readonly int IdleAnimation = Animator.StringToHash("Base Layer.Idle");
        static readonly int RunAnimation = Animator.StringToHash("Base Layer.Run");
        static readonly int AttackAnimation = Animator.StringToHash("Base Layer.Attack");
        static readonly int SpecialAnimation = Animator.StringToHash("Base Layer.Special");

        void Awake() => motor = GetComponent<CharacterController>();

        void Update()
        {
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
            if (State == PressState.Charging)
            {
                if (!BossInRange) CancelCharge();
                else heldSeconds += deltaTime;
            }
            if (rightDown && hasPoint)
            {
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
                    moving = false;
                    if (nextAttack > 0f) State = PressState.Cancelled;
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
                    Face(boss.transform.position - transform.position);
                    PlayAttackAnimation(special);
                    boss.TakeDamage(special ? specialDamage : normalDamage);
                    feedback?.ShowAttack(boss.transform.position + Vector3.up, special);
                    nextAttack = attackCooldown;
                    Status = special ? "SPECIAL ATTACK" : "Normal attack";
                }
                State = PressState.Idle;
                heldSeconds = 0f;
            }
        }

        void CancelCharge()
        {
            State = PressState.Cancelled;
            heldSeconds = 0f;
            Status = "Charge cancelled";
        }

        public void CancelInput()
        {
            State = PressState.Idle;
            heldSeconds = dashRemaining = 0f;
            moving = false;
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
            CollisionFlags flags = motor.Move(step + Vector3.down * (2f * deltaTime));
            if ((flags & CollisionFlags.Sides) != 0) { moving = false; dashRemaining = 0f; }
        }

        void Face(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
        }

        void PlayAttackAnimation(bool special)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            int state = special ? SpecialAnimation : AttackAnimation;
            if (!animator.HasState(0, state)) return;
            animator.Play(state, 0, 0f);
            attackAnimationRemaining = special ? specialAnimationSeconds : normalAnimationSeconds;
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
