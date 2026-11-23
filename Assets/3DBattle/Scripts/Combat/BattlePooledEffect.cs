using UnityEngine;
using UnityEngine.VFX;

namespace OneWay.Battle3D
{
    [DisallowMultipleComponent]
    public sealed class BattlePooledEffect : MonoBehaviour
    {
        public ParticleSystem[] Particles { get; private set; }
        public VisualEffect[] Visuals { get; private set; }
        public B2SlashTrailLayout Trails { get; private set; }
        public BattleRefractiveSlash Refraction { get; private set; }
        public B2SlashFlight Flight { get; private set; }
        public B2PhaseSlashProjectile Projectile { get; private set; }
        public B2PhaseSmokeCloud Cloud { get; private set; }
        public bool IsRented { get; private set; }
        public uint Generation { get; private set; }
        BattleEffectPool pool;
        float[] simulationSpeeds, visualRates;
        float remaining = -1;

        internal void Prepare(BattleEffectPool owner, BattleEffectPool.Kind kind)
        {
            pool = owner;
            Particles = GetComponentsInChildren<ParticleSystem>(true);
            simulationSpeeds = new float[Particles.Length];
            for (int i = 0; i < Particles.Length; i++)
            {
                var main = Particles[i].main; simulationSpeeds[i] = main.simulationSpeed;
                // Expiry is owned by the lease, never a particle's Destroy stop action.
                main.stopAction = ParticleSystemStopAction.None;
            }
            Visuals = GetComponentsInChildren<VisualEffect>(true);
            visualRates = new float[Visuals.Length];
            for (int i = 0; i < Visuals.Length; i++) visualRates[i] = Visuals[i].playRate;
            Trails = GetComponent<B2SlashTrailLayout>(); Refraction = GetComponent<BattleRefractiveSlash>();
            if (kind == BattleEffectPool.Kind.FlyingSlash)
            { Flight = GetComponent<B2SlashFlight>(); if (!Flight) Flight = gameObject.AddComponent<B2SlashFlight>(); Flight.enabled = false; }
            if (kind == BattleEffectPool.Kind.PhaseSlash)
            { Projectile = GetComponent<B2PhaseSlashProjectile>(); if (!Projectile) Projectile = gameObject.AddComponent<B2PhaseSlashProjectile>(); }
            Cloud = GetComponent<B2PhaseSmokeCloud>();
            if (Cloud) Cloud.PrepareForPool();
        }

        internal void BeginLease()
        {
            Generation++; IsRented = true; remaining = -1;
            if (Flight) Flight.enabled = false;
            for (int i = 0; i < Particles.Length; i++)
            { var main = Particles[i].main; main.simulationSpeed = simulationSpeeds[i]; }
            for (int i = 0; i < Visuals.Length; i++) Visuals[i].playRate = visualRates[i];
            if (Cloud) Cloud.ResetForReuse();
        }

        public void Activate(bool playParticles = true)
        {
            gameObject.SetActive(true);
            foreach (var ps in Particles)
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (playParticles && ps.gameObject.activeInHierarchy) ps.Play(false);
            }
            foreach (var visual in Visuals) if (visual.gameObject.activeInHierarchy)
            { visual.Reinit(); if (!playParticles) visual.Stop(); }
        }

        public void FitSwing(float lifetime)
        {
            foreach (var ps in Particles) { var main = ps.main; main.simulationSpeed = .5f / Mathf.Max(.01f, lifetime); }
            foreach (var visual in Visuals) visual.playRate = 1 / Mathf.Max(.01f, lifetime);
            if (Refraction) Refraction.Initialize(lifetime);
        }

        public void ReleaseAfter(float seconds) => remaining = Mathf.Max(0, seconds);
        void Update()
        {
            if (!IsRented || remaining < 0) return;
            remaining -= Time.deltaTime;
            if (remaining <= 0) Release();
        }
        public void Release() => pool?.Return(this);
        internal void EndLease()
        {
            IsRented = false; remaining = -1;
            foreach (var ps in Particles) if (ps) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var visual in Visuals) if (visual) visual.Stop();
            if (Flight) Flight.enabled = false;
            gameObject.SetActive(false);
        }
    }
}
