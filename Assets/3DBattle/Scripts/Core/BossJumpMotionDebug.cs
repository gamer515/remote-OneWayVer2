using UnityEngine;

namespace OneWay.Battle3D
{
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(BattleBoss), typeof(BossActionPriority))]
    public sealed class BossJumpMotionDebug : MonoBehaviour
    {
        public Battle3DSceneController battle;
        public BossJumpTimingDebug jump12Timing;
        public BossJump17TimingDebug jump17Timing;
        [Tooltip("17 is available only after its takeoff/landing times are configured.")]
        public bool useJump17;
        public AnimationClip jump17Clip;
        public float jump17Takeoff = -1f, jump17Landing = -1f;
        [Min(.05f)] public float playbackSpeed = 1f;
        [Min(.1f)] public float landingGap = 2f;
        [Tooltip("Added root height. Zero uses only the vertical motion already in the clip.")]
        [Min(0f)] public float additionalHeight;
        public LayerMask groundMask = ~0;
        public GameObject jumpDustPrefab;
        [Min(.01f)] public float jumpDustScale = 2f;
        public bool IsRunning { get; private set; }
        public bool StopAfterLanding => GetComponent<BossActionController>().StopQueued;
        public float ClipTime { get; private set; }
        public string Status { get; private set; } = "Ready";
        public Vector3 LandingPosition { get; private set; }
        public bool HasLandingPoint { get; private set; }
        BattleBoss boss;
        BossActionPriority priority;
        BattleStage stage;
        Animator animator;
        AnimationClip clip;
        float takeoff, landing, speed, height, gap, oldSpeed;
        bool oldRootMotion, oldPatrol;
        Vector3 departure;
        Quaternion heading;
        void Awake() { boss = GetComponent<BattleBoss>(); priority = GetComponent<BossActionPriority>(); }
        public void Begin() => GetComponent<BossActionController>().RequestJump(useJump17);
        public bool CanExecute(bool jump17)
        {
            var timing = jump17 ? jump17Timing : jump12Timing;
            var c = timing != null ? timing.clip : jump17 ? jump17Clip : null;
            float a = timing != null ? timing.takeoffTime : jump17 ? jump17Takeoff : -1f;
            float z = timing != null ? timing.landingTime : jump17 ? jump17Landing : -1f;
            var b = GetComponent<BattleBoss>();
            string state = jump17 ? "Base Layer.Jump_with_Arms_Open_Boss1" : "Base Layer.Basic_Jump";
            return battle != null && battle.CurrentStage != null && battle.CurrentStage.boss == b && c != null &&
                a >= 0f && z > a && z <= c.length && b.animator != null && b.animator.HasState(0, Animator.StringToHash(state));
        }
        internal void BeginExecution()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsRunning || battle == null || battle.CurrentStage.boss != boss) return;
            if (!boss.IsAlive || priority.ControlsAnimation) { Status = "Death/hit reaction has priority"; return; }
            var timing = useJump17 ? jump17Timing : jump12Timing;
            clip = timing != null ? timing.clip : useJump17 ? jump17Clip : null;
            takeoff = timing != null ? timing.takeoffTime : useJump17 ? jump17Takeoff : -1f;
            landing = timing != null ? timing.landingTime : useJump17 ? jump17Landing : -1f;
            string state = useJump17 ? "Base Layer.Jump_with_Arms_Open_Boss1" : "Base Layer.Basic_Jump";
            animator = boss.animator;
            if (clip == null || takeoff < 0f || landing <= takeoff || landing > clip.length ||
                animator == null || !animator.HasState(0, Animator.StringToHash(state)))
            { Status = "Configure clip and valid takeoff/landing times first"; return; }
            GetComponent<BossPatternRunner>()?.StopPattern();
            stage = battle.CurrentStage;
            speed = Mathf.Max(.05f, playbackSpeed); height = Mathf.Max(0f, additionalHeight); gap = Mathf.Max(.1f, landingGap);
            oldSpeed = animator.speed; oldRootMotion = animator.applyRootMotion; oldPatrol = boss.patrolEnabled;
            boss.patrolEnabled = false; boss.PatternControlsAnimation = true;
            departure = transform.position;
            Vector3 forward = battle.player.transform.position - departure; forward.y = 0f;
            heading = forward.sqrMagnitude > .001f ? Quaternion.LookRotation(forward) : transform.rotation;
            transform.rotation = heading;
            ClipTime = 0f; HasLandingPoint = false; IsRunning = true;
            animator.applyRootMotion = false; animator.speed = 0f;
            animator.Play(state, 0, 0f); animator.Update(0f);
            Status = "Preparing";
        }
        void Update() => Tick(Time.deltaTime);
        public void Tick(float delta)
        {
            if (!IsRunning) return;
            if (battle.CurrentStage != stage) { CancelForPriority(); return; }
            float previous = ClipTime;
            ClipTime = Mathf.Min(clip.length, ClipTime + Mathf.Max(0f, delta) * speed);
            if (!HasLandingPoint && ClipTime >= takeoff)
            {
                if (!FindLanding()) { Finish("No ground at landing point"); return; }
                priority.EnterAirborne(); HasLandingPoint = true; Status = "Airborne (highest priority)";
                SpawnJumpDust(departure, "Takeoff");
            }
            // Sample the Animator at clip time independently of the movement clock.
            string state = useJump17 ? "Base Layer.Jump_with_Arms_Open_Boss1" : "Base Layer.Basic_Jump";
            animator.Play(state, 0, Mathf.Min(.999999f, ClipTime / clip.length));
            animator.Update(0f);
            if (HasLandingPoint)
            {
                float t = Mathf.InverseLerp(takeoff, landing, ClipTime);
                transform.position = Vector3.Lerp(departure, LandingPosition, t) + Vector3.up * (4f * height * t * (1f - t));
            }
            else transform.position = departure;
            transform.rotation = heading;
            if (previous < landing && ClipTime >= landing)
            {
                transform.position = LandingPosition;
                SpawnJumpDust(LandingPosition, "Landing");
                priority.Land(); // Death is resolved before hit reaction at this exact boundary.
                if (!IsRunning) return;
                Status = "Landing recovery";
                if (StopAfterLanding) { Finish("Stopped after landing"); return; }
            }
            if (ClipTime >= clip.length) Finish("Completed");
        }
        void SpawnJumpDust(Vector3 position, string phase)
        {
            if (jumpDustPrefab == null) return;
            RaycastHit? ground = null;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * 20f, Vector3.down, 100f, groundMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < .5f || hit.collider.GetComponentInParent<BattleBoss>() != null || hit.collider.GetComponentInParent<BattlePlayerController>() != null) continue;
                if (!ground.HasValue || hit.distance < ground.Value.distance) ground = hit;
            }
            if (ground.HasValue) position.y = ground.Value.point.y;
            var dust = Instantiate(jumpDustPrefab, position + Vector3.up * .02f, Quaternion.identity);
            dust.name = "Boss Jump Dust " + phase;
            dust.transform.localScale = jumpDustPrefab.transform.localScale * jumpDustScale;
            // Preserve world size even when the boss or arena uses a scaled transform.
            if (stage != null) dust.transform.SetParent(stage.transform, true);
            foreach (var particles in dust.GetComponentsInChildren<ParticleSystem>()) particles.Play(false);
            Destroy(dust, 5f);
        }
        bool FindLanding()
        {
            departure = transform.position;
            var player = battle.player.transform;
            // Approach along the boss-to-player line, regardless of player facing.
            // Never cross the player or move away when already inside the landing gap.
            Vector3 forward = player.position - departure;
            forward.y = 0f;
            float distance = forward.magnitude;
            if (distance > .001f) forward /= distance;
            else forward = transform.forward;
            Vector3 candidate = departure + forward * Mathf.Max(0f, distance - gap);
            var arenaBody = GetComponent<BattleArenaBody>();
            if (arenaBody != null) candidate = arenaBody.Constrain(candidate);
            RaycastHit? ground = null;
            foreach (var hit in Physics.RaycastAll(candidate + Vector3.up * 20f, Vector3.down, 100f, groundMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < .5f || hit.collider.GetComponentInParent<BattleBoss>() != null || hit.collider.GetComponentInParent<BattlePlayerController>() != null) continue;
                if (!ground.HasValue || hit.distance < ground.Value.distance) ground = hit;
            }
            if (!ground.HasValue) return false;
            LandingPosition = new Vector3(candidate.x, ground.Value.point.y, candidate.z);
            if (distance > .001f) heading = Quaternion.LookRotation(forward);
            return true;
        }
        public void RequestStop() => GetComponent<BossActionController>().RequestStop();
        // Only lifecycle/reset or higher-priority reactions use this immediate cancellation.
        internal void CancelForPriority() { if (IsRunning) Finish("Interrupted"); }
        void Finish(string message)
        {
            if (!IsRunning) return;
            IsRunning = false; Status = message;
            if (animator != null) { animator.speed = oldSpeed; animator.applyRootMotion = oldRootMotion; }
            if (boss != null)
            {
                boss.patrolEnabled = oldPatrol; boss.PatternControlsAnimation = false; boss.RefreshAnimation();
            }
            GetComponent<BossActionController>()?.ExecutorEnded(this, message == "Completed");
        }
        public void ResetToSpawn()
        {
            if (!Application.isPlaying || battle == null) return;
            if (IsRunning && priority.Airborne) { Status = "Land first before resetting"; return; }
            CancelForPriority();
            boss.ResetAt(battle.FindStage(1).bossSpawn);
            ClipTime = 0f; HasLandingPoint = false; Status = "Reset (health restored)";
        }
        void OnDisable()
        {
            CancelForPriority();
            if (priority != null) priority.ResetPriority();
        }
        void OnDrawGizmosSelected()
        {
            if (!HasLandingPoint) return;
            Gizmos.color = Color.cyan;
            Vector3 prev = departure;
            for (int i = 1; i <= 32; i++)
            {
                float t = i / 32f;
                Vector3 point = Vector3.Lerp(departure, LandingPosition, t) + Vector3.up * (4f * height * t * (1f - t));
                Gizmos.DrawLine(prev, point); prev = point;
            }
            Gizmos.DrawWireSphere(LandingPosition, .2f);
        }
    }
}
