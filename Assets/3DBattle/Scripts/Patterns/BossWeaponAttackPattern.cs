using System.Collections;
using UnityEngine;

namespace OneWay.Battle3D
{
    [CreateAssetMenu(menuName = "3DBattle/Weapon Attack")]
    public sealed class BossWeaponAttackPattern : BossAttackPattern
    {
        public AnimationClip clip;
        [Tooltip("Authored planar travel, in boss local units. Use an in-place clip with these curves.")]
        public AnimationCurve travelX = AnimationCurve.Constant(0, 1, 0);
        public AnimationCurve travelZ = AnimationCurve.Constant(0, 1, 0);
        [Min(0f)] public float hitStartSeconds = .85f;
        [Min(0f)] public float hitEndSeconds = 1.45f;
        [Tooltip("World-space downward extrusion of the weapon box; does not widen horizontal reach.")]
        [Min(0f)] public float downwardExtension;
        [System.Serializable]
        public struct HitWindow
        {
            [Min(0f)] public float startSeconds, endSeconds;
            [Min(0f)] public float downwardExtension;
            public HitWindow(float start, float end) { startSeconds = start; endSeconds = end; downwardExtension = 0; }
        }
        [Tooltip("Optional separate strikes. Empty uses the legacy single hit window.")]
        public HitWindow[] hitWindows = new HitWindow[0];
        [Tooltip("Later strikes can bypass hit invulnerability caused by this same attack execution, never dodge/block.")]
        public bool independentStrikes;
        [Tooltip("Optional times to decide whether the next authored step is needed. Uses BossApproachController.approachDistance.")]
        public float[] stepDecisionSeconds = new float[0];
        public int StrikeCount => hitWindows != null && hitWindows.Length > 0 ? hitWindows.Length : 1;
        public HitWindow GetHitWindow(int index) => hitWindows != null && hitWindows.Length > 0
            ? hitWindows[index] : new HitWindow(hitStartSeconds, hitEndSeconds);
        public float GetDownwardExtension(int index) => Mathf.Max(0, hitWindows != null && hitWindows.Length > 0
            ? hitWindows[index].downwardExtension : downwardExtension);
        public int GetStrikeIndex(float time)
        {
            for (int i = 0; i < StrikeCount; i++)
            {
                var window = GetHitWindow(i);
                if (time >= window.startSeconds && time <= window.endSeconds) return i;
            }
            return -1;
        }
        public bool TimingIsValid()
        {
            if (clip == null) return false;
            float previousEnd = -1;
            for (int i = 0; i < StrikeCount; i++)
            {
                var w = GetHitWindow(i);
                if (w.startSeconds < 0 || w.endSeconds <= w.startSeconds ||
                    w.endSeconds > clip.length || w.startSeconds <= previousEnd) return false;
                previousEnd = w.endSeconds;
            }
            float previousStep = -1;
            if (stepDecisionSeconds != null) foreach (float time in stepDecisionSeconds)
            {
                if (time < 0 || time <= previousStep || time >= clip.length) return false;
                previousStep = time;
            }
            return true;
        }
        public bool IsValid(BossPatternRunner runner)
        {
            var boss = runner.GetComponent<BattleBoss>();
            var hit = runner.GetComponent<BossWeaponHit>();
            return TimingIsValid() && hit != null && hit.isActiveAndEnabled && hit.weapon != null &&
                boss.animator != null && boss.animator.HasState(0, Animator.StringToHash(animationState));
        }
        public override IEnumerator Execute(BossPatternRunner runner)
        {
            runner.FaceTarget();
            var animator = runner.GetComponent<BattleBoss>().animator;
            animator.Play(animationState, 0, 0f);
            runner.GetComponent<BossAttackMotion>()?.Begin(this);
            runner.GetComponent<BossWeaponHit>().Begin(this, runner.Target);
            // Animator time is authoritative: the damage window and completion share the same clock.
            yield return null;
            while (animator.GetCurrentAnimatorStateInfo(0).fullPathHash == Animator.StringToHash(animationState) &&
                animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                yield return null;
            runner.GetComponent<BossWeaponHit>().End();
            var motion = runner.GetComponent<BossAttackMotion>();
            motion?.Advance();
            motion?.End();
            if (recoverySeconds > 0f) yield return new WaitForSeconds(recoverySeconds);
        }
    }
}
