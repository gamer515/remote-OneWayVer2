using System;
using UnityEngine;

namespace OneWay.Battle3D
{
    public enum BossActionKind { Idle, Attack, Jump12, Jump17, HitReaction, Down, RunApproach, WalkApproach, Rest }
    public enum BossActionEnd { Completed, Interrupted, Reset, Failed }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BattleBoss))]
    public sealed class BossActionController : MonoBehaviour
    {
        public BossActionKind Current { get; private set; }
        public string LastResult { get; private set; } = "Ready";
        public bool StopQueued { get; private set; }
        public bool IsBusy => Current != BossActionKind.Idle;
        public bool Airborne => GetComponent<BossActionPriority>()?.Airborne == true;
        public event Action<BossActionKind> ActionStarted;
        public event Action<BossActionKind, BossActionEnd> ActionEnded;
        BattleBoss boss;
        MonoBehaviour owner;
        float reactionRemaining;
        bool downCompleted, transitioning;
        void Awake() => boss = GetComponent<BattleBoss>();
        bool CanStart => Application.isPlaying && isActiveAndEnabled && !transitioning &&
            GetComponent<BattleBoss>().IsAlive && !Airborne && Current != BossActionKind.HitReaction && Current != BossActionKind.Down;

        public bool RequestAttack(BossAttackPattern pattern)
        {
            var runner=GetComponent<BossPatternRunner>();
            if(!CanStart || pattern==null || runner==null || !runner.isActiveAndEnabled || runner.Target==null || !runner.Target.IsAlive) return false;
            if(pattern is BossWeaponAttackPattern weaponAttack && !weaponAttack.IsValid(runner)) return false;
            EndCurrent(BossActionEnd.Interrupted);
            StartAction(BossActionKind.Attack,runner);
            if(runner.RunExecution(pattern)) return true;
            EndCurrent(BossActionEnd.Failed); return false;
        }
        public bool RequestJump(bool jump17)
        {
            var jump=GetComponent<BossJumpMotionDebug>();
            if(!CanStart || jump==null || !jump.isActiveAndEnabled || !jump.CanExecute(jump17)) return false;
            EndCurrent(BossActionEnd.Interrupted);
            jump.useJump17=jump17;
            StartAction(jump17 ? BossActionKind.Jump17 : BossActionKind.Jump12,jump);
            jump.BeginExecution();
            if(jump.IsRunning) return true;
            EndCurrent(BossActionEnd.Failed); return false;
        }
        public bool RequestApproach(bool walk)
        {
            var approach = GetComponent<BossApproachController>();
            if (!CanStart || approach == null || !approach.CanExecute(walk)) return false;
            EndCurrent(BossActionEnd.Interrupted);
            StartAction(walk ? BossActionKind.WalkApproach : BossActionKind.RunApproach, approach);
            approach.BeginExecution(walk);
            return true;
        }
        public bool RequestRest(float duration)
        {
            if (!CanStart) return false;
            boss = GetComponent<BattleBoss>();
            var animator = boss.animator;
            if (animator == null || !animator.HasState(0, Animator.StringToHash(boss.idleState))) return false;
            EndCurrent(BossActionEnd.Interrupted);
            reactionRemaining = Mathf.Max(0f, duration);
            downCompleted = false;
            StartAction(BossActionKind.Rest, null);
            animator.Play(boss.idleState, 0, 0f);
            return true;
        }
        void StartAction(BossActionKind kind, MonoBehaviour executor)
        {
            Current=kind; owner=executor; StopQueued=false; LastResult="Started "+kind;
            // Notifications are observational: transitions during callbacks are rejected.
            transitioning=true;
            try { ActionStarted?.Invoke(kind); } finally { transitioning=false; }
        }
        public void RequestStop()
        {
            if(transitioning || Current==BossActionKind.Down || Current==BossActionKind.HitReaction) return;
            if(Airborne) { StopQueued=true; LastResult="Stop deferred until landing"; return; }
            EndCurrent(BossActionEnd.Interrupted);
        }
        public void OnLanded()
        {
            // Priority resolves death/hit first, before this method is called.
            if(StopQueued && (Current==BossActionKind.Jump12 || Current==BossActionKind.Jump17))
                EndCurrent(BossActionEnd.Interrupted);
        }
        internal void BeginReaction(bool death, string state, float duration)
        {
            if(Airborne || Current==BossActionKind.Down) return;
            EndCurrent(BossActionEnd.Interrupted);
            boss=GetComponent<BattleBoss>();
            boss.patrolEnabled=false; boss.PatternControlsAnimation=false;
            reactionRemaining=Mathf.Max(.01f,duration); downCompleted=false;
            StartAction(death ? BossActionKind.Down : BossActionKind.HitReaction,null);
            var a=boss.animator;
            if(a!=null && a.runtimeAnimatorController!=null && a.HasState(0,Animator.StringToHash(state))) a.Play(state,0,0f);
        }
        void Update() => Tick(Time.deltaTime);
        public void Tick(float delta)
        {
            if(Current!=BossActionKind.HitReaction && Current!=BossActionKind.Down && Current!=BossActionKind.Rest) return;
            if(downCompleted) return;
            reactionRemaining-=Mathf.Max(0f,delta);
            if(reactionRemaining>0f) return;
            if(Current!=BossActionKind.Down) EndCurrent(BossActionEnd.Completed);
            else
            {
                downCompleted=true; LastResult="Completed Down (terminal)";
                transitioning=true;
                try { ActionEnded?.Invoke(Current,BossActionEnd.Completed); } finally { transitioning=false; }
            }
        }
        internal void ExecutorEnded(MonoBehaviour executor, bool completed)
        {
            if(owner!=executor || transitioning) return;
            EndCurrent(completed ? BossActionEnd.Completed : BossActionEnd.Interrupted);
        }
        void EndCurrent(BossActionEnd reason)
        {
            if(transitioning) return;
            var previous=Current; var executor=owner;
            owner=null; Current=BossActionKind.Idle; StopQueued=false;
            if(previous==BossActionKind.Idle) return;
            transitioning=true;
            try
            {
                if(executor is BossJumpMotionDebug jump) jump.CancelForPriority();
                if(executor is BossPatternRunner runner) runner.StopExecution();
                if(executor is BossApproachController approach) approach.StopExecution();
                LastResult=reason+" "+previous;
                if(!(previous==BossActionKind.Down && downCompleted)) ActionEnded?.Invoke(previous,reason);
            }
            finally { transitioning=false; }
            GetComponent<BattleBoss>().RefreshAnimation();
        }
        public void ResetController()
        {
            if (transitioning) return;
            EndCurrent(BossActionEnd.Reset);
            GetComponent<BossActionPriority>()?.ClearAirborne();
            reactionRemaining=0f; downCompleted=false;
            GetComponent<BossApproachController>()?.ResetDecision();
        }
        void OnDisable() => ResetController();
    }
}
