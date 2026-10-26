using UnityEngine;

namespace OneWay.Battle3D
{
    [DefaultExecutionOrder(-10)]
    public sealed class BossAttackMotion : MonoBehaviour
    {
        BossWeaponAttackPattern pattern;
        Animator animator;
        Vector3 previous;
        bool oldRootMotion;
        BattlePlayerHealth target;
        BossApproachController approach;
        float previousTime;
        public int CurrentStepIndex { get; private set; } = -1;
        public bool StepAllowed { get; private set; }
        public float DecisionDistance { get; private set; }
        public void Begin(BossWeaponAttackPattern attack)
        {
            End();
            pattern = attack;
            animator = GetComponent<BattleBoss>().animator;
            oldRootMotion = animator.applyRootMotion;
            animator.applyRootMotion = false;
            previous = Evaluate(0);
            previousTime = 0;
            target = GetComponent<BossPatternRunner>()?.Target;
            approach = GetComponent<BossApproachController>();
            CurrentStepIndex = -1; StepAllowed = false;
            if (attack.stepDecisionSeconds != null && attack.stepDecisionSeconds.Length > 0 && attack.stepDecisionSeconds[0] == 0)
                DecideStep();
        }
        Vector3 Evaluate(float time) => new Vector3(pattern.travelX.Evaluate(time), 0, pattern.travelZ.Evaluate(time));
        void LateUpdate() => Advance();
        public void Advance()
        {
            if (pattern == null) return;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash != Animator.StringToHash(pattern.animationState)) return;
            float time = Mathf.Clamp01(state.normalizedTime) * pattern.clip.length;
            if (pattern.stepDecisionSeconds != null && pattern.stepDecisionSeconds.Length > 0)
            {
                if (time < previousTime) return;
                float cursor = previousTime;
                // Split at each decision boundary so skipped motion cannot be paid back later.
                while (CurrentStepIndex + 1 < pattern.stepDecisionSeconds.Length &&
                    pattern.stepDecisionSeconds[CurrentStepIndex + 1] <= time)
                {
                    float boundary = pattern.stepDecisionSeconds[CurrentStepIndex + 1];
                    MoveStepSegment(cursor, boundary);
                    DecideStep();
                    cursor = boundary;
                }
                MoveStepSegment(cursor, time);
                previousTime = time;
                previous = Evaluate(time);
                return;
            }
            Vector3 current = Evaluate(time);
            Vector3 delta = transform.TransformVector(current - previous);
            delta.y = 0;
            // Consume the full animation delta even when blocked: never build up movement debt.
            previous = current;
            MoveDelta(delta);
        }
        void DecideStep()
        {
            CurrentStepIndex++;
            Vector3 direction = target != null ? target.transform.position - transform.position : Vector3.zero;
            direction.y = 0;
            DecisionDistance = direction.magnitude;
            float range = approach != null ? approach.approachDistance : 5f;
            StepAllowed = target != null && target.IsAlive && DecisionDistance > range;
        }
        void MoveStepSegment(float from, float to)
        {
            if (!StepAllowed || to <= from) return;
            var delta = transform.TransformVector(Evaluate(to) - Evaluate(from));
            delta.y = 0;
            MoveDelta(delta);
        }
        void MoveDelta(Vector3 delta)
        {
            Vector3 destination = transform.position + delta;
            var body = GetComponent<BattleArenaBody>();
            if (body != null) body.MoveTo(destination);
            else transform.position = destination;
        }
        public void End()
        {
            if (pattern != null && animator != null) animator.applyRootMotion = oldRootMotion;
            pattern = null;
            target = null; CurrentStepIndex = -1; StepAllowed = false; DecisionDistance = 0;
        }
        void OnDisable() => End();
    }
}
