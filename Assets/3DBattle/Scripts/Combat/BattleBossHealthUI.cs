using UnityEngine;
using UnityEngine.UI;

namespace OneWay.Battle3D
{
    public sealed class BattleBossHealthUI : MonoBehaviour
    {
        public Battle3DSceneController battle;
        public BattleIntroWalkDebug intro;
        public RectTransform bar, fill;
        public RectTransform damageFill;
        public CanvasGroup visibility;
        public Text valueText;
        [Min(.01f)] public float revealSeconds = .7f;
        [Range(.1f, 1f)] public float screenWidth = .94f;
        public float RevealProgress { get; private set; }
        public bool IsRevealing { get; private set; }
        BattleStage shownStage;
        readonly BattleHealthBarMotion motion = new BattleHealthBarMotion();

        void OnEnable()
        {
            if (battle != null) battle.StageSelected += OnStageSelected;
            if (intro != null)
            {
                intro.Started += Hide;
                intro.Finished += OnIntroFinished;
                intro.ReturnedToStart += Hide;
            }
            Hide();
        }
        void OnDisable()
        {
            if (battle != null) battle.StageSelected -= OnStageSelected;
            if (intro != null)
            {
                intro.Started -= Hide;
                intro.Finished -= OnIntroFinished;
                intro.ReturnedToStart -= Hide;
            }
            Hide();
        }
        void OnStageSelected(BattleStage stage) => Hide();
        void OnIntroFinished(bool completed)
        {
            Hide();
            if (!completed || battle == null || battle.CurrentStage == null) return;
            shownStage = battle.CurrentStage;
            IsRevealing = true;
        }
        public void Hide()
        {
            motion.Reset();
            shownStage = null; RevealProgress = 0; IsRevealing = false;
            Refresh();
        }
        void LateUpdate() => Tick(Time.unscaledDeltaTime);
        public void Tick(float deltaTime)
        {
            if (shownStage != null && (battle == null || battle.CurrentStage != shownStage)) { Hide(); return; }
            if (IsRevealing)
            {
                RevealProgress = Mathf.Clamp01(RevealProgress + Mathf.Max(0, deltaTime) / Mathf.Max(.01f, revealSeconds));
                if (RevealProgress >= 1) IsRevealing = false;
            }
            Refresh(deltaTime);
        }
        public void Refresh() => Refresh(0);
        void Refresh(float deltaTime)
        {
            float width = Mathf.SmoothStep(0, screenWidth, RevealProgress);
            if (bar != null)
            {
                bar.anchorMin = new Vector2(.5f - width * .5f, 0);
                bar.anchorMax = new Vector2(.5f + width * .5f, 0);
            }
            var boss = shownStage != null ? shownStage.boss : null;
            if (visibility != null)
            {
                visibility.alpha = boss != null && RevealProgress > 0 ? 1 : 0;
                visibility.interactable = visibility.blocksRaycasts = false;
            }
            float max = boss != null ? Mathf.Max(0, boss.maxHealth) : 0;
            float health = boss != null ? Mathf.Clamp(boss.Health, 0, max) : 0;
            motion.Update(max > 0 ? health / max : 0, deltaTime, fill, damageFill);
            if (valueText != null)
            {
                valueText.enabled = false;
            }
        }
    }
}
