using UnityEngine;

namespace OneWay.Battle3D
{
    [DisallowMultipleComponent]
    public sealed class DebugPlayerSpecialCombo : MonoBehaviour
    {
        [Header("Gauge (no UI)")]
        [Tooltip("Testing mode: the effective gauge stays full, even after a special combo.")]
        public bool alwaysFullGauge = true;
        [Min(1)] public float maxGauge = 100;
        [Min(0)] public float currentGauge = 100;
        [Header("After the fourth normal attack")]
        [Min(.01f), Tooltip("Hold recognition time. A click never advances normal attacks automatically.")]
        public float holdRecognitionSeconds = .2f;
        [Min(0), Tooltip("Additional special-entry window after the jump's recovery ends.")]
        public float specialEntryWindow = .3f;
        [Header("Final retreat / rush attack")]
        [Min(.01f), Tooltip("Time spent in the ready pose. Releasing early cancels the slash.")]
        public float finisherHoldSeconds = 3;
        [Range(13,26.5f), Tooltip("Original 30 fps frame, after retreat and before the forward dash at frame 27.")]
        public float finisherPoseFrame = 26;
        [Min(.01f)] public float cancelBlendSeconds = .12f;

        public float Gauge => alwaysFullGauge ? Mathf.Max(1, maxGauge) : Mathf.Clamp(currentGauge, 0, Mathf.Max(1,maxGauge));
        public float Gauge01 => Gauge / Mathf.Max(1,maxGauge);
        public bool Ready => Gauge >= Mathf.Max(1,maxGauge) - .001f;
        public bool TryConsume()
        {
            if (!Ready) return false;
            if (!alwaysFullGauge) currentGauge = 0;
            return true;
        }
        void OnValidate()
        {
            maxGauge = Mathf.Max(1,maxGauge);
            currentGauge = alwaysFullGauge ? maxGauge : Mathf.Clamp(currentGauge,0,maxGauge);
        }
    }
}
