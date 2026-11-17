using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using OneWay.Battle3D;

public static class VerifyDebugSpecialCombo
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    sealed class Session : IDisposable
    {
        public BattlePlayerController player;
        public NewPlayerAttackCombo combo;
        public NewPlayerLocomotion loco;
        public DebugPlayerSpecialCombo rules;
        public DebugPlayerCombatFeel feel;
        public BattleBoss dummy;
        CharacterController motor;
        BattleArenaBody body;
        BattleFollowCamera follow;
        BattleSwordSlashEffect vfx;
        BattleBoss originalBoss;
        GameObject floor, target, oldSlash, oldFlash;
        Vector3 position;
        Quaternion rotation;
        bool enabled, readInput, stop;
        float speed, volume;
        public bool held;
        public int damageBefore;
        public int Hits => combo.DamageApplications - damageBefore;
        public Session()
        {
            player = UnityEngine.Object.FindObjectsByType<BattlePlayerController>(FindObjectsSortMode.None).Single(p => p.name == "New Player - Attack Combo");
            combo = player.GetComponent<NewPlayerAttackCombo>(); loco = player.GetComponent<NewPlayerLocomotion>();
            rules = player.GetComponent<DebugPlayerSpecialCombo>(); feel = player.GetComponent<DebugPlayerCombatFeel>();
            motor = player.GetComponent<CharacterController>(); body = player.GetComponent<BattleArenaBody>();
            follow = player.viewCamera.GetComponent<BattleFollowCamera>(); vfx = player.GetComponent<BattleSwordSlashEffect>();
            originalBoss = player.boss; position = player.transform.position; rotation = player.transform.rotation;
            enabled = player.enabled; readInput = combo.readMouseInput; stop = feel.hitStop; volume = feel.soundVolume; speed = combo.playbackSpeed;
            oldSlash = vfx.slashPrefab; oldFlash = vfx.hitFlashPrefab;
            player.enabled = false; body.enabled = false; follow.enabled = false;
            combo.readMouseInput = false; feel.hitStop = false; feel.soundVolume = 0;
            vfx.slashPrefab = vfx.hitFlashPrefab = null;
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Special test floor";
            floor.transform.position = new Vector3(1000,-.5f,1000); floor.transform.localScale = new Vector3(200,1,200);
            target = new GameObject("Special test boss");
            var capsule = target.AddComponent<CapsuleCollider>(); capsule.radius = 1; capsule.height = 3; capsule.center = Vector3.up*1.5f;
            dummy = target.AddComponent<BattleBoss>(); dummy.hitbox = capsule; dummy.maxHealth = 100000;
            Reset();
        }
        public void Reset()
        {
            combo.Cancel(); loco.Stop(); loco.SetDefenseMode(false); held = false;
            player.ResetForBattle(null);
            rules.enabled = true; rules.alwaysFullGauge = true; rules.currentGauge = rules.maxGauge = 100;
            combo.playbackSpeed = speed;
            motor.enabled = false; player.transform.SetPositionAndRotation(new Vector3(1000,0,1000),Quaternion.identity); motor.enabled = true;
            combo.ResetAirRecovery(); player.boss = dummy;
            dummy.ResetBoss(); target.transform.position = new Vector3(1000,0,1003);
            player.viewCamera.transform.SetPositionAndRotation(new Vector3(1000,2,990),Quaternion.identity);
            Physics.SyncTransforms(); damageBefore = combo.DamageApplications;
        }
        public void Input(bool down, bool holding, bool roll = false, float scroll = 0)
        {
            held = holding;
            loco.ProcessControlFrame(scroll, down, roll, holding);
        }
        public void Advance(float dt)
        {
            loco.ProcessControlFrame(0,false,false,held);
            combo.Tick(dt);
        }
        public void Click() { Input(true,true); Advance(.005f); Input(false,false); }
        public void Hold() => Input(true,true);
        public void Release() => Input(false,false);
        public void Until(Func<bool> predicate, float dt = .01f, int limit = 10000)
        {
            for (int n=0; n<limit && !predicate(); n++) Advance(dt);
            Check(predicate(), "Timed out: sequence="+combo.SequenceIndex+", frame="+combo.Frame+", active="+combo.IsActive+", special="+combo.IsSpecialCombo+", holding="+combo.IsHoldingFinisher);
        }
        public void ReachJump(bool keepHeld, float dt = .01f)
        {
            Click();
            for (int next=1;next<=3;next++)
            {
                int previous = next-1;
                Until(()=>combo.SequenceIndex==previous && !combo.InPreparation && combo.Frame>=1,dt);
                if(next==3 && keepHeld) Hold(); else Click();
                int expected=next;
                Until(()=>combo.SequenceIndex==expected && !combo.InPreparation,dt);
            }
        }
        public void ReachHold(float dt = .01f)
        {
            ReachJump(true,dt);
            Until(()=>combo.IsHoldingFinisher,dt);
        }
        public void Dispose()
        {
            feel.ResetFeedback(); combo.Cancel(); loco.Stop(); loco.SetDefenseMode(false);
            rules.enabled = true; rules.alwaysFullGauge = true; rules.currentGauge = rules.maxGauge = 100;
            player.boss = originalBoss; combo.playbackSpeed = speed;
            motor.enabled = false; player.transform.SetPositionAndRotation(position,rotation); motor.enabled = true; combo.ResetAirRecovery();
            combo.readMouseInput=readInput; feel.hitStop=stop; feel.soundVolume=volume;
            vfx.slashPrefab=oldSlash; vfx.hitFlashPrefab=oldFlash;
            body.enabled=true; follow.enabled=true; player.enabled=enabled; follow.Snap();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(floor);
        }
    }
    public static string Review()
    {
        Check(Application.isPlaying,"Play mode required.");
        using(var s=new Session())
        {
            s.ReachHold();
            var camera=s.player.viewCamera;
            const int w=800,h=600;
            var sheet=new Texture2D(w*2,h,TextureFormat.RGB24,false);
            var positions=new[]{new Vector3(4,2.5f,-6),new Vector3(4,2.5f,6)};
            for(int i=0;i<2;i++)
            {
                camera.transform.position=s.player.transform.position+positions[i];
                camera.transform.LookAt(s.player.transform.position+Vector3.up*1.3f);
                var rt=RenderTexture.GetTemporary(w,h,24);
                var oldTarget=camera.targetTexture; var oldActive=RenderTexture.active;
                camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
                var image=new Texture2D(w,h,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,w,h),0,0); image.Apply();
                sheet.SetPixels(i*w,0,w,h,image.GetPixels());
                camera.targetTexture=oldTarget; RenderTexture.active=oldActive;
                RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(image);
            }
            sheet.Apply();
            var path="Assets/3DBattle/Authoring~/DebugSpecialReadyPose.png";
            System.IO.File.WriteAllBytes(path,sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            return JsonConvert.SerializeObject(new{path,frame=s.combo.Frame,holding=s.combo.IsHoldingFinisher});
        }
    }
    public static string Run()
    {
        Check(Application.isPlaying,"Play mode required.");
        var results = new List<object>();
        using(var s=new Session())
        {
            float holdSeconds=s.rules.finisherHoldSeconds;
            try
            {
                s.Hold(); s.Advance(2);
                Check(!s.combo.IsActive && s.combo.SequenceIndex==0 && s.Hits==1,"Holding the first click advanced normal attacks.");
                results.Add(new { firstClickHoldDoesNotAutoCombo=true });

                s.Reset(); s.ReachJump(false); s.Until(()=>!s.combo.IsActive); s.Advance(1);
                Check(s.Hits==5 && !s.combo.IsSpecialCombo && s.combo.SequenceIndex==3,"Four clicks did not stop after jump.");
                results.Add(new { normalAttacks=4, normalDamageApplications=s.Hits });

                foreach(float dt in new[]{1f/60,1f/30,1f/15})
                {
                    s.Reset(); s.ReachHold(dt);
                    Check(s.Hits==7,"Wrong hit count before charged finisher: "+s.Hits);
                    var pose=s.player.transform.position;
                    float frame=s.combo.Frame;
                    float scale=Time.timeScale;
                    s.Advance(Mathf.Max(0,holdSeconds-.02f-s.combo.FinisherHoldElapsed));
                    Check(s.combo.IsHoldingFinisher && s.combo.Frame==frame && Vector3.Distance(s.player.transform.position,pose)<.001f,"Holding moved player or animation.");
                    Check(s.Hits==7 && Time.timeScale==scale,"Holding dealt damage or paused the world.");
                    s.Advance(.03f);
                    Check(s.combo.FinisherCommitted && !s.combo.IsHoldingFinisher,"Configured hold did not commit the finisher.");
                    Check(Mathf.Abs(s.combo.FinisherHoldElapsed-holdSeconds)<.001f,"Finisher hold differs from Inspector.");
                    s.Until(()=>!s.combo.IsActive,dt);
                    Check(s.Hits==8,"Finisher must damage once, total hits="+s.Hits);
                    Check(s.rules.Gauge==100,"Always-full gauge was spent.");
                    s.Advance(2);
                    Check(!s.combo.IsActive && s.Hits==8,"Held button repeated completed special.");
                    s.Release(); s.Click();
                    Check(s.combo.IsActive && s.combo.SequenceIndex==0,"Fresh click did not restart normals.");
                    results.Add(new { fps=Mathf.RoundToInt(1/dt),holdSeconds, totalHits=8, noAutomaticRepeat=true });
                }
                foreach(float releaseAt in new[]{0,holdSeconds*.5f,holdSeconds-.01f})
                {
                    s.Reset(); s.ReachHold(.001f);
                    s.Advance(Mathf.Max(0,releaseAt-s.combo.FinisherHoldElapsed));
                    var position=s.player.transform.position;
                    int hits=s.Hits;
                    s.Release(); s.Advance(1);
                    Check(!s.combo.IsActive && s.Hits==hits && Vector3.Distance(s.player.transform.position,position)<.001f,"Early release caused slash or movement at "+releaseAt);
                    results.Add(new { releaseAt, cancelledWithoutSlash=true });
                }
                s.Reset(); s.ReachHold(); s.Advance(holdSeconds-s.combo.FinisherHoldElapsed+.005f);
                s.Release(); s.Until(()=>!s.combo.IsActive);
                Check(s.Hits==8,"Release after commitment cancelled the slash.");
                results.Add(new { committedSlashCompletesAfterRelease=true });

                foreach(int sequence in new[]{4,5})
                {
                    s.Reset(); s.ReachJump(true);
                    s.Until(()=>s.combo.IsSpecialCombo && s.combo.SequenceIndex==sequence && !s.combo.InPreparation && s.combo.Frame>=8);
                    s.Release(); s.Until(()=>!s.combo.IsActive);
                    Check(s.combo.SequenceIndex==sequence,"Release advanced to next special attack.");
                    results.Add(new { releasedSpecialStep=sequence-3, stoppedAfterCurrentAttack=true });
                }
                s.Reset(); s.ReachJump(false); s.Until(()=>!s.combo.IsActive); s.Hold();
                s.Until(()=>s.combo.IsSpecialCombo);
                results.Add(new { postJumpHoldWindowWorks=true });
                s.Reset(); s.ReachJump(false); s.Until(()=>!s.combo.IsActive); s.Click();
                Check(s.combo.SequenceIndex==0 && s.combo.IsActive,"Post-jump short click was lost.");
                results.Add(new { postJumpTapStartsNormal=true });

                s.Reset(); s.ReachJump(true); s.rules.alwaysFullGauge=false; s.rules.currentGauge=50;
                s.Until(()=>!s.combo.IsActive); s.Advance(1);
                Check(s.Hits==5 && !s.combo.IsSpecialCombo && s.rules.Gauge==50,"Unfilled gauge entered special.");
                results.Add(new { gaugeConditionWorks=true });

                s.Reset(); s.combo.playbackSpeed=3.2f; s.ReachHold();
                s.Advance(holdSeconds-.02f-s.combo.FinisherHoldElapsed);
                Check(s.combo.IsHoldingFinisher,"Attack speed shortened the hold.");
                s.Advance(.03f); Check(s.combo.FinisherCommitted,"Fast attack speed never committed.");
                results.Add(new { holdIndependentOfAttackSpeed=true });

                s.Reset(); s.ReachHold(); int before=s.Hits; s.Input(false,true,true); s.Advance(.02f);
                Check(s.loco.Rolling && !s.combo.IsActive && s.Hits==before,"Roll did not cancel hold safely.");
                s.Reset(); s.ReachHold(); before=s.Hits; s.Input(false,true,false,-1); s.Advance(.02f);
                Check(s.loco.Parrying && !s.combo.IsActive && s.Hits==before,"Guard did not cancel hold safely.");
                results.Add(new { rollAndParryCancel=true });

                s.Reset(); s.rules.enabled=false; s.Click(); bool last=false;
                for(int n=0;n<1200;n++)
                {
                    s.combo.RequestAttack(); s.Advance(.01f);
                    if(s.combo.SequenceIndex==6)last=true;
                    if(last && s.combo.SequenceIndex==0)break;
                }
                Check(last && s.combo.SequenceIndex==0 && s.Hits==8,"Opt-out changed original seven-hit sequence.");
                results.Add(new { originalSequencePreserved=true });
                return JsonConvert.SerializeObject(new {passed=true,results},Formatting.Indented);
            }
            catch(Exception e) { throw new Exception(e.Message+" Completed: "+JsonConvert.SerializeObject(results),e); }
        }
    }
    public static string VerifyAuthoredFinisher()
    {
        Check(Application.isPlaying,"Play mode required.");
        using(var s=new Session())
        {
            Check(s.rules.finisherPreparation!=null,"Authored preparation is missing.");
            s.ReachHold(.001f);
            var rig=s.player.animator;
            Func<string,Transform> bone=name=>rig.GetComponentsInChildren<Transform>().First(t=>t.name==name);
            var hips=bone("Hips");var left=bone("LeftFoot");var right=bone("RightFoot");var sword=bone("Sword");
            var root=s.player.transform.position;var lp=left.position;var rp=right.position;var hp=hips.position;var sq=sword.rotation;
            float leftLead=Vector3.Dot(lp-rp,s.player.transform.forward);
            float bodyTurn=Vector3.Angle(s.player.transform.forward,Vector3.ProjectOnPlane(hips.forward,Vector3.up));
            float chestTwist=Vector3.SignedAngle(Vector3.ProjectOnPlane(hips.forward,Vector3.up),Vector3.ProjectOnPlane(bone("Chest").forward,Vector3.up),Vector3.up);
            Check(leftLead>.4f&&bodyTurn>85&&bodyTurn<94,"Preparation must lead with the left foot and turn the whole stance to the edited reference.");
            Check(chestTwist<-15&&chestTwist>-28,"Chest must counter-turn relative to the pelvis instead of adding more spine twist.");
            s.Advance(.64f-s.combo.FinisherHoldElapsed);
            Check(Vector3.Distance(hp,hips.position)<.001f,"Ready pose did not remain settled through .64 seconds.");
            Check(Quaternion.Angle(sq,sword.rotation)<.1f,"Sword moved before the final .15 seconds.");
            s.Advance(.1599f);
            float drop=hp.y-hips.position.y;float pull=Quaternion.Angle(sq,sword.rotation);
            float planted=Mathf.Max(Vector3.Distance(lp,left.position),Vector3.Distance(rp,right.position));
            float pulledBodyTurn=Vector3.Angle(s.player.transform.forward,Vector3.ProjectOnPlane(hips.forward,Vector3.up));
            float pulledChestTwist=Vector3.SignedAngle(Vector3.ProjectOnPlane(hips.forward,Vector3.up),Vector3.ProjectOnPlane(bone("Chest").forward,Vector3.up),Vector3.up);
            Check(pulledBodyTurn>95&&pulledBodyTurn<103&&pulledChestTwist<-15&&pulledChestTwist>-28,"Frame 26 must keep the user's pelvis/chest rotation distribution.");
            Check(drop>.10f&&pull>25,"Final windup did not lower the body and pull the sword back.");
            Check(planted<.003f,"Feet slid during preparation: "+planted);
            Check(s.Hits==7&&Vector3.Distance(root,s.player.transform.position)<.001f,"Preparation moved root or caused damage.");
            var transforms=rig.GetComponentsInChildren<Transform>();var before=transforms.Select(t=>t.position).ToArray();
            var rotations=transforms.Select(t=>t.rotation).ToArray();
            s.Advance(.0002f);
            Check(s.combo.FinisherCommitted,"Finisher did not commit at .8 seconds.");
            float maxPosition=transforms.Select((t,i)=>Vector3.Distance(t.position,before[i])).Max();
            float maxRotation=transforms.Select((t,i)=>Quaternion.Angle(t.rotation,rotations[i])).Max();
            Check(maxPosition<.015f&&maxRotation<2,"Preparation to slash snapped: "+maxPosition+" / "+maxRotation+" "+string.Join(",",transforms.Select((t,i)=>new{name=t.name,angle=Quaternion.Angle(t.rotation,rotations[i])}).OrderByDescending(x=>x.angle).Take(5).Select(x=>x.name+":"+x.angle)));
            var front=s.player.transform.forward;
            var leadBefore=s.player.transform.InverseTransformPoint(left.position);
            var trailBefore=s.player.transform.InverseTransformPoint(right.position);
            s.Until(()=>s.combo.Frame>=28.95f,.0002f);
            var leadAfter=s.player.transform.InverseTransformPoint(left.position);
            var trailAfter=s.player.transform.InverseTransformPoint(right.position);
            float leadingStep=leadAfter.z-leadBefore.z;
            float leadingStepAlongStance=Vector3.Dot(leadAfter-leadBefore,Quaternion.AngleAxis(33.8967f,Vector3.up)*Vector3.forward);
            Check(leadingStep>.14f&&leadingStepAlongStance>.21f&&Vector3.Distance(trailAfter,trailBefore)<.005f,"The left foot must step alone first along the turned stance.");
            Check(Vector3.Distance(root,s.player.transform.position)<.002f&&s.Hits==7,"Rush or damage started before the leading left-foot plant.");
            s.Until(()=>s.combo.Frame>=31,.0002f);
            float passingFootLift=s.player.transform.InverseTransformPoint(right.position).y-trailBefore.y;
            Check(passingFootLift>.09f,"The passing right foot did not lift.");
            s.Until(()=>s.combo.Frame>=34,.0002f);
            float rightLead=Vector3.Dot(right.position-left.position,s.player.transform.forward);
            Check(rightLead>.7f&&s.Hits==8,"The right-foot plant and slash did not finish together.");
            s.Until(()=>!s.combo.IsActive);
            Check(s.Hits==8,"Authored finisher must still hit once.");
            // Production's empty optional clip must keep the existing static hold behavior.
            var prepared=s.rules.finisherPreparation;
            try {
                s.rules.finisherPreparation=null;s.Reset();s.ReachHold(.001f);var unchanged=hips.position;var rotation=sword.rotation;
                s.Advance(.79f-s.combo.FinisherHoldElapsed);
                Check(Vector3.Distance(unchanged,hips.position)<.001f&&Quaternion.Angle(rotation,sword.rotation)<.1f,"Empty optional clip changed legacy holding.");
            } finally {s.rules.finisherPreparation=prepared;}
            return JsonConvert.SerializeObject(new{passed=true,holdSeconds=.8f,stablePoseSeconds=.65f,windupSeconds=.15f,bodyTurnDegrees=bodyTurn,chestTwistDegrees=chestTwist,pulledBodyTurnDegrees=pulledBodyTurn,pulledChestTwistDegrees=pulledChestTwist,leftLeadDuringPreparation=leftLead,leadingLeftStep=leadingStep,leadingStepAlongStance,passingRightFootLift=passingFootLift,rightLeadAtImpact=rightLead,pelvisDrop=drop,swordPullDegrees=pull,maxFootDrift=planted,commitPositionChange=maxPosition,commitRotationChange=maxRotation,productionOptOutPreserved=true},Formatting.Indented);
        }
    }
}
