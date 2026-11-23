using UnityEngine;

namespace OneWay.Battle3D
{
    // Runtime counterpart of the authored cloud. Cache the expanded particles instead of
    // re-simulating several seconds of 128 particle systems every frame during the volley.
    public sealed class B2PhaseSmokeCloud : MonoBehaviour
    {
        public B2SmokeSlashPreview shape;
        ParticleSystem.Particle[][] held;
        ParticleSystem.Particle[] buffer;
        int[] heldCounts;
        bool hasHeld;
        float elapsed;
        public void PrepareForPool()
        {
            if (!shape) return;
            CaptureHeld(); ResetForReuse();
        }
        public void ResetForReuse()
        {
            elapsed = 0; hasHeld = false;
            if (shape) shape.SampleCloud(0);
        }
        void CaptureHeld()
        {
            shape.SampleCloud(shape.cloudHoldTime);
            if (held == null || held.Length != shape.cloudLayers.Length)
            {
                held = new ParticleSystem.Particle[shape.cloudLayers.Length][];
                heldCounts = new int[held.Length];
            }
            int capacity = 0;
            for (int i = 0; i < held.Length; i++)
            {
                var ps = shape.cloudLayers[i];
                int count = ps ? ps.particleCount : 0;
                if (held[i] == null || held[i].Length < count) held[i] = new ParticleSystem.Particle[count];
                heldCounts[i] = ps ? ps.GetParticles(held[i]) : 0;
                capacity = Mathf.Max(capacity, heldCounts[i]);
            }
            if (buffer == null || buffer.Length < capacity) buffer = new ParticleSystem.Particle[capacity];
            hasHeld = true;
        }
        public void Advance(float delta, float opacity = 1)
        {
            elapsed += Mathf.Max(0, delta);
            if (!shape) return;
            if (elapsed < shape.cloudHoldTime)
            {
                shape.SampleCloud(elapsed);
                return;
            }
            if (!hasHeld) CaptureHeld();
            for (int i = 0; i < held.Length; i++)
            {
                var ps = shape.cloudLayers[i];
                if (!ps) continue;
                int count = heldCounts[i];
                System.Array.Copy(held[i], buffer, count);
                for (int j = 0; j < count; j++)
                {
                    var color = buffer[j].startColor; color.a = (byte)(color.a * Mathf.Clamp01(opacity));
                    buffer[j].startColor = color;
                }
                ps.SetParticles(buffer, count);
                shape.FloatCloud(ps, i, elapsed - shape.cloudHoldTime);
            }
        }
    }
}
