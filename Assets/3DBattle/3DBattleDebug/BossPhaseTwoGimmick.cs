using UnityEngine;
using UnityEngine.InputSystem;

namespace OneWay.Battle3D
{
    // Optional debug-scene extension. The shared phase controller delegates only when attached.
    [DefaultExecutionOrder(30)]
    public sealed class BossPhaseTwoGimmick : MonoBehaviour
    {
        [Header("Approach")]
        [Min(.1f)] public float attackDistance = 4.5f;
        [Min(.1f)] public float repeatJumpDistance = 20f;
        public bool useJump17;
        [Header("One click per rotation; clip seconds")]
        public float[] rotationStarts = new float[10];
        public float[] strikeTimes = new float[7];
        [Min(.01f)] public float parryWindow = .22f;
        [Min(0)] public float failureDamage = 20;
        [Min(0)] public float pushDistance = 1.2f;
        [Min(.01f)] public float pushDuration = .18f;
        public AnimationClip guardClip, hitClip;
        [Min(.1f)] public float hitSpeed = 1.5f;
        [Header("Camera relative to player facing boss")]
        public Vector3 cameraOffset = new Vector3(2.2f, 1.1f, -4.2f);
        public Vector3 cameraLookOffset = new Vector3(0, 2.6f, 2.5f);
        [Min(.01f)] public float cameraBlendSeconds = .35f;
        [Min(.01f)] public float cameraReturnSeconds = .5f;
        public bool Locked { get; private set; }
        public int Rotation { get; private set; }
        public int Successes { get; private set; }
        public int Failures { get; private set; }
        public string Status { get; private set; } = "Ready";
        public bool OwnsAttack => isActiveAndEnabled && entryRunning;
        public bool Invulnerable => Locked && TimeInAttack < rotationStarts[8];
        BossPhaseTwo phase;
        BattleBoss boss;
        BossActionController actions;
        BattlePlayerController player;
        BattlePlayerHealth health;
        NewPlayerLocomotion locomotion;
        NewPlayerAttackCombo combo;
        BattleFollowCamera camera;
        bool playerEnabled, locomotionEnabled, cameraEnabled;
        bool attemptedJump, entryRunning, returning;
        bool[] clicked = new bool[10], resolved = new bool[7];
        float[] clickTimes = new float[10];
        float pushRemaining, hitElapsed = -1, blendElapsed, returnElapsed;
        Vector3 facing, savedCameraOffset, returnStart;
        Quaternion savedCameraRotation, returnRotation;
        float savedAnimatorSpeed;
        float TimeInAttack => boss != null && phase.entryAttack != null ?
            boss.animator.GetCurrentAnimatorStateInfo(0).normalizedTime * phase.entryAttack.clip.length : 0;
        void Awake() { phase=GetComponent<BossPhaseTwo>();boss=GetComponent<BattleBoss>();actions=GetComponent<BossActionController>(); }
        bool Bind()
        {
            var battle=GetComponent<BossApproachController>().battle;
            if(battle==null || battle.player==null)return false;
            player=battle.player;health=player.GetComponent<BattlePlayerHealth>();
            locomotion=player.GetComponent<NewPlayerLocomotion>();combo=player.GetComponent<NewPlayerAttackCombo>();camera=battle.followCamera;
            return health!=null && health.IsAlive && player.animator!=null && guardClip!=null && hitClip!=null &&
                rotationStarts.Length==10 && strikeTimes.Length==7;
        }
        public bool PrepareEntry(float delta)
        {
            if(!Bind()){Status="Missing player/guard/hit/timing references";return false;}
            if(actions.Airborne || actions.Current==BossActionKind.Jump12 || actions.Current==BossActionKind.Jump17)return false;
            if(actions.IsBusy && actions.Current!=BossActionKind.RunApproach)return false;
            Vector3 direction=player.transform.position-transform.position;direction.y=0;
            float distance=direction.magnitude;
            if(distance<attackDistance)
            {
                if(actions.IsBusy)actions.RequestStop();
                Status="In range";return true;
            }
            if(!attemptedJump || distance>=repeatJumpDistance)
            {
                if(actions.RequestJump(useJump17)){attemptedJump=true;Status="Jump approach";return false;}
            }
            var approach=GetComponent<BossApproachController>();
            if(actions.Current!=BossActionKind.RunApproach && !actions.RequestApproach(false))return false;
            transform.rotation=Quaternion.LookRotation(direction);
            var body=GetComponent<BattleArenaBody>();
            Vector3 destination=transform.position+direction.normalized*Mathf.Min(approach.runSpeed*Mathf.Max(0,delta),Mathf.Max(0,distance-attackDistance+.02f));
            if(body!=null)body.MoveTo(destination);else transform.position=destination;
            Status="Run approach";return false;
        }
        public void BeginEntry()
        {
            if(!Bind())return;
            entryRunning=Locked=true;returning=false;Rotation=1;Successes=Failures=0;
            clicked=new bool[10];resolved=new bool[7];clickTimes=new float[10];
            pushRemaining=0;hitElapsed=-1;blendElapsed=0;
            playerEnabled=player.enabled;locomotionEnabled=locomotion!=null && locomotion.enabled;
            player.CancelInput();if(combo!=null)combo.Cancel();
            savedAnimatorSpeed=player.animator.speed > 0 ? player.animator.speed : 1f;
            if(locomotion!=null){locomotion.SetDefenseMode(true);locomotion.enabled=false;}
            player.enabled=false;if(player.shield!=null)player.shield.SetActive(true);
            facing=transform.position-player.transform.position;facing.y=0;facing=facing.normalized;
            player.transform.rotation=Quaternion.LookRotation(facing);
            if(camera!=null)
            {
                cameraEnabled=camera.enabled;savedCameraOffset=camera.transform.position-player.transform.position;
                savedCameraRotation=camera.transform.rotation;camera.enabled=false;
                Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
            }
            HoldGuard();Status="Rotation 1: ready";
        }
        void Update()
        {
            if(!entryRunning)return;
            if(player==null || health==null || !health.IsAlive || !boss.IsAlive || !phase.enabled ||
                actions.Current!=BossActionKind.Attack || GetComponent<BossPatternRunner>().CurrentPattern!=phase.entryAttack)
            {Abort();return;}
            float time=TimeInAttack;
            Rotation=1;for(int i=1;i<10;i++)if(time>=rotationStarts[i])Rotation=i+1;
            if(!Locked)return;
            if(time>=rotationStarts[8]){Release(false);Status="Rotation 9: controls released";return;}
            if(Application.isFocused && Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame)PressParry(time);
            for(int i=0;i<7;i++)if(!resolved[i] && time>=strikeTimes[i])ResolveStrike(i);
            if(Locked && hitElapsed>=0){hitElapsed+=Time.deltaTime*hitSpeed;if(hitElapsed>=hitClip.length)hitElapsed=-1;}
        }
        public bool PressParry(float clipTime)
        {
            if(!Locked)return false;
            hitElapsed=-1;HoldGuard();
            int turn=0;for(int i=1;i<10;i++)if(clipTime>=rotationStarts[i])turn=i;
            if(turn<1 || turn>7 || clicked[turn])return false;
            clicked[turn]=true;clickTimes[turn]=clipTime;return true;
        }
        void ResolveStrike(int index)
        {
            resolved[index]=true;int turn=index+1;
            float age=(strikeTimes[index]-clickTimes[turn])/Mathf.Max(.01f,boss.animator.speed);
            bool success=clicked[turn] && age>=0 && age<=parryWindow;
            if(success){Successes++;hitElapsed=-1;player.GetComponent<BattleParryEffect>()?.PlaySuccessEffect();}
            else {Failures++;health.TakeGimmickDamage(failureDamage);if(health.IsAlive)hitElapsed=0;}
            if(!health.IsAlive){Abort();return;}
            pushRemaining+=pushDistance;
            Status="Rotation "+(turn+1)+(success?": parried":": hit");
        }
        void LateUpdate()
        {
            if(Locked)
            {
                if(pushRemaining>0)
                {
                    float step=Mathf.Min(pushRemaining,pushDistance/Mathf.Max(.01f,pushDuration)*Time.deltaTime);
                    Vector3 before=player.transform.position;
                    var body=player.GetComponent<BattleArenaBody>();Vector3 desired=before-facing*step;
                    if(body!=null)desired=body.Constrain(desired);
                    var motor=player.GetComponent<CharacterController>();
                    if(motor!=null && motor.enabled)motor.Move(desired-before);else player.transform.position=desired;
                    float actual=Mathf.Clamp(Vector3.Dot(player.transform.position-before,-facing),0,step);
                    var bossBody=GetComponent<BattleArenaBody>();var destination=transform.position-facing*actual;
                    if(bossBody!=null)bossBody.MoveTo(destination);else transform.position=destination;
                    pushRemaining=Mathf.Max(0,pushRemaining-step);
                    player.SyncBodyPosition();
                }
                if(hitElapsed>=0)Sample(hitClip,hitElapsed);else HoldGuard();
                if(camera!=null)
                {
                    blendElapsed+=Time.deltaTime;float blend=Mathf.SmoothStep(0,1,blendElapsed/Mathf.Max(.01f,cameraBlendSeconds));
                    Quaternion heading=Quaternion.LookRotation(facing);
                    Vector3 desired=player.transform.position+heading*cameraOffset;
                    Vector3 look=player.transform.position+heading*cameraLookOffset;
                    camera.transform.SetPositionAndRotation(Vector3.Lerp(player.transform.position+savedCameraOffset,desired,blend),
                        Quaternion.Slerp(savedCameraRotation,Quaternion.LookRotation(look-desired),blend));
                }
            }
            else if(returning && camera!=null && player!=null)
            {
                returnElapsed+=Time.deltaTime;float t=Mathf.SmoothStep(0,1,returnElapsed/Mathf.Max(.01f,cameraReturnSeconds));
                camera.transform.SetPositionAndRotation(Vector3.Lerp(player.transform.position+returnStart,player.transform.position+savedCameraOffset,t),Quaternion.Slerp(returnRotation,savedCameraRotation,t));
                if(t>=1){returning=false;camera.enabled=cameraEnabled;}
            }
        }
        void Sample(AnimationClip clip,float time)
        {
            var a=player.animator;a.speed=0;
            // These converted clips share the playable knight's transform bindings.
            clip.SampleAnimation(a.gameObject,Mathf.Clamp(time,0,clip.length));
        }
        void HoldGuard(){if(player!=null && player.animator!=null && guardClip!=null)Sample(guardClip,guardClip.length);}
        void Release(bool immediate)
        {
            if(Locked)
            {
                Locked=false;pushRemaining=0;hitElapsed=-1;
                if(player!=null)
                {
                    if(locomotion!=null){locomotion.enabled=locomotionEnabled;locomotion.SetDefenseMode(true);}
                    player.enabled=playerEnabled;
                    if(player.animator!=null && health.IsAlive){player.animator.speed=savedAnimatorSpeed;player.animator.Play("Base Layer.Idle",0,0);}
                    if(player.shield!=null)player.shield.SetActive(true);
                }
                if(camera!=null && player!=null){returnStart=camera.transform.position-player.transform.position;returnRotation=camera.transform.rotation;returnElapsed=0;returning=true;}
            }
            if(immediate && returning && camera!=null)
            {
                if(player!=null)camera.transform.SetPositionAndRotation(player.transform.position+savedCameraOffset,savedCameraRotation);
                camera.enabled=cameraEnabled;returning=false;
            }
        }
        public void Abort(){entryRunning=false;Release(false);attemptedJump=false;Status="Stopped";}
        public void ResetGimmick(){entryRunning=false;Release(true);attemptedJump=false;Rotation=0;Status="Ready";}
        void OnDisable()=>ResetGimmick();
    }
}

