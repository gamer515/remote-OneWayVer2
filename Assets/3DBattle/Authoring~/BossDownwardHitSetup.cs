using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class BossDownwardHitSetup
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static string Run()
    {
        Check(!Application.isPlaying,"Edit mode required");
        var boss=UnityEngine.Object.FindObjectsByType<BossApproachController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single();
        var realPlayer=UnityEngine.Object.FindObjectsByType<BattlePlayerHealth>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single();
        var cc=realPlayer.GetComponent<CharacterController>();
        var copy=UnityEngine.Object.Instantiate(boss.gameObject);copy.hideFlags=HideFlags.HideAndDontSave;
        var dummy=new GameObject("DownwardHitTestTarget"){hideFlags=HideFlags.HideAndDontSave};
        var testPatterns=new List<BossWeaponAttackPattern>();
        var changes=new List<Tuple<BossWeaponAttackPattern,int,float>>();
        try {
            foreach(var b in copy.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            foreach(var c in copy.GetComponentsInChildren<Collider>(true))c.enabled=false;
            copy.SetActive(true);
            var a=copy.GetComponent<BattleBoss>().animator;a.enabled=true;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();a.Update(0);
            var hit=copy.GetComponent<BossWeaponHit>();hit.enabled=true;
            var debug=copy.GetComponent<BossWeaponHitDebug>();debug.showHitbox=true;
            var health=dummy.AddComponent<BattlePlayerHealth>();health.maxHealth=1000;health.ResetHealth();
            var capsule=dummy.AddComponent<CapsuleCollider>();capsule.height=cc.height;capsule.radius=cc.radius;capsule.center=cc.center;
            var foot=copy.GetComponent<BossFootGrounding>();
            var report=new System.Text.StringBuilder();
            foreach(int number in new[]{6,7,10,14}) {
                var asset=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>("Assets/3DBattle/Settings/B1_Attack"+number+"_Smooth10_20.asset");
                var p=UnityEngine.Object.Instantiate(asset);testPatterns.Add(p);
                int strike=number==7?1:0;
                var window=p.GetHitWindow(strike);
                float topMax=float.NegativeInfinity,bottomMax=float.NegativeInfinity,chosen=window.startSeconds;
                Action<float> pose=time=>{
                    foot?.RemoveCorrection();a.Play(p.animationState,0,time/p.clip.length);a.Update(0);foot?.CorrectPose();
                };
                for(float time=window.startSeconds+.001f;time<window.endSeconds;time+=1/(p.clip.frameRate*4)){
                    pose(time);hit.GetWorldBox(out var center,out var rotation,out var half);
                    float y=Mathf.Abs((rotation*Vector3.right*half.x).y)+Mathf.Abs((rotation*Vector3.up*half.y).y)+Mathf.Abs((rotation*Vector3.forward*half.z).y);
                    topMax=Mathf.Max(topMax,center.y+y);
                    if(center.y-y>bottomMax){bottomMax=center.y-y;chosen=time;}
                }
                // Carry every part of the overhead box down to standing torso height.
                float torsoY=realPlayer.transform.position.y+cc.center.y;
                float extension=Mathf.Ceil(Mathf.Max(0,topMax-torsoY)*4)/4;
                Check(extension>0,"Expected overhead attack "+number);
                if(number==7){p.hitWindows=(BossWeaponAttackPattern.HitWindow[])p.hitWindows.Clone();var w=p.hitWindows[1];w.downwardExtension=extension;p.hitWindows[1]=w;}
                else p.downwardExtension=extension;
                health.ResetHealth();hit.Begin(p,health);pose(chosen);
                hit.GetWorldBox(out var c0,out var r0,out var h0);
                dummy.transform.position=new Vector3(c0.x,realPlayer.transform.position.y,c0.z);Physics.SyncTransforms();
                bool raw=Physics.OverlapBox(c0,h0,r0,~0,QueryTriggerInteraction.Ignore).Any(c=>c.GetComponentInParent<BattlePlayerHealth>()==health);
                hit.SampleContact();debug.Refresh();
                Check(hit.HitCount==1&&health.Health==1000-p.damage,"Downward contact failed "+number);
                hit.SampleContact();Check(hit.HitCount==1,"Repeated hit "+number);
                var line=copy.transform.Find("BossHitbox_DebugOnly").GetComponent<LineRenderer>();
                Check(line.positionCount==33,"Expanded debug edges missing");
                float min=float.PositiveInfinity,max=float.NegativeInfinity;
                for(int i=0;i<line.positionCount;i++){var v=line.GetPosition(i);min=Mathf.Min(min,v.y);max=Mathf.Max(max,v.y);}
                float extentY=Mathf.Abs((r0*Vector3.right*h0.x).y)+Mathf.Abs((r0*Vector3.up*h0.y).y)+Mathf.Abs((r0*Vector3.forward*h0.z).y);
                Check(Mathf.Abs(min-(c0.y-extentY-extension))<.005f&&Mathf.Abs(max-(c0.y+extentY))<.005f,"Debug bounds mismatch");
                // Move beyond the X projection: extending down must not add sideways reach.
                float extentX=Mathf.Abs((r0*Vector3.right*h0.x).x)+Mathf.Abs((r0*Vector3.up*h0.y).x)+Mathf.Abs((r0*Vector3.forward*h0.z).x);
                hit.End();health.ResetHealth();hit.Begin(p,health);
                dummy.transform.position=new Vector3(c0.x+extentX+cc.radius+1,realPlayer.transform.position.y,c0.z);Physics.SyncTransforms();hit.SampleContact();
                Check(hit.HitCount==0,"Horizontal reach widened "+number);
                hit.End();hit.Begin(p,health);pose(window.startSeconds-.5f/p.clip.frameRate);hit.SampleContact();
                Check(!hit.WindowOpen&&hit.CurrentDownwardExtension==0,"Timing changed "+number);
                hit.End();
                if(number==7)Check(p.GetDownwardExtension(0)==0&&p.GetDownwardExtension(2)==0,"Other swings expanded");
                changes.Add(Tuple.Create(asset,strike,extension));
                report.AppendLine($"Attack {number} strike {strike+1}: downward {extension:F2}, test frame {chosen*p.clip.frameRate:F1}, raw contact={raw}, expanded contact=true; side miss, one hit, debug cage, inactive gap PASS.");
            }
            foreach(var change in changes){
                var asset=change.Item1;Undo.RecordObject(asset,"Expand overhead strike downward");
                if(asset.StrikeCount>1){var windows=(BossWeaponAttackPattern.HitWindow[])asset.hitWindows.Clone();var w=windows[change.Item2];w.downwardExtension=change.Item3;windows[change.Item2]=w;asset.hitWindows=windows;}
                else asset.downwardExtension=change.Item3;
                EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);
            }
            string result=report+"Saved only four selected attack assets. Animation clips and timing unchanged.";
            System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/BossDownwardHitValidation.txt",result);return result;
        }finally {
            UnityEngine.Object.DestroyImmediate(copy);UnityEngine.Object.DestroyImmediate(dummy);
            foreach(var p in testPatterns)UnityEngine.Object.DestroyImmediate(p);
        }
    }
}
