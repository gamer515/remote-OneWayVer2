using UnityEngine;

namespace OneWay.Battle3D
{
    // Detects priority conditions; the action controller owns all transitions.
    [DefaultExecutionOrder(-40)]
    [RequireComponent(typeof(BattleBoss), typeof(BossActionController))]
    public sealed class BossActionPriority : MonoBehaviour
    {
        public string hitState = "Base Layer.Hit_Reaction_1";
        [Min(.01f)] public float hitDuration = 1f;
        [Min(.01f)] public float downDuration = 2f;
        public bool Airborne { get; private set; }
        public bool HitReacting => Actions.Current == BossActionKind.HitReaction;
        public bool Down => Actions.Current == BossActionKind.Down;
        public bool PendingHit { get; private set; }
        public bool PendingDeath => Boss != null && !Boss.IsAlive && !Down;
        public bool ControlsAnimation => Airborne || HitReacting || Down || Actions.Current == BossActionKind.Rest;
        public bool BlocksNormalActions => Actions.IsBusy || Airborne;
        BattleBoss Boss => GetComponent<BattleBoss>();
        BossActionController Actions => GetComponent<BossActionController>();
        bool crossedTwoThirds, crossedOneThird;
        void Update() => Resolve();
        public void Tick(float delta) { Actions.Tick(delta); Resolve(); }
        public void OnDamage()
        {
            if(Boss.Health<=Boss.maxHealth*(2f/3f) && !crossedTwoThirds) { crossedTwoThirds=true; PendingHit=true; }
            if(Boss.Health<=Boss.maxHealth/3f && !crossedOneThird) { crossedOneThird=true; PendingHit=true; }
            Resolve();
        }
        public void EnterAirborne() => Airborne=true;
        internal void ClearAirborne() => Airborne=false;
        public void Land() { Airborne=false; Resolve(); Actions.OnLanded(); }
        void Resolve()
        {
            if(Airborne || Down || (Boss.IsAlive && !PendingHit)) return;
            bool death=!Boss.IsAlive;
            PendingHit=false;
            Actions.BeginReaction(death,death ? Boss.deadState : hitState,death ? downDuration : hitDuration);
        }
        public void ResetPriority()
        {
            Actions.ResetController();
            Airborne=PendingHit=false;
            crossedTwoThirds=crossedOneThird=false;
        }
        void OnDisable() => ResetPriority();
    }
}
