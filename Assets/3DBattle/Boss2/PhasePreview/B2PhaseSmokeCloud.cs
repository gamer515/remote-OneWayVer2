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
        float elapsed;
        public void Advance(float delta, float opacity = 1)
        {
            elapsed += Mathf.Max(0, delta);
            if (!shape) return;
            if (elapsed < shape.cloudHoldTime)
            {
                shape.SampleCloud(elapsed);
                return;
            }
            if (held == null)
            {
                shape.SampleCloud(shape.cloudHoldTime);
                held = new ParticleSystem.Particle[shape.cloudLayers.Length][];
                int capacity = 0;
                for (int i = 0; i < held.Length; i++)
                {
                    var ps = shape.cloudLayers[i];
                    held[i] = new ParticleSystem.Particle[ps.particleCount];
                    ps.GetParticles(held[i]); capacity = Mathf.Max(capacity, held[i].Length);
                }
                buffer = new ParticleSystem.Particle[capacity];
            }
            for (int i = 0; i < held.Length; i++)
            {
                var ps = shape.cloudLayers[i];
                System.Array.Copy(held[i], buffer, held[i].Length);
                for (int j = 0; j < held[i].Length; j++)
                {
                    var color = buffer[j].startColor; color.a = (byte)(color.a * Mathf.Clamp01(opacity));
                    buffer[j].startColor = color;
                }
                ps.SetParticles(buffer, held[i].Length);
                shape.FloatCloud(ps, i, elapsed - shape.cloudHoldTime);
            }
        }
    }
}
