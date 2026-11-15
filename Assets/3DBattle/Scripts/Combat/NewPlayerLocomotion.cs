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
        [Min(0), Tooltip("Outward clearance from the left forearm while standing, in world units.")]
        public float idleShieldClearance = .13f;
        [Header("Running shield")]
        [Tooltip("Turn the left wrist so the shield stays outside the torso while the arm swings.")]
        public bool correctRunningShield = true;
        [Range(0, 30), Tooltip("Lean the top of the running shield away from the body, in degrees.")]
        public float runningShieldOutwardTilt = 10f;
        [Min(0)] public float runningShieldClearance = .06f;
        [Header("Rolling shield")]
        public bool correctRollingShield = true;
        [Min(0), Tooltip("Extra outward clearance from the left forearm during a roll, in world units.")]
        public float rollingShieldClearance = .09f;
        [Min(.01f), Tooltip("Blend back to the normal shield pose after the roll-to-run/idle transition finishes.")]
        public float rollingShieldReturnDuration = .15f;
        [Min(0), Tooltip("Clearance while blending from movement back to the standing pose.")]
        public float shieldRecoveryClearance = .17f;
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
        Transform runningShieldTorso, correctedHand;
        Quaternion authoredHandRotation, appliedHandRotation;
        float runningShieldBlend;
        float rollingShieldBlend;
        bool handCorrectionApplied;
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
        void LateUpdate() => ApplyShieldPose(Time.deltaTime);
        void ApplyShieldPose(float delta)
        {
            RestoreCorrectedHand();
            CacheShieldAttachment();
            if (shieldTransform == null) return;
            bool idle = DefenseMode && !Parrying && !Rolling && !Running && (combo == null || !combo.IsActive);
            // Return to the authored attachment immediately for parry/other motions.
            idleShieldBlend = idle ? Mathf.MoveTowards(idleShieldBlend, 1, delta / .1f) : 0;
            shieldTransform.localRotation = shieldAttachmentRotation *
                Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(idleShieldRotationOffset), idleShieldBlend);
            shieldTransform.localPosition = shieldAttachmentPosition + idleShieldPositionOffset * idleShieldBlend;
            bool canCorrect = DefenseMode && !Parrying && (combo == null || !combo.IsActive);
            bool rolling = correctRollingShield && canCorrect && Rolling;
            if (rolling) rollingShieldBlend = 1;
            else if (!correctRollingShield || !canCorrect) rollingShieldBlend = 0;
            else if (player.animator == null || !player.animator.IsInTransition(0))
                rollingShieldBlend = Mathf.MoveTowards(rollingShieldBlend, 0, Mathf.Max(0, delta) / Mathf.Max(.01f, rollingShieldReturnDuration));
            bool running = correctRunningShield && canCorrect && Running && !Rolling;
            runningShieldBlend = running ? Mathf.MoveTowards(runningShieldBlend, 1, Mathf.Max(0, delta) / .1f) : 0;
            if (running && rollingShieldBlend > 0) runningShieldBlend = 1;
            if (!running && rollingShieldBlend <= 0)
            {
                if (idle) shieldTransform.position -= shieldTransform.right * idleShieldClearance;
                return;
            }
            if (shieldTransform.parent == null || shieldTransform.parent.name != "LeftHand") return;
            if (runningShieldTorso == null && player.animator != null)
                foreach (var bone in player.animator.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "UpperChest") { runningShieldTorso = bone; break; }
            if (runningShieldTorso == null) return;

            correctedHand = shieldTransform.parent;
            authoredHandRotation = correctedHand.localRotation;
            // Shield local X points inward and local Z follows its long edge.
            Vector3 toTorso = runningShieldTorso.position - correctedHand.position;
            Vector3 inward = Vector3.ProjectOnPlane(toTorso, Vector3.up);
            if (running && inward.sqrMagnitude > .0001f)
            {
                Quaternion runningRotation = Quaternion.LookRotation(Vector3.up, Vector3.Cross(Vector3.up, inward.normalized))
                    * Quaternion.AngleAxis(-runningShieldOutwardTilt, Vector3.up);
                correctedHand.rotation = Quaternion.Slerp(correctedHand.rotation,
                    runningRotation * Quaternion.Inverse(shieldAttachmentRotation), runningShieldBlend);
            }
            if (rollingShieldBlend > 0 && correctedHand.parent != null)
            {
                // Follow the forearm through the somersault instead of using world-up.
                Vector3 alongArm = (correctedHand.parent.position - correctedHand.position).normalized;
                inward = Vector3.ProjectOnPlane(toTorso, alongArm);
                if (inward.sqrMagnitude < .0001f) inward = Vector3.ProjectOnPlane(runningShieldTorso.right, alongArm);
                if (alongArm.sqrMagnitude > .0001f && inward.sqrMagnitude > .0001f)
                {
                    Quaternion rollingRotation = Quaternion.LookRotation(alongArm, Vector3.Cross(alongArm, inward.normalized));
                    correctedHand.rotation = Quaternion.Slerp(correctedHand.rotation,
                        rollingRotation * Quaternion.Inverse(shieldTransform.localRotation), rollingShieldBlend);
                }
            }
            float baseClearance = idle ? idleShieldClearance : running ? runningShieldClearance : 0;
            float recoveryClearance = idle ? shieldRecoveryClearance : rollingShieldClearance;
            shieldTransform.position -= shieldTransform.right * Mathf.Lerp(baseClearance, recoveryClearance, rollingShieldBlend);
            appliedHandRotation = correctedHand.localRotation;
            handCorrectionApplied = true;
        }
        void RestoreCorrectedHand()
        {
            // A newly sampled parry/roll pose takes precedence over the previous overlay.
            if (handCorrectionApplied && correctedHand != null && Quaternion.Angle(correctedHand.localRotation, appliedHandRotation) < .01f)
                correctedHand.localRotation = authoredHandRotation;
            handCorrectionApplied = false;
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
            combo.SetAttackHeld(!DefenseMode && !Rolling && !KnockedBack && (attackHeld || attackDown));
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
            if (correctRollingShield && DefenseMode && (Rolling || Running)) rollingShieldBlend = 1;
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
            RestoreCorrectedHand();
            if (shieldTransform != null)
            {
                shieldTransform.localRotation = shieldAttachmentRotation;
                shieldTransform.localPosition = shieldAttachmentPosition;
            }
            idleShieldBlend = 0;
            runningShieldBlend = 0;
            rollingShieldBlend = 0;
        }
    }
}
