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
        // Runtime overrides leave the serialized settings intact for direct scene/debug launches.
        bool decisionEnergyConfigured;
        float decisionAttackHitEnergy, decisionParryEnergy;
        bool UsesFullGauge => alwaysFullGauge && !decisionEnergyConfigured;
        float MaximumGauge => Mathf.Max(1, maxGauge);
        const float DecisionStartingEnergy = 50f, DecisionSpecialCost = 50f;
        [Header("Special combo entry")]
        [Min(.01f), Tooltip("Hold from idle or freshly press during normal attack four. Normal attacks two and three never arm a special hold.")]
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

        public float Gauge => UsesFullGauge ? MaximumGauge : Mathf.Clamp(currentGauge, 0, MaximumGauge);
        public float Gauge01 => Gauge / MaximumGauge;
        public float StartingEnergy => decisionEnergyConfigured ? DecisionStartingEnergy
            : MaximumGauge * Mathf.Clamp01(startingEnergyPercent / 100f);
        public float SpecialCost => decisionEnergyConfigured ? DecisionSpecialCost
            : MaximumGauge * Mathf.Clamp(specialCostPercent, 1, 100) / 100f;
        public float AttackHitEnergy => decisionEnergyConfigured ? decisionAttackHitEnergy
            : MaximumGauge * Mathf.Clamp01(attackHitEnergyPercent / 100f);
        public float ParryEnergy => decisionEnergyConfigured ? decisionParryEnergy
            : MaximumGauge * Mathf.Clamp01(parryEnergyPercent / 100f);
        public bool Ready => Gauge + .00001f >= SpecialCost;

        public void ConfigureDecisionEnergy(float maximum, float attackHitGain, float parryGain)
        {
            maxGauge = Mathf.Max(1, maximum);
            decisionAttackHitEnergy = Mathf.Max(0, attackHitGain);
            decisionParryEnergy = Mathf.Max(0, parryGain);
            decisionEnergyConfigured = true;
        }

        void Awake() => ResetGauge();
        public void ResetGauge() => currentGauge = UsesFullGauge ? MaximumGauge : Mathf.Clamp(StartingEnergy, 0, MaximumGauge);
        public void ChargeFromAttackHit() => AddEnergy(AttackHitEnergy);
        public void ChargeFromParry() => AddEnergy(ParryEnergy);
        void AddEnergy(float amount)
        {
            if (!isActiveAndEnabled || UsesFullGauge || GetComponent<BattlePlayerHealth>()?.IsAlive == false) return;
            currentGauge = Mathf.Min(MaximumGauge, Gauge + Mathf.Max(0, amount));
        }
        public bool TryConsume()
        {
            if (!Ready) return false;
            if (!UsesFullGauge) currentGauge = Mathf.Max(0, Gauge - SpecialCost);
            return true;
        }
        void OnValidate()
        {
            maxGauge = Mathf.Max(1,maxGauge);
            currentGauge = UsesFullGauge ? maxGauge : Mathf.Clamp(currentGauge,0,maxGauge);
        }
    }
}
