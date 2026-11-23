using UnityEngine;

namespace OneWay.Battle3D
{
    [DisallowMultipleComponent]
    public sealed class DebugPlayerSpecialCombo : MonoBehaviour
    {
        [Header("Energy")]
        [Tooltip("Testing mode: the effective gauge stays full, even after a special combo.")]
        public bool alwaysFullGauge = true;
        [Min(1)] public float maxGauge = 100;
        [Min(0)] public float currentGauge = 100;
        [Range(0, 100), Tooltip("Energy at battle start, as a percentage of maximum.")]
        public float startingEnergyPercent = 50;
        [Range(1, 100), Tooltip("Paid once when the special combo's first dash begins.")]
        public float specialCostPercent = 50;
        [Range(0, 100)] public float attackHitEnergyPercent = 5;
        [Range(0, 100)] public float parryEnergyPercent = 25;
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
        [Header("Optional authored finisher preparation")]
        [Tooltip("Dedicated settle / ready / pullback animation. Leave empty to keep the original held frame.")]
        public AnimationClip finisherPreparation;
        [Min(0), Tooltip("Time of the planted ready pose inside the preparation clip.")]
        public float finisherReadyTime = 13f / 30f;
        [Min(0), Tooltip("Final part of Hold Seconds used to pull the sword back and lower the body; included in the total hold.")]
        public float finisherWindupSeconds = .15f;
        [Min(27), Tooltip("Authored finisher only: forward movement starts after the leading left-foot step.")]
        public float finisherDashStartFrame = 27;
        [Min(27.1f), Tooltip("Authored finisher only: forward movement ends with the right-foot plant and slash.")]
        public float finisherDashEndFrame = 30;
        [Min(.01f)] public float cancelBlendSeconds = .12f;

        public float PreparationTime(float frame, float elapsed, bool holding)
        {
            if (finisherPreparation == null) return 0;
            float ready = Mathf.Clamp(finisherReadyTime, 0, finisherPreparation.length);
            if (!holding)
                return ready * Mathf.InverseLerp(13, Mathf.Clamp(finisherPoseFrame,13,26.5f), frame);
            float total = Mathf.Max(.01f, finisherHoldSeconds);
            float windup = Mathf.Clamp(finisherWindupSeconds, 0, total);
            float progress = windup > .00001f ? Mathf.Clamp01((elapsed - (total - windup)) / windup) : 0;
            return Mathf.Lerp(ready, finisherPreparation.length, progress);
        }

        public float Gauge => alwaysFullGauge ? Mathf.Max(1, maxGauge) : Mathf.Clamp(currentGauge, 0, Mathf.Max(1,maxGauge));
        public float Gauge01 => Gauge / Mathf.Max(1,maxGauge);
        public float SpecialCost => Mathf.Max(1, maxGauge) * Mathf.Clamp(specialCostPercent, 1, 100) / 100f;
        public bool Ready => Gauge + .00001f >= SpecialCost;
        void Awake() => ResetGauge();
        public void ResetGauge() => currentGauge = Mathf.Max(1, maxGauge) * (alwaysFullGauge ? 1 : Mathf.Clamp01(startingEnergyPercent / 100f));
        public void ChargeFromAttackHit() => AddPercent(attackHitEnergyPercent);
        public void ChargeFromParry() => AddPercent(parryEnergyPercent);
        void AddPercent(float percent)
        {
            if (!isActiveAndEnabled || alwaysFullGauge || GetComponent<BattlePlayerHealth>()?.IsAlive == false) return;
            currentGauge = Mathf.Min(Mathf.Max(1, maxGauge), Gauge + Mathf.Max(1, maxGauge) * Mathf.Clamp01(percent / 100f));
        }
        public bool TryConsume()
        {
            if (!Ready) return false;
            if (!alwaysFullGauge) currentGauge = Mathf.Max(0, Gauge - SpecialCost);
            return true;
        }
        void OnValidate()
        {
            maxGauge = Mathf.Max(1,maxGauge);
            currentGauge = alwaysFullGauge ? maxGauge : Mathf.Clamp(currentGauge,0,maxGauge);
        }
    }
}
