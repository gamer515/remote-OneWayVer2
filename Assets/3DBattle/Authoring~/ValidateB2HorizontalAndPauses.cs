using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public static class ValidateB2HorizontalAndPauses
{
 public static object Run()
 {
  const string folder="Assets/3DBattle/Boss2/BossAnim/Crisp/";
  var controller=BattleCharacterAssetPaths.Load<AnimatorController>(folder+"B2_CrispSequence.controller");
  var clip=BattleCharacterAssetPaths.Load<AnimationClip>(folder+"B2_Skill03_FromPlayer.anim");
  var backup=BattleCharacterAssetPaths.Load<AnimationClip>(folder+"Backups/B2_Skill03_BeforeHorizontalSword.anim");
  float maxOtherCurveError=0,maxPausePoseError=0,maxPitch=0;
  foreach(var binding in AnimationUtility.GetCurveBindings(backup))
  {
   if(binding.path.EndsWith("/RightHand")&&binding.propertyName.StartsWith("m_LocalRotation"))continue;
   var before=AnimationUtility.GetEditorCurve(backup,binding);var after=AnimationUtility.GetEditorCurve(clip,binding);
   for(int f=0;f<=44;f++)maxOtherCurveError=Mathf.Max(maxOtherCurveError,Mathf.Abs(before.Evaluate(f/30f)-after.Evaluate(f/30f)));
  }
  string[] names={"PowerSlash","DoubleCombo","WeaponCombo","Skill03"};var sm=controller.layers[0].stateMachine;
  foreach(string name in names)
  {
   var state=sm.states.Single(s=>s.state.name==name).state;var wait=sm.states.Single(s=>s.state.name=="WaitAfter"+name).state;
   var motion=(AnimationClip)state.motion;var pause=(AnimationClip)wait.motion;
   if(Mathf.Abs(pause.length/wait.speed-1)>.001f||AnimationUtility.GetAnimationEvents(pause).Length!=0)throw new Exception("Invalid pause: "+wait.name);
   foreach(var binding in AnimationUtility.GetCurveBindings(motion))
   {
    float last=AnimationUtility.GetEditorCurve(motion,binding).Evaluate(motion.length);var curve=AnimationUtility.GetEditorCurve(pause,binding);
    maxPausePoseError=Mathf.Max(maxPausePoseError,Mathf.Abs(last-curve.Evaluate(0)),Mathf.Abs(last-curve.Evaluate(.5f)),Mathf.Abs(last-curve.Evaluate(1)));
   }
  }
  var sceneAnimator=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(a=>a.name.Contains("Heavy_Hammer")&&a.gameObject.activeInHierarchy);
  var pose=UnityEngine.Object.Instantiate(sceneAnimator.gameObject);
  try
  {
   pose.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);pose.transform.localScale=Vector3.one;
   foreach(var a in pose.GetComponentsInChildren<Animator>())a.enabled=false;
   var sword=pose.GetComponentsInChildren<Transform>().Single(t=>t.name=="B2 Holy Sword");
   for(int i=0;i<=192;i++){clip.SampleAnimation(pose,i/240f);maxPitch=Mathf.Max(maxPitch,Mathf.Abs(Mathf.Asin(Mathf.Clamp(sword.forward.y,-1,1))*Mathf.Rad2Deg));}
  }
  finally{UnityEngine.Object.DestroyImmediate(pose);}
  if(maxOtherCurveError>.0001f||maxPausePoseError>.0001f||maxPitch>3)throw new Exception("Pose validation failed: "+maxOtherCurveError+", "+maxPausePoseError+", "+maxPitch);
  var asset=BattleCharacterAssetPaths.Load<GameObject>(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(sceneAnimator.gameObject)));
  var test=UnityEngine.Object.Instantiate(asset);var order=new List<string>();var seconds=new Dictionary<string,float>();string previous="";float firstSkillUnblendedFrame=-1;
  try
  {
   var legacy=test.GetComponent<Animation>();if(legacy)UnityEngine.Object.DestroyImmediate(legacy);
   var animator=test.GetComponent<Animator>();if(!animator)animator=test.AddComponent<Animator>();
   animator.avatar=sceneAnimator.avatar;animator.runtimeAnimatorController=controller;animator.fireEvents=false;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
   var allNames=names.Concat(names.Select(n=>"WaitAfter"+n)).ToArray();
   for(int i=0;i<6000;i++)
   {
    animator.Update(1/240f);var current=animator.GetCurrentAnimatorStateInfo(0);bool transitioning=animator.IsInTransition(0);
    string currentName=allNames.FirstOrDefault(n=>current.IsName(n));if(currentName!=null&&previous!=currentName){order.Add(currentName);previous=currentName;}
    if(transitioning&&currentName!=null&&currentName.StartsWith("WaitAfter")&&!seconds.ContainsKey(currentName))
    {
     var transition=animator.GetAnimatorTransitionInfo(0);float atStart=current.normalizedTime-transition.normalizedTime*.12f;
     seconds.Add(currentName,atStart);if(Mathf.Abs(atStart-1)>.01f)throw new Exception("Wrong wait duration: "+currentName+" "+atStart);
    }
    if(transitioning&&animator.GetNextAnimatorStateInfo(0).IsName("Skill03")&&animator.GetNextAnimatorStateInfo(0).normalizedTime*clip.length*30>=8)throw new Exception("Skill03 entry covered by blend");
    if(!transitioning&&current.IsName("Skill03")&&firstSkillUnblendedFrame<0)firstSkillUnblendedFrame=current.normalizedTime*clip.length*30;
   }
  }
  finally{UnityEngine.Object.DestroyImmediate(test);}
  if(seconds.Count!=4||firstSkillUnblendedFrame<0||firstSkillUnblendedFrame>=8)throw new Exception("Sequence check incomplete");
  return new{maxAttackSwordPitchDegrees=maxPitch,maxOtherCurveError,maxPausePoseError,measuredPauseSeconds=seconds,firstSkillUnblendedFrame,sequence=order.Take(17).ToArray()};
 }
}
