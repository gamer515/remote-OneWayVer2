using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class B2SlashFlight : MonoBehaviour
    {
        Vector3 velocity;
        float remaining;
        float delayRemaining;
        bool finishing;
        public void Initialize(Vector3 direction, float speed, float lifetime, float launchDelay = 0f)
        {
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
                var mesh = GetComponent<MeshRenderer>();
                if (mesh != null) mesh.enabled = false;
                float fadeTime = 0f;
                foreach (var smoke in GetComponentsInChildren<ParticleSystem>())
                {
                    smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    fadeTime = Mathf.Max(fadeTime, smoke.main.startLifetime.constantMax);
                }
                Destroy(gameObject, fadeTime);
            }
        }
    }
}
