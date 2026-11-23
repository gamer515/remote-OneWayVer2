using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;

public static class ValidateB2Skill03
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var animator=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(a=>a.name.Contains("Heavy_Hammer")&&a.gameObject.activeInHierarchy);
  var controller=(AnimatorController)animator.runtimeAnimatorController;
  var clip=BattleCharacterAssetPaths.Load<AnimationClip>("Assets/3DBattle/Boss2/Runtime/Animations/B2_Skill03_FromPlayer.anim");
  var combo=UnityEngine.Object.FindObjectsByType<NewPlayerAttackCombo>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(x=>x.gameObject.activeInHierarchy);
  int entryFrames=Mathf.RoundToInt((clip.length-combo.motions.attacks[4].clip.length)*30f);
  float strikeFrame=combo.motions.attacks[4].damageWindows[0].x+entryFrames;
  AnimationUtility.SetAnimationEvents(clip,new[]{new AnimationEvent{time=strikeFrame/30f,functionName="EmitB2Skill03Slash"}});
  var bindings=AnimationUtility.GetCurveBindings(clip);
  int missing=bindings.Count(b=>b.path!=""&&!animator.transform.Find(b.path));
  if(missing>0)throw new Exception("Missing target curves: "+missing);
  foreach(var b in bindings)foreach(var k in AnimationUtility.GetEditorCurve(clip,b).keys)if(float.IsNaN(k.value)||float.IsInfinity(k.value))throw new Exception("Invalid curve "+b.path);
  var modelAsset=BattleCharacterAssetPaths.Load<GameObject>(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(animator.gameObject)));
  var test=UnityEngine.Object.Instantiate(modelAsset);
  var visited=new HashSet<string>();var order=new List<string>();string previous="";float firstUnblendedFrame=-1;
  var names=new[]{"PowerSlash","DoubleCombo","WeaponCombo","Skill03"};
  try
  {
   var legacy=test.GetComponent<Animation>();if(legacy)UnityEngine.Object.DestroyImmediate(legacy);
   var a=test.GetComponent<Animator>();if(!a)a=test.AddComponent<Animator>();
   a.avatar=animator.avatar;a.runtimeAnimatorController=controller;a.fireEvents=false;a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;
   a.Rebind();a.Update(0);
   for(int i=0;i<900;i++)
   {
    a.Update(1/60f);var info=a.GetCurrentAnimatorStateInfo(0);
    if(a.IsInTransition(0)&&a.GetNextAnimatorStateInfo(0).IsName("Skill03")&&a.GetNextAnimatorStateInfo(0).normalizedTime*clip.length*30f>=entryFrames)throw new Exception("Skill03 strike is still inside incoming blend");
    if(!a.IsInTransition(0)&&info.IsName("Skill03")&&firstUnblendedFrame<0)firstUnblendedFrame=info.normalizedTime*clip.length*30f;
    foreach(var n in names)if(info.IsName(n)){visited.Add(n);if(n!=previous){order.Add(n);previous=n;}}
   }
   if(visited.Count!=4||order.Count<5)throw new Exception("Sequence incomplete: "+string.Join(",",order));
   if(firstUnblendedFrame<0||firstUnblendedFrame>=entryFrames)throw new Exception("First pose did not reach full weight before strike");
  }
  finally{UnityEngine.Object.DestroyImmediate(test);}
  var emitter=animator.GetComponent<B2SwordSlash>();
  if(!emitter||!emitter.useStationarySlash||!emitter.stationarySlashPrefab)throw new Exception("Stationary slash is not connected");
  EditorUtility.SetDirty(clip);AssetDatabase.SaveAssets();
  return new{clip=clip.name,frames=Mathf.RoundToInt(clip.length*30),entryFrames,firstUnblendedFrame,missingPaths=missing,curveCount=bindings.Length,order=order.ToArray(),strikeFrame,stationarySlash=AssetDatabase.GetAssetPath(emitter.stationarySlashPrefab),playerSource=AssetDatabase.GetAssetPath(combo.motions.attacks[4].clip)};
 }
}
