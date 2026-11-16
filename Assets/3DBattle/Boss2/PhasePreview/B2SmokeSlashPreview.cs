using UnityEngine;

namespace OneWay.Battle3D
{
    // Visual-only test of one flying slash emerging from a stationary cloud.
    public sealed class B2SmokeSlashPreview : MonoBehaviour
    {
        public Transform cloudRoot;
        public ParticleSystem[] cloudLayers;
        public Transform slashRoot;
        public ParticleSystem[] slashLayers;
        [Header("Cloud")]
        [Min(0)] public float smokeStart = .1f;
        [Min(.1f)] public float smokeEnd = 5.6f;
        [Tooltip("지정한 시점의 연기 크기와 농도를 유지하면서 입자들이 천천히 떠다닙니다.")]
        public bool holdCloudShape = true;
        [Tooltip("유지할 연기 모양의 미리보기 시간(초)입니다. Smoke End보다 작게 설정하세요.")]
        [Min(0)] public float cloudHoldTime = 3.6f;
        [Tooltip("퍼진 연기가 둥둥 떠다니는 거리입니다. 0이면 움직이지 않습니다.")]
        [Min(0)] public float cloudFloatDistance = .5f;
        [Tooltip("연기가 떠다니는 속도입니다. 0이면 움직이지 않습니다.")]
        [Min(0)] public float cloudFloatSpeed = .7f;
        [Header("Flying slash")]
        [Min(0)] public float slashSpawnTime = 1.6f;
        [Min(0)] public float launchDelay = .2f;
        [Min(0)] public float travelSpeed = 40;
        [Min(.01f)] public float flightSeconds = 2;
        public Vector3 slashOrigin = new Vector3(0, 3.4f, 0);
        public Vector3 slashEuler = new Vector3(20, 0, 0);
        [Min(.01f)] public float slashScale = 13.6f;
        [Header("Preview")]
        public bool playOnStart = true;
        public bool loop = true;
        [Min(0)] public float repeatPause = .9f;
        [Range(.1f, 2)] public float playbackSpeed = 1;

        float elapsed;
        bool playing;
        ParticleSystem.Particle[] cloudParticles;
        public float PreviewTime { get; private set; }
        public float TrailFadeSeconds
        {
            get
            {
                float duration = 0;
                if (slashLayers != null)
                    foreach (var p in slashLayers) if (p) duration = Mathf.Max(duration, p.main.startLifetime.constantMax);
                return duration;
            }
        }
        public float Duration => Mathf.Max(smokeEnd, slashSpawnTime + launchDelay + flightSeconds + TrailFadeSeconds) + repeatPause;
        void Start() { if (playOnStart) PlayPreview(); }
        void Update()
        {
            if (!playing) return;
            elapsed += Time.deltaTime * playbackSpeed;
            if (loop) elapsed = Mathf.Repeat(elapsed, Duration);
            else if (elapsed >= Duration) { elapsed = Duration; playing = false; }
            SamplePreview(elapsed);
        }
        void OnDisable() { StopPreview(); }
        public void PlayPreview() { playing = true; elapsed = 0; SamplePreview(0); }
        public void StopPreview() { playing = false; elapsed = 0; SamplePreview(0); }

        public void SamplePreview(float time)
        {
            PreviewTime = Mathf.Clamp(time, 0, Duration);
            SampleCloud(PreviewTime);
            SampleSlash(PreviewTime);
        }

        // Other isolated previews can share the same cloud without playing a slash.
        public void SampleCloud(float time)
        {
            time = Mathf.Max(0, time);
            if (cloudLayers != null)
            {
                // Keep particle size/opacity at the chosen age, with bounded motion afterward.
                float cloudTime = holdCloudShape ? Mathf.Min(time, cloudHoldTime) : time;
                for (int i = 0; i < cloudLayers.Length; i++)
                {
                    var ps = cloudLayers[i]; if (!ps) continue;
                    float age = cloudTime - smokeStart - i * .018f;
                    var main = ps.main; main.startLifetime = Mathf.Max(.1f, smokeEnd - smokeStart - i * .018f);
                    if (age <= 0 || cloudTime >= smokeEnd) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    else
                    {
                        ps.Simulate(age, false, true, false);
                        if (holdCloudShape && time > cloudHoldTime)
                            FloatCloud(ps, i, time - cloudHoldTime);
                    }
                }
            }
        }

        void SampleSlash(float time)
        {
            if (!slashRoot) return;
            float slashAge = time - slashSpawnTime;
            float emissionEnd = Mathf.Max(0, launchDelay) + Mathf.Max(.01f, flightSeconds);
            bool visible = slashAge >= 0 && slashAge < emissionEnd + TrailFadeSeconds;
            slashRoot.localPosition = slashOrigin + Vector3.forward * (Mathf.Clamp(slashAge - launchDelay, 0, flightSeconds) * travelSpeed);
            slashRoot.localRotation = Quaternion.Euler(slashEuler);
            slashRoot.localScale = Vector3.one * slashScale;
            slashRoot.gameObject.SetActive(visible);
            if (slashLayers == null) return;
            foreach (var ps in slashLayers)
            {
                if (!ps) continue;
                if (!visible) { ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); continue; }
                ps.Simulate(Mathf.Min(slashAge, emissionEnd), false, true, false);
                if (slashAge > emissionEnd)
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    ps.Simulate(slashAge - emissionEnd, false, false, false);
                }
            }
        }

        public void FloatCloud(ParticleSystem ps, int layer, float heldTime)
        {
            if (cloudFloatDistance <= 0 || cloudFloatSpeed <= 0) return;
            int count = ps.particleCount;
            if (cloudParticles == null || cloudParticles.Length < count)
                cloudParticles = new ParticleSystem.Particle[Mathf.Max(count, 64)];
            count = ps.GetParticles(cloudParticles);
            float ramp = Mathf.SmoothStep(0, 1, heldTime / .8f);
            float t = heldTime * cloudFloatSpeed;
            var main = ps.main;
            Transform space = main.simulationSpace == ParticleSystemSimulationSpace.Local ? ps.transform
                : main.simulationSpace == ParticleSystemSimulationSpace.Custom ? main.customSimulationSpace : null;
            Transform cloud = cloudRoot ? cloudRoot : transform;
            for (int i = 0; i < count; i++)
            {
                var particle = cloudParticles[i];
                float phase = (particle.randomSeed % 10007u) / 10007f * Mathf.PI * 2 + layer * 1.73f;
                var drift = new Vector3(Mathf.Sin(t * .8f + phase) * .65f,
                    Mathf.Sin(t * .62f + phase * 1.37f), Mathf.Cos(t * .74f + phase * 1.91f) * .5f);
                var worldDrift = cloud.TransformVector(drift * (cloudFloatDistance * ramp));
                particle.position += space ? space.InverseTransformVector(worldDrift) : worldDrift;
                particle.rotation += Mathf.Sin(t * .55f + phase) * 6 * ramp;
                cloudParticles[i] = particle;
            }
            // Each sample starts from the held pose, so scrubbing cannot accumulate drift.
            ps.SetParticles(cloudParticles, count);
        }
    }
}
