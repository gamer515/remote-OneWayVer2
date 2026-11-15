using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;

public static class AdjustB2Skill03AndPauses
{
 const string Folder="Assets/3DBattle/Boss2/BossAnim/Crisp/";
 static Transform Bone(GameObject g,string name)=>g.GetComponentsInChildren<Transform>().Single(t=>t.name==name);
 static Vector3 Facing(GameObject g)=>Vector3.ProjectOnPlane(Bone(g,"headfront").position-Bone(g,"Head").position,Vector3.up).normalized;
 static AnimationClip SaveClip(AnimationClip clip,string path)
 {
  var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
  if(saved){Undo.RecordObject(saved,"Adjust B2 motion");EditorUtility.CopySerialized(clip,saved);UnityEngine.Object.DestroyImmediate(clip);EditorUtility.SetDirty(saved);return saved;}
  AssetDatabase.CreateAsset(clip,path);return clip;
 }
 static void Configure(AnimatorStateTransition t,AnimatorState destination,float duration)
 {
  Undo.RecordObject(t,"Set B2 attack interval");t.destinationState=destination;t.hasExitTime=true;t.exitTime=1;t.hasFixedDuration=true;t.duration=duration;t.offset=0;t.canTransitionToSelf=false;t.interruptionSource=TransitionInterruptionSource.None;EditorUtility.SetDirty(t);
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var combo=UnityEngine.Object.FindObjectsByType<NewPlayerAttackCombo>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(c=>c.gameObject.activeInHierarchy);
  var animator=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(a=>a.name.Contains("Heavy_Hammer")&&a.gameObject.activeInHierarchy);
  var controller=(AnimatorController)animator.runtimeAnimatorController;var sm=controller.layers[0].stateMachine;
  var skill=sm.states.Single(s=>s.state.name=="Skill03").state;var clip=(AnimationClip)skill.motion;
  if(!AssetDatabase.IsValidFolder(Folder+"Backups"))AssetDatabase.CreateFolder(Folder.TrimEnd('/'),"Backups");
  string backupPath=Folder+"Backups/B2_Skill03_BeforeHorizontalSword.anim";
  if(!AssetDatabase.LoadAssetAtPath<AnimationClip>(backupPath)&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(clip),backupPath))throw new Exception("Cannot back up Skill03");
  string controllerBackup=Folder+"Backups/B2_CrispSequence_BeforePauses.controller";
  if(!AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerBackup)&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(controller),controllerBackup))throw new Exception("Cannot back up controller");
  var original=AssetDatabase.LoadAssetAtPath<AnimationClip>(backupPath);var sourceClip=combo.motions.attacks[4].clip;
  int end=Mathf.RoundToInt(original.length*30f);int prefix=Mathf.RoundToInt((original.length-sourceClip.length)*30f);
  float attackEnd=combo.motions.attacks[4].sourceChainFrame;
  var p=UnityEngine.Object.Instantiate(combo.GetComponentInChildren<Animator>().gameObject);var b=UnityEngine.Object.Instantiate(animator.gameObject);
  float maxAttackPitch=0,maxDirectionError=0,maxMountError=0;
  try
  {
   foreach(var g in new[]{p,b}){g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);g.transform.localScale=Vector3.one;foreach(var a in g.GetComponentsInChildren<Animator>())a.enabled=false;}
   var ps=Bone(p,"Sword");var bs=Bone(b,"B2 Holy Sword");var hand=Bone(b,"RightHand");var grip=Bone(b,"Holy Sword Grip");
   original.SampleAnimation(b,0);sourceClip.SampleAnimation(p,0);
   var heading=Quaternion.FromToRotation(Facing(p),Facing(b));
   var mount=Quaternion.Inverse(hand.rotation)*bs.rotation;
   Vector3 gripOffset=hand.InverseTransformPoint(grip.position);
   var curves=Enumerable.Range(0,4).Select(_=>new AnimationCurve()).ToArray();Quaternion previous=Quaternion.identity;
   // Sample the wrist more densely than the body so fast turns remain horizontal between frames.
   for(int step=0;step<=end*4;step++)
   {
    float f=step/4f;
    float sourceFrame=Mathf.Max(0,f-prefix);original.SampleAnimation(b,f/30f);sourceClip.SampleAnimation(p,sourceFrame/30f);
    Vector3 flat=Vector3.ProjectOnPlane(ps.forward,Vector3.up).normalized;
    Quaternion flatRotation=flat.sqrMagnitude>.001f?Quaternion.FromToRotation(ps.forward,flat)*ps.rotation:ps.rotation;
    float restore=Mathf.SmoothStep(0,1,Mathf.InverseLerp(attackEnd,sourceClip.length*30f,sourceFrame));
    var desired=heading*Quaternion.Slerp(flatRotation,ps.rotation,restore);
    hand.rotation=desired*Quaternion.Inverse(mount);
    var q=hand.localRotation;if(step>0&&Quaternion.Dot(previous,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous=q;
    float[] values={q.x,q.y,q.z,q.w};for(int i=0;i<4;i++)curves[i].AddKey(new Keyframe(f/30f,values[i]));
    if(sourceFrame<=attackEnd)maxAttackPitch=Mathf.Max(maxAttackPitch,Mathf.Abs(Mathf.Asin(Mathf.Clamp(bs.forward.y,-1,1))*Mathf.Rad2Deg));
    maxDirectionError=Mathf.Max(maxDirectionError,Vector3.Angle(bs.forward,desired*Vector3.forward));
    maxMountError=Mathf.Max(maxMountError,Vector3.Distance(gripOffset,hand.InverseTransformPoint(grip.position)));
   }
   if(maxAttackPitch>.1f||maxDirectionError>.1f)throw new Exception("Sword orientation validation failed");
   Undo.RecordObject(clip,"Hold Skill03 sword horizontally");string path=AnimationUtility.CalculateTransformPath(hand,b.transform);string[] axes={"x","y","z","w"};
   for(int i=0;i<4;i++){for(int k=0;k<curves[i].length;k++){AnimationUtility.SetKeyLeftTangentMode(curves[i],k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curves[i],k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+axes[i]),curves[i]);}
   clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);
  }
  finally{UnityEngine.Object.DestroyImmediate(p);UnityEngine.Object.DestroyImmediate(b);}

  if(!AssetDatabase.IsValidFolder(Folder+"Pauses"))AssetDatabase.CreateFolder(Folder.TrimEnd('/'),"Pauses");
  string[] names={"PowerSlash","DoubleCombo","WeaponCombo","Skill03"};
  var result=new List<object>();
  for(int i=0;i<names.Length;i++)
  {
   var attack=sm.states.Single(s=>s.state.name==names[i]).state;var next=sm.states.Single(s=>s.state.name==names[(i+1)%names.Length]).state;
   var attackClip=(AnimationClip)attack.motion;var pauseClip=new AnimationClip{name="B2_PauseAfter_"+names[i],frameRate=30};
   foreach(var binding in AnimationUtility.GetCurveBindings(attackClip))
   {
    float value=AnimationUtility.GetEditorCurve(attackClip,binding).Evaluate(attackClip.length);
    AnimationUtility.SetEditorCurve(pauseClip,binding,AnimationCurve.Constant(0,1,value));
   }
   AnimationUtility.SetAnimationEvents(pauseClip,Array.Empty<AnimationEvent>());var settings=AnimationUtility.GetAnimationClipSettings(pauseClip);settings.loopTime=false;settings.startTime=0;settings.stopTime=1;AnimationUtility.SetAnimationClipSettings(pauseClip,settings);
   pauseClip=SaveClip(pauseClip,Folder+"Pauses/"+pauseClip.name+".anim");
   string waitName="WaitAfter"+names[i];var wait=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name==waitName);
   if(!wait)wait=sm.AddState(waitName,new Vector3(i*260,150,0));
   Undo.RecordObject(wait,"Set B2 attack interval");wait.motion=pauseClip;wait.speed=1;wait.speedParameterActive=false;wait.writeDefaultValues=attack.writeDefaultValues;EditorUtility.SetDirty(wait);
   var toWait=attack.transitions.SingleOrDefault(t=>t.destinationState==next||t.destinationState==wait);
   if(!toWait)toWait=attack.AddTransition(wait);
   Configure(toWait,wait,0);
   var toNext=wait.transitions.SingleOrDefault(t=>t.destinationState==next);if(!toNext)toNext=wait.AddTransition(next);Configure(toNext,next,.12f);
   EditorUtility.SetDirty(attack);result.Add(new{attack=attack.name,wait=wait.name,seconds=pauseClip.length/wait.speed,next=next.name,events=AnimationUtility.GetAnimationEvents(pauseClip).Length});
  }
  EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
  return new{maxAttackPitch,maxDirectionError,maxMountError,entryFrames=prefix,attackEnd,frames=end,pauses=result};
 }
}
