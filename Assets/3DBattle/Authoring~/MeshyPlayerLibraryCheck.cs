using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class MeshyPlayerLibraryCheck {
 public static string Run(){
  if(!Application.isPlaying)throw new Exception("Play required");
  var player=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();var g=UnityEngine.Object.Instantiate(player.gameObject);
  var report=new List<string>();
  try{
   foreach(var script in g.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
   var p=g.GetComponent<BattlePlayerController>();p.boss=null;p.feedback=null;var a=p.animator;a.enabled=true;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;a.Rebind();a.Update(0);
   var names=new[]{"MeshyWeaponCombo_Player","MeshyDoubleComboAttack_Player","MeshyAttack_Player","MeshyCrouchPick_Player","MeshyRun_Player","MeshyShieldPushLeft_Player","MeshyGroundRoll_Player"};
   var lengths=new[]{2.5f,25/60f,2.1f,2f,.8f,70/30f,.4f+47/30f};
   var parts=a.GetComponentsInChildren<Transform>(true);var initialRoot=a.transform.localPosition;
   for(int i=0;i<names.Length;i++){
    var clip=a.runtimeAnimatorController.animationClips.Single(c=>c.name==names[i]);
    if(Mathf.Abs(clip.length-lengths[i])>.001f)throw new Exception("Duration mismatch "+names[i]);
    var bindings=AnimationUtility.GetCurveBindings(clip);if(bindings.Any(b=>b.path!=""&&a.transform.Find(b.path)==null))throw new Exception("Unbound "+names[i]);
    foreach(var binding in bindings)if(AnimationUtility.GetEditorCurve(clip,binding).keys.Any(k=>float.IsNaN(k.value)||float.IsInfinity(k.value)))throw new Exception("Invalid curve "+names[i]);
    string state="Base Layer."+(i==0?"AttackNew":names[i]);int hash=Animator.StringToHash(state);if(!a.HasState(0,hash))throw new Exception("Missing state "+state);
    a.Play(hash,0,0);a.Update(0);var first=parts.Select(t=>t.localRotation).ToArray();int moving=0;
    for(int f=1;f<=8;f++){a.Play(hash,0,f/9f);a.Update(0);if(a.GetCurrentAnimatorStateInfo(0).fullPathHash!=hash)throw new Exception("Incorrect state "+state);moving+=parts.Where((t,j)=>Quaternion.Angle(first[j],t.localRotation)>.1f).Count();}
    if(moving==0)throw new Exception("No motion "+state);
    if(Vector3.Distance(initialRoot,a.transform.localPosition)>.0001f)throw new Exception("Model root drift");
    report.Add(names[i]+": Animator PASS; duration="+clip.length+"; missing bindings=0; changing bone samples="+moving);
   }
   p.ProcessControlFrame(0,true,false,.01f);a.Update(0);string firstState=p.UsesFiveHitCombo?"Base Layer.MeshyCombo01":"Base Layer.AttackNew";if((!p.UsesFiveHitCombo&&!p.UsesNewNormalAttack)||!a.GetCurrentAnimatorStateInfo(0).IsName(firstState))throw new Exception("Input did not start normal attack");
   if(Mathf.Abs(p.ComboChainDelay(0)-(p.UsesFiveHitCombo?72/60f:2.5f)/Mathf.Max(.1f,p.normalAttackSpeed))>.001f)throw new Exception("Normal timing mismatch");
   p.ProcessControlFrame(0,true,false,.01f);p.ProcessControlFrame(0,false,false,p.ComboChainDelay(0)+.1f);a.Update(0);if(p.ComboStep!=2||p.NormalAttackStartFrame!=0)throw new Exception("Normal repeat failed");
   report.Add("Normal attack click/repeat timing PASS; repeat starts at 0. Other gameplay input assignments unchanged.");
   var preview=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/MeshyPlayer/Converted/PlayerMotionPreview.prefab");if(preview.GetComponent<MeshyAnimationDebugger>().clips.Length!=7)throw new Exception("Preview playlist missing clips");
   report.Add("Preview prefab: 7 clips registered.");
   File.WriteAllLines("Assets/3DBattle/Authoring~/MeshyPlayerLibraryRuntimeValidation.txt",report);return string.Join("\n",report);
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
}
