using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class B2SlashFlight : MonoBehaviour
    {
        Vector3 velocity;
        float remaining;
        float delayRemaining;
        bool finishing;
        ParticleSystem[] particles;
        MeshRenderer mesh;
        bool meshEnabled;
        BattlePooledEffect pooled;
        public void Initialize(Vector3 direction, float speed, float lifetime, float launchDelay = 0f)
        {
            if (particles == null)
            {
                pooled = GetComponent<BattlePooledEffect>();
                particles = pooled ? pooled.Particles : GetComponentsInChildren<ParticleSystem>(true);
                mesh = GetComponent<MeshRenderer>(); meshEnabled = mesh && mesh.enabled;
            }
            if (mesh) mesh.enabled = meshEnabled;
            enabled = true;
            velocity = direction * Mathf.Max(0, speed);
            remaining = Mathf.Max(.01f, lifetime);
            delayRemaining = Mathf.Max(0f, launchDelay);
            finishing = false;
        }
        void Update()
        {
            Advance(Time.deltaTime);
        }
        void Advance(float deltaTime)
        {
            if (finishing) return;
            float waiting = Mathf.Min(deltaTime, delayRemaining);
            delayRemaining -= waiting;
            float flightTime = deltaTime - waiting;
            transform.position += velocity * Mathf.Min(flightTime, remaining);
            remaining -= flightTime;
            if (remaining <= 0)
            {
                finishing = true;
                if (mesh != null) mesh.enabled = false;
                float fadeTime = 0f;
                foreach (var smoke in particles)
                {
                    if (!smoke.gameObject.activeInHierarchy) continue;
                    smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    fadeTime = Mathf.Max(fadeTime, smoke.main.startLifetime.constantMax);
                }
                if (pooled) pooled.ReleaseAfter(fadeTime); else Destroy(gameObject, fadeTime);
            }
        }
    }
}
