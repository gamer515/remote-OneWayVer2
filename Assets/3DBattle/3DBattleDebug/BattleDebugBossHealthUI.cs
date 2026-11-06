using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class BattleDebugBossHealthUI : MonoBehaviour
    {
        public Battle3DSceneController battle;
        public RectTransform fill, damageFill;
        readonly BattleHealthBarMotion motion = new BattleHealthBarMotion();
        BattleBoss shownBoss;
        void OnEnable() { shownBoss = null; motion.Reset(); }
        void LateUpdate() => Refresh(Time.unscaledDeltaTime);
        public void Refresh(float deltaTime)
        {
            var boss = battle != null && battle.CurrentStage != null ? battle.CurrentStage.boss : null;
            if (shownBoss != boss) { shownBoss = boss; motion.Reset(); }
            float ratio = boss != null && boss.maxHealth > 0f
                ? (Application.isPlaying ? boss.Health / boss.maxHealth : 1f) : 0f;
            motion.Update(ratio, deltaTime, fill, damageFill);
        }
    }
}
