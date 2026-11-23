using UnityEngine;

namespace OneWay.Battle3D
{
    [DefaultExecutionOrder(200)]
    public sealed class B2SwordSlash : MonoBehaviour
    {
        public BattleBoss boss;
        public SkinnedMeshRenderer body;
        public Transform swordGrip;
        [Tooltip("Skill03 참격의 방향을 계산할 칼날입니다. 로컬 Z축이 검끝을 향합니다.")]
        public Transform swordBlade;
        [Tooltip("날아가는 참격 프리팹 (연기 포함)")]
        public GameObject slashPrefab;
        [Tooltip("제자리에서 재생되는 참격 프리팹 (연기 없음)")]
        public GameObject stationarySlashPrefab;
        [Tooltip("켜면 고정형 참격, 끄면 기존 날아가는 참격을 사용합니다.")]
        public bool useStationarySlash;
        [Min(.05f)] public float stationaryDuration = .55f;
        [Min(.01f)] public float heightMultiplier = 2f;
        [Tooltip("참격 이동 속도 (유닛/초)")]
        [Min(0)] public float travelSpeed = 20f;
        [Tooltip("생성된 위치에서 기다린 뒤 이동을 시작하는 시간 (초)")]
        [Min(0)] public float launchDelay = .2f;
        [Tooltip("대기가 끝난 뒤 날아가는 시간 (초)")]
        [Min(.01f)] public float lifetime = 2f;
        [Header("Trail Length Multipliers (1 = original length)")]
        [Tooltip("맨 위 연기 길이 배율")][Min(0)] public float topTrailLength = 1f;
        [Tooltip("위와 중간 사이 연기 길이 배율")][Min(0)] public float upperMiddleTrailLength = .7f;
        [Tooltip("중앙 연기 길이 배율")][Min(0)] public float centerTrailLength = .4f;
        [Tooltip("아래와 중간 사이 연기 길이 배율")][Min(0)] public float lowerMiddleTrailLength = .7f;
        [Tooltip("맨 아래 연기 길이 배율")][Min(0)] public float bottomTrailLength = 1f;
        public int EmittedCount { get; private set; }
        public float BossHeight => body != null && body.sharedMesh != null
            ? body.sharedMesh.bounds.size.y * Mathf.Abs(body.transform.lossyScale.y) : 1f;
        BattleEffectPool stationaryPool, flyingPool;
        GameObject cachedBasisPrefab;
        Quaternion cachedBasis;
        Vector3 cachedGripOffset;
        Animator animator;
        bool samplingSkill03, skill03SlashPending, hasBladeSample;
        Vector3 previousBladeDirection, accumulatedSwingNormal;

        void Awake() => animator = GetComponent<Animator>();
        void Start() => PreparePools();
        void PreparePools()
        {
            if (stationarySlashPrefab && (stationaryPool == null || stationaryPool.Prefab != stationarySlashPrefab))
            { stationaryPool?.Dispose(); stationaryPool = new BattleEffectPool(stationarySlashPrefab, transform, BattleEffectPool.Kind.Particles, 2, 4); }
            if (slashPrefab && (flyingPool == null || flyingPool.Prefab != slashPrefab))
            { flyingPool?.Dispose(); flyingPool = new BattleEffectPool(slashPrefab, transform, BattleEffectPool.Kind.FlyingSlash, 4, 8); }
        }

        // Animation events can arrive before the final bone pose is applied.
        // Sample and emit in LateUpdate so the effect uses the visible sword pose.
        public void EmitB2Skill03Slash() => skill03SlashPending = true;

