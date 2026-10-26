using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class FiveHitComboCheck {
 static readonly FieldInfo Remaining=typeof(BattlePlayerController).GetField("attackAnimationRemaining",BindingFlags.Instance|BindingFlags.NonPublic);
 static void Tick(BattlePlayerController p,Animator a,float dt,bool click=false){
  a.Update(dt);Remaining.SetValue(p,Mathf.Max(0,(float)Remaining.GetValue(p)-dt));p.ProcessControlFrame(0,click,false,dt);a.Update(0);
 }
 public static string Run(){
  if(!Application.isPlaying)throw new Exception("Play mode required");
  var original=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();var g=UnityEngine.Object.Instantiate(original.gameObject);var report=new List<string>();
  try{
   foreach(var component in g.GetComponentsInChildren<MonoBehaviour>(true))component.enabled=false;
   var p=g.GetComponent<BattlePlayerController>();p.boss=null;p.feedback=null;var a=p.animator;a.enabled=true;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();a.Update(0);
   if(!p.UsesFiveHitCombo)throw new Exception("Five-hit configuration not detected");
   int[] endFrames={90,58,68,51,152},gateFrames={72,40,50,33,152};
   var body=a.GetComponentsInChildren<Transform>().First(t=>t.name=="Body");Vector3 positionAtThird=Vector3.zero;float planarJump=0;
   for(int n=2;n<5;n++){
    var c=a.runtimeAnimatorController.animationClips.Single(c=>c.name=="MeshyCombo0"+(n+1));
    foreach(int frame in new[]{0,4,8,16}){
     a.Play("Base Layer.MeshyCombo0"+(n+1),0,frame/(60f*c.length));a.Update(0);Vector3 position=body.position;position.y=0;
     if(n==2&&frame==0)positionAtThird=position;planarJump=Mathf.Max(planarJump,Vector3.Distance(positionAtThird,position));
    }
   }
   if(planarJump>.005f)throw new Exception("3->4->5 horizontal teleport: "+planarJump);
   report.Add("Runtime 3->4->5 max horizontal offset="+planarJump+"m PASS.");
   for(int n=0;n<5;n++){
    string name="MeshyCombo0"+(n+1);var c=a.runtimeAnimatorController.animationClips.Single(c=>c.name==name);
    if(Mathf.Abs(c.length-endFrames[n]/60f)>.001f)throw new Exception(name+" length");
    if(AnimationUtility.GetCurveBindings(c).Any(b=>b.path!=""&&a.transform.Find(b.path)==null))throw new Exception(name+" missing bone");
    if(!a.HasState(0,Animator.StringToHash("Base Layer."+name)))throw new Exception(name+" missing state");
   }
   foreach(float speed in new[]{1f,3f}){
    p.CancelInput();p.normalAttackSpeed=speed;Tick(p,a,1,true);
    for(int step=1;step<=5;step++){
     a.Update(.0001f);if(p.ComboStep!=step||!a.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.MeshyCombo0"+step))throw new Exception("Wrong combo step "+step+" at speed "+speed);
     float gate=p.ComboChainDelay(step-1);if(Mathf.Abs(gate-gateFrames[step-1]/60f/speed)>.001f)throw new Exception("Gate mismatch");
     Tick(p,a,.001f,true);Tick(p,a,gate+.002f);
    }
    a.Update(.1f);if(p.ComboStep!=1)throw new Exception("Five-to-one restart failed");
    report.Add("Speed "+speed+": queued 1->2->3->4->5->1 PASS; first four skip recovery, fifth completes recovery.");
   }
   p.CancelInput();p.normalAttackSpeed=1;Tick(p,a,1,true);Tick(p,a,p.ComboChainDelay(0)+.02f);
   if(p.ComboStep!=1||!a.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.MeshyCombo01"))throw new Exception("Unqueued attack advanced");
   Tick(p,a,.001f,true);a.Update(.1f);if(p.ComboStep!=1)throw new Exception("Late input did not restart first");
   Tick(p,a,2);Remaining.SetValue(p,0f);typeof(BattlePlayerController).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(p,null);a.Update(.2f);
   if(!a.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"))throw new Exception("Did not return idle");
   report.Add("No queue: recovery -> idle PASS. Late recovery input: restart first PASS.");
   var preview=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/MeshyPlayer/Converted/FiveHitComboPreview.prefab");if(preview.GetComponent<MeshyAnimationDebugger>().clips.Length!=5)throw new Exception("Preview clips");
   report.Add("Five clip bindings and preview playlist PASS.");File.WriteAllLines("Assets/3DBattle/Authoring~/FiveHitComboRuntimeValidation.txt",report);return string.Join("\n",report);
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
}
