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
        bool attackDashPlanned;
        BattlePlayerController player;
        CharacterController motor;
        NewPlayerLocomotion locomotion;
        NewPlayerAttackSet.Motion current;
        bool[] struck, effects;
        Vector3 direction;
        float groundY, previousHeight, retreated;
        Collider[] jumpObstacles;

        void Awake() { player = GetComponent<BattlePlayerController>(); motor = GetComponent<CharacterController>(); locomotion = GetComponent<NewPlayerLocomotion>(); }
        public void ProcessInput()
        {
            if (!readMouseInput) return;
            if (Mouse.current == null || !Application.isFocused) { locomotion?.ReleaseGuard(); return; }
            var follow = player.viewCamera != null ? player.viewCamera.GetComponent<BattleFollowCamera>() : null;
            if (follow != null && !follow.InputCaptured) { locomotion?.ReleaseGuard(); return; }
            if (locomotion != null && locomotion.enabled)
                locomotion.ProcessControlFrame(Mouse.current.scroll.ReadValue().y,
                    Mouse.current.leftButton.wasPressedThisFrame, Mouse.current.rightButton.wasPressedThisFrame,
                    Mouse.current.leftButton.isPressed);
            else if (Mouse.current.leftButton.wasPressedThisFrame) RequestAttack();
        }
        public void RequestAttack()
        {
            if (!Application.isPlaying || motions == null || player == null || player.IsDefeated) return;
            if (locomotion != null && locomotion.enabled)
            {
                if (locomotion.Rolling || locomotion.Parrying || locomotion.KnockedBack) return;
                locomotion.Stop();
            }
            if (IsActive) { if (!InPreparation && Frame > current.chainFrame) return; Queued = true; return; }
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
            player.animator.speed = 0;
            player.animator.Play(Animator.StringToHash("Base Layer." + current.clip.name), 0,
                Mathf.Clamp01(Frame / Mathf.Max(.001f, current.Frames)));
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
            if (locomotion != null && locomotion.enabled && locomotion.Tick(dt)) return;
            if (!IsActive || dt <= 0 || current == null) return;
            float remaining = dt * 30f * Mathf.Max(.1f, playbackSpeed);
            // Consume overshoot across transitions, even at low frame rates.
            for (int guard = 0; guard < 2048 && IsActive && remaining > .00001f; guard++)
            {
                float boundary = !InPreparation && Queued ? current.chainFrame : current.Frames;
                float next = Mathf.Min(boundary, Frame + Mathf.Min(remaining, .5f));
                if (!InPreparation && AttackIndex == 0)
                {
                    if (Frame < 13) next = Mathf.Min(next, 13);
                    else if (Frame < 27) next = Mathf.Min(next, 27);
                    else if (Frame < 30) next = Mathf.Min(next, 30);
                }
                float consumed = Mathf.Max(0, next - Frame);
                MoveBetween(Frame, next);
                float before = Frame;
                Frame = next;
                // Capture the latest camera heading at the forward dash, before its hit window.
                if (!InPreparation && AttackIndex == 0 && Frame >= 27 && !attackDashPlanned)
                    PlanAttackDash();
                Sample();
                if (!InPreparation) CheckDamage(before, next);
                remaining -= consumed;
                if (Frame >= boundary - .0001f)
                {
                    if (InPreparation) Begin(motions.attacks[AttackIndex], false);
                    else if (Queued)
                    {
                        Queued = false;
                        var link = motions.links[SequenceIndex];
                        SequenceIndex = (SequenceIndex + 1) % motions.order.Length;
                        Begin(link, true);
                    }
                    else { Finish(); break; }
                }
            }
        }
        void MoveBetween(float a, float b)
        {
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            float forward = current.forward.Evaluate(b) - current.forward.Evaluate(a);
            float lateral = current.sideways.Evaluate(b) - current.sideways.Evaluate(a);
            float y = current.jump.Evaluate(b) * jumpHeightMultiplier;
            if (!InPreparation && AttackIndex == 4)
            {
                forward = ActiveSkillDashDistance * (Mathf.Clamp01(b / 10f) - Mathf.Clamp01(a / 10f));
                // Authored lateral root drift must not bypass an assisted or suppressed dash.
                lateral = 0;
            }
            if (!InPreparation && AttackIndex == 0)
            {
                forward = b <= 13f ? -attackRetreatDistance * (b-a)/13f
                    : a >= 27f && a < 30f ? ActiveAttackDashDistance * (Mathf.Clamp01((b-27f)/3f)-Mathf.Clamp01((a-27f)/3f)) : 0;
                lateral = 0;
            }
            Vector3 start = transform.position;
            Vector3 step = direction * forward + side * lateral;
            // Animation is grounded; only code raises the CharacterController during the jump.
            step.y = y > 0 || previousHeight > 0 ? groundY + y - start.y : -2f * (b-a)/(30f*Mathf.Max(.1f,playbackSpeed));
            if (!InPreparation && AttackIndex == 5) MoveJump(step);
            else if (motor != null && motor.enabled) motor.Move(step); else transform.position += step;
            if (!InPreparation && AttackIndex == 0 && b <= 13f)
                retreated += Mathf.Max(0, -Vector3.Dot(transform.position-start, direction));
            previousHeight = y;
            player.SyncBodyPosition();
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
                    GetComponent<BattleSwordSlashEffect>()?.PlayAuthoredSwing((window.y-window.x)/(30f*playbackSpeed));
                }
                if (struck[i] || !player.BossInRange) continue;
                Vector3 toBoss = player.boss.transform.position-transform.position; toBoss.y=0;
                if (Vector3.Angle(direction,toBoss)>75f) continue;
                player.boss.TakeDamage(player.normalDamage);
                player.feedback?.ShowAttack(player.boss.transform.position+Vector3.up,false);
                struck[i]=true;
                DamageApplications++;
            }
        }
        void Finish()
        {
            IsActive = Queued = InPreparation = false;
            current = null;
            previousHeight = 0;
            if (player != null && player.animator != null && motions != null)
            {
                player.animator.speed = 1;
                player.animator.Play("Base Layer.Idle", 0, 0);
                player.animator.Update(0);
            }
        }
        public void Cancel()
        {
            if (!IsActive) return;
            if (IsActive && previousHeight > 0 && motor != null && motor.enabled)
                MoveJump(Vector3.up*(groundY-transform.position.y));
            Finish();
        }
        void OnDisable() { if (player != null) Cancel(); }
    }
}
