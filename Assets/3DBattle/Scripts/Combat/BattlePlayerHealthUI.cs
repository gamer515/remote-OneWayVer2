using UnityEngine;
using UnityEngine.UI;

namespace OneWay.Battle3D
{
    public sealed class BattlePlayerHealthUI : MonoBehaviour
    {
        public BattlePlayerHealth playerHealth;
        public RectTransform fill;
        public RectTransform damageFill;
        public Text valueText;
        public DebugPlayerSpecialCombo energy;
        public BattlePlayerVitalsGraphic vitals;
        readonly BattleHealthBarMotion motion = new BattleHealthBarMotion();
        float lastMaximum;
        Text lastValueText;
        double displayedCurrent = double.NaN, displayedMaximum = double.NaN;

        void OnEnable() { motion.Reset(); lastValueText = null; Refresh(); }
        void LateUpdate() => Tick(Time.unscaledDeltaTime);
        public void Refresh() => Tick(0);

        public void Tick(float deltaTime)
        {
            if (playerHealth == null) return;
            float maximum = Mathf.Max(0f, playerHealth.maxHealth);
            float current = Application.isPlaying ? Mathf.Clamp(playerHealth.Health, 0f, maximum) : maximum;
            float ratio = maximum > 0f ? current / maximum : 0f;
            if (maximum != lastMaximum || !Application.isPlaying) motion.Reset();
            lastMaximum = maximum;
            motion.Update(ratio, deltaTime, fill, damageFill);
            if (vitals != null) vitals.SetValues(motion.Displayed, motion.DamageEdge, energy != null ? energy.Gauge01 : 0);
            // Keep animating the bars even when the rounded numeric label is unchanged.
            if (valueText != null && valueText.isActiveAndEnabled)
            {
                double roundedCurrent = System.Math.Round(current, 0, System.MidpointRounding.AwayFromZero);
                double roundedMaximum = System.Math.Round(maximum, 0, System.MidpointRounding.AwayFromZero);
                if (lastValueText != valueText || displayedCurrent != roundedCurrent || displayedMaximum != roundedMaximum)
                {
                    valueText.text = $"{current:0}/{maximum:0}";
                    lastValueText = valueText; displayedCurrent = roundedCurrent; displayedMaximum = roundedMaximum;
                }
            }
            else lastValueText = null;
        }
    }
}
