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
        readonly BattleHealthBarMotion motion = new BattleHealthBarMotion();
        float lastMaximum;

        void OnEnable() { motion.Reset(); Refresh(); }
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
            if (valueText != null) valueText.text = $"{current:0}/{maximum:0}";
        }
    }
}
