using UnityEngine;

namespace OneWay.Battle3D
{
    [DisallowMultipleComponent]
    public sealed class NewPlayerLocomotion : MonoBehaviour
    {
        public AnimationClip runClip, rollClip, parryClip;
        [Header("Held shield")]
        [Range(0, 1)] public float shieldHoldPose = .5f;
        [Min(0)] public float heldParryKnockbackDistance = 2f;
        [Min(.01f)] public float heldParryKnockbackDuration = .2f;
        [Tooltip("Local shield rotation offset used only while standing idle in defense mode.")]
        public Vector3 idleShieldRotationOffset;
        public Vector3 idleShieldPositionOffset;
        public float ShieldHeldSeconds { get; private set; }
        public bool KnockedBack => knockbackRemaining > 0;
        public bool DefenseMode { get; private set; }
        public bool Rolling { get; private set; }
        public bool Parrying { get; private set; }
        public bool Running { get; private set; }
        public float MotionTime { get; private set; }
        BattlePlayerController player;
        NewPlayerAttackCombo combo;
        CharacterController motor;
        Vector3 rollDirection;
        Vector3 knockbackVelocity;
        float knockbackRemaining;
        Transform shieldTransform;
        Quaternion shieldAttachmentRotation;
        Vector3 shieldAttachmentPosition;
        float idleShieldBlend;
        void Awake()
        {
            player = GetComponent<BattlePlayerController>();
            combo = GetComponent<NewPlayerAttackCombo>();
            motor = GetComponent<CharacterController>();
            CacheShieldAttachment();
            SetDefenseMode(false);
        }
        void CacheShieldAttachment()
        {
            var attached = player != null && player.shield != null ? player.shield.transform : null;
            if (attached == shieldTransform) return;
            shieldTransform = attached;
            if (attached != null)
            {
                shieldAttachmentRotation = attached.localRotation;
                shieldAttachmentPosition = attached.localPosition;
            }
            idleShieldBlend = 0;
        }
        void LateUpdate()
        {
            CacheShieldAttachment();
            if (shieldTransform == null) return;
            bool idle = DefenseMode && !Parrying && !Rolling && !Running && (combo == null || !combo.IsActive);
            // Return to the authored attachment immediately for parry/other motions.
            idleShieldBlend = idle ? Mathf.MoveTowards(idleShieldBlend, 1, Time.deltaTime / .1f) : 0;
            shieldTransform.localRotation = shieldAttachmentRotation *
                Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(idleShieldRotationOffset), idleShieldBlend);
            shieldTransform.localPosition = shieldAttachmentPosition + idleShieldPositionOffset * idleShieldBlend;
        }
        public void SetDefenseMode(bool defense)
        {
            if (player == null) player = GetComponent<BattlePlayerController>();
            DefenseMode = defense;
            if (player.shield != null) player.shield.SetActive(defense);
            if (!defense && Parrying) ReleaseGuard();
        }
        public void ProcessControlFrame(float scroll, bool attackDown, bool rollDown, bool attackHeld = false)
        {
            if (player == null || player.IsDefeated) return;
            if (scroll != 0)
            {
                SetDefenseMode(scroll < 0);
                if (DefenseMode && combo.IsActive) combo.Cancel();
            }
            if (rollDown && !Rolling) StartRoll();
            else if (!Rolling)
            {
                if (DefenseMode)
                {
                    if (attackHeld || attackDown) { if (!Parrying) StartParry(); }
                    else ReleaseGuard();
                }
                else if (attackDown && !KnockedBack) { Stop(); combo.RequestAttack(); }
            }
        }
        void FaceForward()
        {
            var forward = player.CameraForward;
            if (forward.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(forward);
        }
        public void StartRoll()
        {
            if (rollClip == null || player.IsDefeated || Rolling) return;
            combo.Cancel();
            Running = Parrying = false;
            ShieldHeldSeconds = 0;
            knockbackRemaining = 0;
            Rolling = true;
            MotionTime = 0;
            FaceForward();
            rollDirection = transform.forward;
            Sample(rollClip, 0);
        }
        public void StartParry()
        {
            if (parryClip == null || player.IsDefeated || Rolling || Parrying) return;
            combo.Cancel();
            Running = false;
            Parrying = true;
            ShieldHeldSeconds = 0;
            MotionTime = 0;
            SetDefenseMode(true);
            FaceForward();
            Sample(parryClip, 0);
        }
        void Sample(AnimationClip clip, float normalized)
        {
            player.animator.speed = 0;
            player.animator.Play("Base Layer." + clip.name, 0, Mathf.Clamp01(normalized));
            player.animator.Update(0);
        }
        void Move(Vector3 step, float dt)
        {
            if (motor != null && motor.enabled) motor.Move(step + Vector3.down * (2f * dt));
            player.SyncBodyPosition();
        }
        public bool Tick(float dt)
        {
            bool recoiling = KnockedBack;
            if (recoiling)
            {
                float used = Mathf.Min(Mathf.Max(0, dt), knockbackRemaining);
                Move(knockbackVelocity * used, used);
                knockbackRemaining = Mathf.Max(0, knockbackRemaining - used);
            }
            if (Rolling)
            {
                float duration = rollClip.length / Mathf.Max(.1f, player.rollAnimationSpeed);
                float used = Mathf.Min(dt, Mathf.Max(0, duration - MotionTime));
                MotionTime += used;
                Move(rollDirection * (player.rollDistance * used / duration), used);
                Sample(rollClip, MotionTime / duration);
                if (MotionTime >= duration - .00001f)
                {
                    Rolling = false;
                    Running = true;
                    player.animator.speed = 1;
                    player.animator.CrossFadeInFixedTime("Base Layer." + runClip.name, .1f);
                    if (dt > used) Tick(dt - used);
                }
                return true;
            }
            if (Parrying)
            {
                float duration = parryClip.length / Mathf.Max(.1f, player.shieldAnimationSpeed);
                ShieldHeldSeconds += Mathf.Max(0, dt);
                MotionTime = Mathf.Min(duration * shieldHoldPose, MotionTime + Mathf.Max(0, dt));
                Sample(parryClip, MotionTime / duration);
                Move(Vector3.zero, dt);
                return true;
            }
            if (Running)
            {
                FaceForward();
                Move(transform.forward * (player.moveSpeed * dt), dt);
                return true;
            }
            return recoiling;
        }
        public void ReleaseGuard()
        {
            if (Parrying) Stop(false);
        }
        public void OnSuccessfulParry()
        {
            if (!enabled || !Parrying || ShieldHeldSeconds < 1f || heldParryKnockbackDistance <= 0) return;
            float duration = Mathf.Max(.01f, heldParryKnockbackDuration);
            knockbackVelocity = -Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized
                * (heldParryKnockbackDistance / duration);
            knockbackRemaining = duration;
        }
        public void Stop(bool cancelKnockback = true)
        {
            bool wasMoving = Rolling || Parrying || Running;
            Rolling = Parrying = Running = false;
            MotionTime = 0;
            ShieldHeldSeconds = 0;
            if (cancelKnockback) knockbackRemaining = 0;
            if (wasMoving && player != null && player.animator != null)
            {
                player.animator.speed = 1;
                player.animator.CrossFadeInFixedTime("Base Layer.Idle", .1f);
            }
        }
        void OnDisable()
        {
            Stop();
            if (shieldTransform != null)
            {
                shieldTransform.localRotation = shieldAttachmentRotation;
                shieldTransform.localPosition = shieldAttachmentPosition;
            }
            idleShieldBlend = 0;
        }
    }
}
