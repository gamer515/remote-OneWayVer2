using System.Collections.Generic;
using UnityEngine;

namespace OneWay.Battle3D
{
    // Damage regions expire independently, including when several hits overlap.
    public sealed class BattleHealthBarMotion
    {
        struct DamageRegion { public float upper, remaining; }
        readonly List<DamageRegion> damage = new List<DamageRegion>();
        bool initialized;
        float target, start, elapsed;
        public float Displayed { get; private set; }
        public float DamageEdge { get; private set; }
        public void Reset() { initialized = false; damage.Clear(); }
        public void Update(float ratio, float deltaTime, RectTransform green, RectTransform red)
        {
            ratio = Mathf.Clamp01(ratio);
            float dt = Mathf.Max(0, deltaTime);
            for (int i = damage.Count - 1; i >= 0; i--)
            {
                var region = damage[i]; region.remaining -= dt;
                if (region.remaining <= 0) damage.RemoveAt(i); else damage[i] = region;
            }
            if (!initialized || ratio > target)
            {
                initialized = true; target = start = Displayed = ratio; elapsed = 2;
                damage.Clear();
            }
            else if (ratio < target)
            {
                damage.Add(new DamageRegion { upper = Displayed, remaining = 3 });
                target = ratio; start = Displayed; elapsed = 0;
            }
            else elapsed = Mathf.Min(2, elapsed + dt);
            Displayed = Mathf.Lerp(start, target, elapsed / 2);
            DamageEdge = target;
            foreach (var region in damage) DamageEdge = Mathf.Max(DamageEdge, region.upper);
            if (green != null) green.anchorMax = new Vector2(Displayed, 1);
            if (red != null) red.anchorMax = new Vector2(DamageEdge, 1);
        }
    }
}
