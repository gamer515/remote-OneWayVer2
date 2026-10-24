using UnityEngine;

namespace OneWay.Battle3D
{
    // Chooses approach actions; BossActionController owns their lifecycle.
    [DefaultExecutionOrder(20)]
    [RequireComponent(typeof(BossActionController))]
    public sealed class BossApproachController : MonoBehaviour
    {
        public bool automaticApproach = true;
        public Battle3DSceneController battle;
        [Min(.1f)] public float jumpDistance = 20f;
        [Min(.1f)] public float approachDistance = 5f;
        [Min(0f)] public float walkTimeout = 5f;
        [Min(.01f)] public float runSpeed = 6f;
        [Min(.01f)] public float walkSpeed = 2f;
        [Tooltip("Off: 12번 점프. On: 17번 도약.")]
        public bool useJump17;
        public BossAttackPattern basicAttack;
        public BossAttackPattern[] attacks = new BossAttackPattern[0];
        public bool randomAttackOrder = true;
        [Min(0f)] public float afterAttackRestSeconds = 3f;
        bool restAfterAttack;
        int lastAttack = -1;
        public string runState = "Base Layer.Run_03_Boss1";
        public string walkState = "Base Layer.Spear_Walk";
        public float Distance { get; private set; }
        public float WalkElapsed { get; private set; }
        public string Status { get; private set; } = "판단";
        BossActionController actions;
        BattleBoss boss;
        bool running, walking, walkAfterJump, oldPatrol;
        bool oldRootMotion;
        void Awake() { actions = GetComponent<BossActionController>(); boss = GetComponent<BattleBoss>(); }
        void OnEnable()
        {
            actions = GetComponent<BossActionController>();
            actions.ActionEnded += OnActionEnded;
        }
        void OnDisable()
        {
            if (running || actions.Current == BossActionKind.Rest) actions.RequestStop();
            actions.ActionEnded -= OnActionEnded;
            ResetDecision();
        }
        void OnActionEnded(BossActionKind kind, BossActionEnd reason)
        {
            if (kind == BossActionKind.Attack && reason == BossActionEnd.Completed) restAfterAttack = true;
            if (kind == BossActionKind.Jump12 || kind == BossActionKind.Jump17)
                walkAfterJump = reason == BossActionEnd.Completed;
        }
        public void ResetDecision() { restAfterAttack = false; walkAfterJump = false; WalkElapsed = 0f; lastAttack = -1; Status = "판단"; }
        BossAttackPattern ChooseAttack()
        {
            if (attacks == null || attacks.Length == 0) return basicAttack;
            int index = (lastAttack + 1) % attacks.Length;
            if (randomAttackOrder && attacks.Length > 1)
            {
                index = lastAttack < 0 ? Random.Range(0, attacks.Length) : Random.Range(0, attacks.Length - 1);
                if (lastAttack >= 0 && index >= lastAttack) index++;
            }
            for (int i = 0; i < attacks.Length; i++)
            {
                int candidate = (index + i) % attacks.Length;
                if (attacks[candidate] == null) continue;
                lastAttack = candidate;
                return attacks[candidate];
            }
            return basicAttack;
        }
        bool ValidTarget => battle != null && battle.CurrentStage != null && battle.CurrentStage.boss == boss &&
            battle.player != null && battle.player.GetComponent<BattlePlayerHealth>()?.IsAlive == true;
        public bool CanExecute(bool walk)
        {
            if (!isActiveAndEnabled || !ValidTarget || boss.animator == null) return false;
            return boss.animator.HasState(0, Animator.StringToHash(walk ? walkState : runState));
        }
        internal void BeginExecution(bool walk)
        {
            running = true; walking = walk; WalkElapsed = 0f;
            oldPatrol = boss.patrolEnabled; oldRootMotion = boss.animator.applyRootMotion;
            boss.patrolEnabled = false; boss.PatternControlsAnimation = true;
            boss.animator.applyRootMotion = false;
            boss.animator.CrossFadeInFixedTime(walk ? walkState : runState, .1f, 0, 0f);
            Status = walk ? "무장 걷기" : "달리기";
        }
        internal void StopExecution()
        {
            if (!running) return;
            running = false; WalkElapsed = 0f;
            boss.patrolEnabled = oldPatrol; boss.PatternControlsAnimation = false;
            boss.animator.applyRootMotion = oldRootMotion;
            Status = "판단";
        }
        void Update() => Tick(Time.deltaTime);
        public void Tick(float delta)
        {
            if (!automaticApproach || !ValidTarget || !boss.IsAlive)
            {
                if (running || actions.Current == BossActionKind.Rest) actions.RequestStop();
                return;
            }
            Vector3 direction = battle.player.transform.position - transform.position;
            direction.y = 0f; Distance = direction.magnitude;
            if (running)
            {
                if (Distance < approachDistance)
                {
                    actions.ExecutorEnded(this, true);
                    Status = "접근 완료 · 판단";
                    return;
                }
                if (walking)
                {
                    WalkElapsed += Mathf.Max(0f, delta);
                    if (WalkElapsed >= walkTimeout) { actions.RequestApproach(false); return; }
                }
                else if (Distance >= jumpDistance && actions.RequestJump(useJump17)) return;

                transform.rotation = Quaternion.LookRotation(direction);
                // Stop just inside the threshold so distance >= 5 cannot stall at exactly 5.
                float step = Mathf.Min((walking ? walkSpeed : runSpeed) * Mathf.Max(0f, delta),
                    Mathf.Max(0f, Distance - Mathf.Max(0f, approachDistance - .01f)));
                Vector3 destination = transform.position + direction.normalized * step;
                var body = GetComponent<BattleArenaBody>();
                if (body != null) body.MoveTo(destination);
                else transform.position = destination;
                return;
            }
            if (actions.Current == BossActionKind.Rest && Distance >= approachDistance)
                actions.RequestStop();
            if (actions.IsBusy) return;
            if (restAfterAttack)
            {
                if (Distance < approachDistance)
                {
                    if (actions.RequestRest(afterAttackRestSeconds))
                    {
                        restAfterAttack = false;
                        Status = "5번 대기 · 공격 후 휴식";
                    }
                    return;
                }
                restAfterAttack = false;
            }
            if (Distance < approachDistance)
            {
                walkAfterJump = false;
                var attack = ChooseAttack();
                Status = attack != null && actions.RequestAttack(attack) ? attack.displayName : "사거리 내 · 판단";
                return;
            }
            if (walkAfterJump)
            {
                if (actions.RequestApproach(true)) walkAfterJump = false;
            }
            else if (Distance >= jumpDistance)
            {
                if (!actions.RequestJump(useJump17)) Status = "점프 설정 확인 필요";
            }
            else actions.RequestApproach(false);
        }
    }
}
