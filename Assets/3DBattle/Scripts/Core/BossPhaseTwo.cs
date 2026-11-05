using UnityEngine;

namespace OneWay.Battle3D
{
    // Runs before automatic approach decisions, after damage/reaction resolution.
    [DefaultExecutionOrder(10)]
    [RequireComponent(typeof(BattleBoss), typeof(BossActionController))]
    public sealed class BossPhaseTwo : MonoBehaviour
    {
        public string transitionState = "Base Layer.Skill_01_Boss1";
        public BossWeaponAttackPattern entryAttack;
        public BossAttackPattern originalAttack8;
        [Min(0f)] public float waitAfterSkill = 2f;
        public int Phase { get; private set; } = 1;
        public bool EntryFinished { get; private set; }
        public bool NeedsTransition => isActiveAndEnabled && Phase == 1 && Boss.IsAlive && (forceTransition || Boss.Health < Boss.maxHealth * .5f);
        public bool IsInvulnerable => isActiveAndEnabled && (Actions.Current == BossActionKind.PhaseChange || (Gimmick != null && Gimmick.Invulnerable));
        public bool BlocksAutomaticActions => NeedsTransition || (Phase == 2 && !EntryFinished);
        BattleBoss Boss => GetComponent<BattleBoss>();
        BossActionController Actions => GetComponent<BossActionController>();
        BossPhaseTwoGimmick Gimmick => GetComponent<BossPhaseTwoGimmick>() is BossPhaseTwoGimmick g && g.isActiveAndEnabled ? g : null;
        bool forceTransition;
        public bool ForcePhaseTwo() { if (!Application.isPlaying || !isActiveAndEnabled || !Boss.IsAlive || Phase != 1) return false; forceTransition = true; return true; }
        enum Step { Ready, Skill, Wait, Approach, Attack, Done }
        Step step;
        float remaining;
        bool authorizingAttack;

        public bool AllowsAttack(BossAttackPattern pattern)
        {
            if (!isActiveAndEnabled) return true;
            if (pattern == entryAttack || pattern == originalAttack8) return authorizingAttack && pattern == entryAttack;
            return !BlocksAutomaticActions;
        }
        void Update() => Tick(Time.deltaTime);
        public void Tick(float delta)
        {
            if (!Boss.IsAlive) { Gimmick?.Abort(); return; }
            var priority = GetComponent<BossActionPriority>();
            bool reactionPending = priority != null && priority.PendingHit;
            if (step == Step.Ready && NeedsTransition)
            {
                if (Actions.Current == BossActionKind.Attack || Actions.Current == BossActionKind.HitReaction ||
                    Actions.Current == BossActionKind.Down || Actions.Airborne || reactionPending) return;
                GetComponent<BossPatternRunner>()?.FaceTarget();
                if (Actions.RequestPhaseChange(transitionState)) { Phase = 2; step = Step.Skill; }
                return;
            }
            if (step == Step.Skill)
            {
                if (Actions.Current == BossActionKind.PhaseChange) return;
                remaining = waitAfterSkill;
                step = Step.Wait;
                return;
            }
            if (step == Step.Wait)
            {
                remaining -= Mathf.Max(0f, delta);
                if (remaining > 0f || Actions.IsBusy || reactionPending) return;
                step = Step.Approach;
            }
            if (step == Step.Approach)
            {
                if (Gimmick != null) { if (!Gimmick.PrepareEntry(delta)) return; }
                else if (Actions.IsBusy || reactionPending) return;
                authorizingAttack = true;
                try { if (Actions.RequestAttack(entryAttack)) { step = Step.Attack; Gimmick?.BeginEntry(); } }
                finally { authorizingAttack = false; }
                return;
            }
            if (step == Step.Attack && Actions.Current != BossActionKind.Attack)
            {
                EntryFinished = true; step = Step.Done;
            }
        }
        public void ResetPhase()
        {
            Gimmick?.ResetGimmick(); forceTransition = false;
            Phase = 1; EntryFinished = false; step = Step.Ready; remaining = 0; authorizingAttack = false;
        }
        void OnDisable() { if (Actions.Current == BossActionKind.PhaseChange) Actions.RequestStop(); ResetPhase(); }
    }
}