        void LateUpdate()
        {
            if (animator == null) animator = GetComponent<Animator>();
            bool inSkill03 = animator != null &&
                (animator.GetCurrentAnimatorStateInfo(0).IsName("Skill03") ||
                (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("Skill03")));
            if (inSkill03 && !samplingSkill03)
            {
                hasBladeSample = false;
                accumulatedSwingNormal = Vector3.zero;
            }
            samplingSkill03 = inSkill03;
            bool fullSkill03Pose = inSkill03 && animator != null && !animator.IsInTransition(0);
            if ((fullSkill03Pose || skill03SlashPending) && swordBlade != null)
            {
                Vector3 direction = swordBlade.forward;
                if (hasBladeSample)
                    accumulatedSwingNormal += Vector3.Cross(previousBladeDirection, direction);
                previousBladeDirection = direction;
                hasBladeSample = true;
            }
            if (!skill03SlashPending) return;
            skill03SlashPending = false;
            if (swordBlade == null) { EmitB2SwordSlash(); return; }

            var prefab = useStationarySlash ? stationarySlashPrefab : slashPrefab;
            if (prefab == null) return;
            Vector3 radial = swordBlade.forward.normalized;
            Vector3 normal = Vector3.ProjectOnPlane(accumulatedSwingNormal, radial);
            if (normal.sqrMagnitude < .0001f)
                normal = Vector3.ProjectOnPlane(boss != null ? boss.transform.up : Vector3.up, radial);
            if (normal.sqrMagnitude < .0001f) normal = Vector3.Cross(radial, swordBlade.right);
            normal.Normalize();
            Quaternion rotation = Quaternion.LookRotation(radial, normal) * Quaternion.Inverse(SlashMeshBasis(prefab));
            EmitSlash(prefab, rotation, radial, true);
        }

        Quaternion SlashMeshBasis(GameObject prefab)
        {
            if (cachedBasisPrefab == prefab) return cachedBasis;
            cachedBasisPrefab = prefab; cachedBasis = Quaternion.identity; cachedGripOffset = Vector3.zero;
            var core = prefab.GetComponentInChildren<ParticleSystem>();
            if (core == null) return Quaternion.identity;
            cachedGripOffset = prefab.transform.InverseTransformPoint(core.transform.position);
            var main = core.main;
            Vector3 angles = main.startRotation3D
                ? new Vector3(main.startRotationX.constant, main.startRotationY.constant, main.startRotationZ.constant)
                : new Vector3(0, 0, main.startRotation.constant);
            Quaternion meshRotation = Quaternion.Inverse(prefab.transform.rotation) * core.transform.rotation *
                Quaternion.Euler(angles * Mathf.Rad2Deg);
            // The Golden Slash mesh lies in XY; its curved outer edge faces (-X, +Y).
            cachedBasis = Quaternion.LookRotation(meshRotation * new Vector3(-1, 1, 0).normalized,
                meshRotation * Vector3.forward);
            return cachedBasis;
        }

        // Called by each authored strike event.
        public void EmitB2SwordSlash()
        {
            var prefab = useStationarySlash ? stationarySlashPrefab : slashPrefab;
            if (!isActiveAndEnabled || prefab == null || boss == null || !boss.IsAlive) return;
            Vector3 forward = Vector3.ProjectOnPlane(boss.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            EmitSlash(prefab, Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(20f, 0f, 0f), forward, false);
        }

        void EmitSlash(GameObject prefab, Quaternion rotation, Vector3 travelDirection, bool alignCoreToGrip)
        {
            if (!isActiveAndEnabled || prefab == null || boss == null || !boss.IsAlive) return;
            Vector3 origin = swordGrip != null ? swordGrip.position : transform.position + Vector3.up * BossHeight * .5f;
            float scale = BossHeight * heightMultiplier;
            if (alignCoreToGrip)
            {
                SlashMeshBasis(prefab);
                origin -= rotation * (cachedGripOffset * scale);
            }
            PreparePools();
            var slash = (useStationarySlash ? stationaryPool : flyingPool).Rent(origin, rotation).Effect;
            slash.name = useStationarySlash ? "B2 Stationary Golden Slash" : "B2 Flying Golden Slash";
            slash.transform.localScale = Vector3.one * scale;
            var trails = slash.Trails;
            if (trails != null)
                trails.ApplyLengths(topTrailLength, upperMiddleTrailLength, centerTrailLength,
                    lowerMiddleTrailLength, bottomTrailLength);
            if (useStationarySlash)
            {
                float duration = Mathf.Max(.05f, stationaryDuration);
                slash.Activate(); slash.FitSwing(duration);
                slash.ReleaseAfter(duration + .05f);
            }
            else { slash.Activate(); slash.Flight.Initialize(travelDirection, travelSpeed, lifetime, launchDelay); }
            EmittedCount++;
        }
        void OnDisable()
        {
            skill03SlashPending = samplingSkill03 = hasBladeSample = false;
            accumulatedSwingNormal = Vector3.zero;
            stationaryPool?.ReleaseAll(); flyingPool?.ReleaseAll();
        }
        void OnDestroy() { stationaryPool?.Dispose(); flyingPool?.Dispose(); }
    }
}
