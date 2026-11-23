using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using OneWay.Battle3D;
using Object=UnityEngine.Object;
public static class LinkUserFinisherToDebugPlayer
{
 const string Folder="Assets/3DBattle/3DBattleDebug/Finisher/";
 const string ClipPath=Folder+"DebugFinisher_RetreatRush_UserEdit.anim";
 const string PrepPath=Folder+"DebugFinisher_Preparation_UserEdit.anim";
 public static string Verify()
 {
  var combo=Object.FindObjectsByType<NewPlayerAttackCombo>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(c=>c.name=="New Player - Attack Combo");
  var rules=combo.GetComponent<DebugPlayerSpecialCombo>();var animator=combo.GetComponent<BattlePlayerController>().animator;
  var clip=BattleCharacterAssetPaths.Load<AnimationClip>(ClipPath);var prep=BattleCharacterAssetPaths.Load<AnimationClip>(PrepPath);
  if(combo.motions.attacks[0].clip!=clip||rules.finisherPreparation!=prep)throw new Exception("Runtime references not connected.");
  var controller=animator.runtimeAnimatorController as AnimatorController;
  var states=controller.layers[0].stateMachine.states.Select(s=>s.state).ToArray();
  foreach(var motion in new[]{clip,prep})if(!states.Any(s=>s.name==motion.name&&s.motion==motion))throw new Exception("Animator state missing.");
  float startError=0,holdError=0;
  foreach(var b in AnimationUtility.GetCurveBindings(clip))
  {
   var source=AnimationUtility.GetEditorCurve(clip,b);var curve=AnimationUtility.GetEditorCurve(prep,b);
   startError=Mathf.Max(startError,Mathf.Abs(curve.Evaluate(0)-source.Evaluate(13f/30)));
   for(int i=0;i<=20;i++)holdError=Mathf.Max(holdError,Mathf.Abs(curve.Evaluate(Mathf.Lerp(rules.finisherReadyTime,prep.length,i/20f))-source.Evaluate(26f/30)));
  }
  if(startError>.00001||holdError>.00001)throw new Exception("Preparation mismatch.");
  var oldSet=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Temp/DebugAttackSetBeforeUserFinisher.json"));
  var newSet=Newtonsoft.Json.Linq.JObject.Parse(EditorJsonUtility.ToJson(combo.motions));oldSet["MonoBehaviour"]["attacks"][0]["clip"]=newSet["MonoBehaviour"]["attacks"][0]["clip"].DeepClone();
  var oldRules=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Temp/DebugSpecialRulesBeforeUserFinisher.json"));
  var newRules=Newtonsoft.Json.Linq.JObject.Parse(EditorJsonUtility.ToJson(rules));oldRules["MonoBehaviour"]["finisherPreparation"]=newRules["MonoBehaviour"]["finisherPreparation"].DeepClone();
  if(!Newtonsoft.Json.Linq.JToken.DeepEquals(oldSet,newSet)||!Newtonsoft.Json.Linq.JToken.DeepEquals(oldRules,newRules))throw new Exception("Settings other than clip references changed.");
  return JsonConvert.SerializeObject(new{passed=true,attack=clip.name,preparation=prep.name,holdSeconds=rules.finisherHoldSeconds,damageWindows=combo.motions.attacks[0].damageWindows.Select(w=>new{x=w.x,y=w.y}).ToArray(),preparationStartError=startError,preparationHoldError=holdError,otherSettingsUnchanged=true,sceneDirty=combo.gameObject.scene.isDirty},Formatting.Indented);
 }
 public static string Apply()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required.");
  var combo=Object.FindObjectsByType<NewPlayerAttackCombo>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(c=>c.name=="New Player - Attack Combo");
  var scene=combo.gameObject.scene;
  if(scene.path!="Assets/Scenes/3DBattleDebugScene.unity")throw new Exception("Debug scene required.");
  var clip=BattleCharacterAssetPaths.Load<AnimationClip>(ClipPath);
  var rules=combo.GetComponent<DebugPlayerSpecialCombo>();
  var animator=combo.GetComponent<BattlePlayerController>().animator;
  var controller=animator.runtimeAnimatorController as AnimatorController;
  if(AssetDatabase.GetAssetPath(combo.motions)!="Assets/3DBattle/3DBattleDebug/DebugPlayerAttackSet.asset"||AssetDatabase.GetAssetPath(controller)!=Folder+"DebugFinisher.controller")throw new Exception("Expected isolated Debug assets.");
  if(BattleCharacterAssetPaths.Load<AnimationClip>(PrepPath))throw new Exception("Preparation copy already exists; inspect before replacing.");
  if(clip.frameRate!=30||combo.motions.order.Last()!=0||rules.finisherPoseFrame!=26)throw new Exception("Unexpected timing.");
  string sourceJson=EditorJsonUtility.ToJson(clip);
  string oldSetJson=EditorJsonUtility.ToJson(combo.motions);string oldRulesJson=EditorJsonUtility.ToJson(rules);
  File.WriteAllText("Temp/DebugAttackSetBeforeUserFinisher.json",oldSetJson);
  File.WriteAllText("Temp/DebugSpecialRulesBeforeUserFinisher.json",oldRulesJson);
  var prep=Object.Instantiate(rules.finisherPreparation);prep.name="DebugFinisher_Preparation_UserEdit";prep.hideFlags=HideFlags.None;prep.ClearCurves();
  // Match edited frames 13..26 during settling, then hold exactly the edited frame 26.
  // 60 fps preserves the old 0.583333-second duration with integer keys throughout this helper clip.
  prep.frameRate=60;float duration=rules.finisherPreparation.length;int end=Mathf.RoundToInt(duration*60);
  if(Mathf.Abs(duration*60-end)>.0001)throw new Exception("Preparation end needs review.");
  var bindings=AnimationUtility.GetCurveBindings(clip);var sources=bindings.Select(b=>AnimationUtility.GetEditorCurve(clip,b)).ToArray();
  var curves=sources.Select(source=>{
   var keys=Enumerable.Range(0,end+1).Select(f=>{float time=f/60f;float sourceFrame=Mathf.Lerp(13,26,Mathf.Clamp01(time/rules.finisherReadyTime));return new Keyframe(time,source.Evaluate(sourceFrame/30f));}).ToArray();
   var curve=new AnimationCurve(keys){preWrapMode=source.preWrapMode,postWrapMode=source.postWrapMode};
   for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}return curve;
  }).ToArray();
  AnimationUtility.SetEditorCurves(prep,bindings,curves);var settings=AnimationUtility.GetAnimationClipSettings(rules.finisherPreparation);settings.loopTime=false;settings.startTime=0;settings.stopTime=duration;AnimationUtility.SetAnimationClipSettings(prep,settings);
  float holdError=0,startError=0;
  for(int i=0;i<bindings.Length;i++)
  {
   var c=AnimationUtility.GetEditorCurve(prep,bindings[i]);startError=Mathf.Max(startError,Mathf.Abs(c.Evaluate(0)-sources[i].Evaluate(13f/30)));
   for(int f=26;f<=end;f++)holdError=Mathf.Max(holdError,Mathf.Abs(c.Evaluate(f/60f)-sources[i].Evaluate(26f/30)));
  }
  if(holdError>.00001||startError>.00001)throw new Exception("Preparation continuity failed.");
  AssetDatabase.CreateAsset(prep,PrepPath);AssetDatabase.SaveAssetIfDirty(prep);
  var sm=controller.layers[0].stateMachine;
  foreach(var motion in new[]{clip,prep})
  {
   var state=sm.states.Select(x=>x.state).SingleOrDefault(s=>s.name==motion.name);
   if(state==null)state=sm.AddState(motion.name);
   Undo.RecordObject(state,"Connect edited Debug finisher state");state.motion=motion;state.writeDefaultValues=true;EditorUtility.SetDirty(state);
  }
  Undo.RecordObjects(new Object[]{combo.motions,rules},"Connect edited Debug finisher");
  combo.motions.attacks[0].clip=clip;rules.finisherPreparation=prep;
  EditorUtility.SetDirty(combo.motions);EditorUtility.SetDirty(controller);EditorUtility.SetDirty(rules);
  AssetDatabase.SaveAssetIfDirty(combo.motions);AssetDatabase.SaveAssetIfDirty(controller);
  EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Failed to save Debug scene.");
  if(EditorJsonUtility.ToJson(clip)!=sourceJson)throw new Exception("User clip unexpectedly changed.");
  return JsonConvert.SerializeObject(new{applied=true,scene=scene.path,player=combo.name,attackSet=AssetDatabase.GetAssetPath(combo.motions),attack=ClipPath,preparation=PrepPath,holdSeconds=rules.finisherHoldSeconds,readyFrame=rules.finisherPoseFrame,dashStart=rules.finisherDashStartFrame,dashEnd=rules.finisherDashEndFrame,damageWindows=combo.motions.attacks[0].damageWindows.Select(w=>new{x=w.x,y=w.y}).ToArray(),preparationStartError=startError,preparationHoldPoseError=holdError,userClipUnchanged=true},Formatting.Indented);
 }
}