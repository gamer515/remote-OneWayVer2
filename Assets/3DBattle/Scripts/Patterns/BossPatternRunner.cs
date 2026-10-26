using System.Collections;
using UnityEngine;

namespace OneWay.Battle3D
{
    [RequireComponent(typeof(BattleBoss))]
    public sealed class BossPatternRunner : MonoBehaviour
    {
        public LineRenderer telegraph;
        public BattlePlayerHealth Target { get; private set; }
        public BossAttackPattern CurrentPattern { get; private set; }
        public bool IsRunning => CurrentPattern != null;
        BattleBoss boss;
        Coroutine routine;
        bool previousPatrol;

        void Awake() => boss = GetComponent<BattleBoss>();
        public void SetTarget(BattlePlayerHealth target) => Target = target;
        public bool Run(BossAttackPattern pattern)
        {
            var actions = GetComponent<BossActionController>();
            return actions != null ? actions.RequestAttack(pattern) : RunExecution(pattern);
        }
        internal bool RunExecution(BossAttackPattern pattern)
        {
            var priority = GetComponent<BossActionPriority>();
            if (GetComponent<BossActionController>() == null && priority != null && priority.BlocksNormalActions) return false;
            if (!Application.isPlaying || !isActiveAndEnabled || pattern == null ||
                boss == null || !boss.IsAlive || Target == null || !Target.IsAlive) return false;
            StopExecution();
            previousPatrol = boss.patrolEnabled;
            boss.patrolEnabled = false;
            boss.PatternControlsAnimation = true;
            CurrentPattern = pattern;
            routine = StartCoroutine(Execute(pattern));
            return true;
        }

        IEnumerator Execute(BossAttackPattern pattern)
        {
            yield return pattern.Execute(this);
            Finish(true);
        }

        void Update()
        {
            if (IsRunning && (boss == null || !boss.IsAlive || Target == null || !Target.IsAlive))
                StopPattern();
        }

        public void StopPattern()
        {
            var actions = GetComponent<BossActionController>();
            if (actions != null) { if (actions.Current == BossActionKind.Attack) actions.RequestStop(); }
            else StopExecution();
        }
        internal void StopExecution()
        {
            if (routine != null) StopCoroutine(routine);
            Finish();
        }

        void Finish(bool completed = false)
        {
            GetComponent<BossWeaponHit>()?.End();
            GetComponent<BossAttackMotion>()?.End();
            bool wasRunning = IsRunning;
            if (boss != null && IsRunning)
            {
                boss.patrolEnabled = previousPatrol;
                boss.PatternControlsAnimation = false;
                boss.RefreshAnimation();
            }
            routine = null;
            CurrentPattern = null;
            HideTelegraph();
            if (wasRunning) GetComponent<BossActionController>()?.ExecutorEnded(this, completed);
        }

        void OnDisable() => StopExecution();
        public void FaceTarget()
        {
            if (Target == null) return;
            Vector3 direction = Target.transform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
        }

        public void PlayAnimation(string state)
        {
            if (boss.animator == null || boss.animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state)) return;
            int hash = Animator.StringToHash(state);
            if (boss.animator.HasState(0, hash)) boss.animator.CrossFadeInFixedTime(hash, 0.08f, 0, 0f);
        }

        public void ShowTelegraph(float radius, float angle)
        {
            if (telegraph == null) return;
            const int segments = 64;
            telegraph.useWorldSpace = true;
            telegraph.positionCount = segments + 3;
            Vector3 origin = transform.position + Vector3.up * 0.08f;
            telegraph.SetPosition(0, origin);
            for (int i = 0; i <= segments; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(-angle * 0.5f + angle * i / segments, Vector3.up) * transform.forward;
                telegraph.SetPosition(i + 1, origin + direction * radius);
            }
            telegraph.SetPosition(segments + 2, origin);
            telegraph.enabled = true;
        }

        public void HideTelegraph() { if (telegraph != null) telegraph.enabled = false; }
    }
}
