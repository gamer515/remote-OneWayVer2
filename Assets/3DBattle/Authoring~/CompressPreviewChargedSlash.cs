using System;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.Animations;using OneWay.Battle3D;
public static class CompressPreviewChargedSlash
{
 static float Map(float f)=>f<=2?f:f<=20?2+(f-2)/6:f-15;
 static AnimationCurve Retime(AnimationCurve source,float units){
  var keys=source.keys;
  for(int i=0;i<keys.Length;i++){
   var k=keys[i];float f=k.time*units;
   k.time=Map(f)/units;
   if(f>2.00001f&&f<=20.00001f)k.inTangent*=6;
   if(f>=1.99999f&&f<19.99999f)k.outTangent*=6;
   keys[i]=k;
  }
  return new AnimationCurve(keys){preWrapMode=source.preWrapMode,postWrapMode=source.postWrapMode};
 }
 static int Replace(AnimatorStateMachine sm,AnimationClip old,AnimationClip clip){int n=0;foreach(var state in sm.states)if(state.state.motion==old){state.state.motion=clip;EditorUtility.SetDirty(state.state);n++;}foreach(var child in sm.stateMachines)n+=Replace(child.stateMachine,old,clip);return n;}
 public static object Run(){
  if(EditorApplication.isPlaying)throw new Exception("Stop play first");
  var preview=GameObject.Find("Attack 02 - Charged_Upward_Slash").GetComponentInChildren<Animator>();
  var controller=preview.runtimeAnimatorController as AnimatorController;
  var old=controller.animationClips.Single();
  if(Mathf.Abs(old.length*30-115)>.01f)throw new Exception("Unexpected preview length");
  const string path="Assets/3DBattle/NewPlayer/Attack/Recovery20/NewKnight_Charged_Upward_Slash_Return20_Compressed.anim";
  if(AssetDatabase.LoadAssetAtPath<AnimationClip>(path)!=null)throw new Exception("Already exists");
  var bindings=AnimationUtility.GetCurveBindings(old);var curves=bindings.Select(b=>AnimationUtility.GetEditorCurve(old,b)).ToArray();
  var clip=new AnimationClip{name="NewKnight_Charged_Upward_Slash_Return20_Compressed",frameRate=30,wrapMode=old.wrapMode};
  AnimationUtility.SetEditorCurves(clip,bindings,curves.Select(x=>Retime(x,30)).ToArray());
  foreach(var b in AnimationUtility.GetObjectReferenceCurveBindings(old)){var ks=AnimationUtility.GetObjectReferenceCurve(old,b);for(int i=0;i<ks.Length;i++)ks[i].time=Map(ks[i].time*30)/30;AnimationUtility.SetObjectReferenceCurve(clip,b,ks);}
  var events=AnimationUtility.GetAnimationEvents(old);foreach(var e in events)e.time=Map(e.time*30)/30;AnimationUtility.SetAnimationEvents(clip,events);
  var settings=AnimationUtility.GetAnimationClipSettings(old);settings.stopTime=Map(old.length*30)/30;AnimationUtility.SetAnimationClipSettings(clip,settings);
  float error=0;for(int i=0;i<bindings.Length;i++){var result=AnimationUtility.GetEditorCurve(clip,bindings[i]);for(float f=0;f<=115;f+=.25f)error=Mathf.Max(error,Mathf.Abs(curves[i].Evaluate(f/30)-result.Evaluate(Map(f)/30)));}
  if(error>.002f||Mathf.Abs(clip.length*30-100)>.01f)throw new Exception("Retime validation failed "+error);
  // Restore only Attack 02, leaving all other current user settings untouched.
  var c=UnityEngine.Object.FindFirstObjectByType<NewPlayerAttackCombo>();var set=c.motions;
  var backup=ScriptableObject.CreateInstance<NewPlayerAttackSet>();
  EditorJsonUtility.FromJsonOverwrite(System.IO.File.ReadAllText("Assets/3DBattle/Authoring~/AttackSetBeforeChargedCompression.json"),backup);
  var restored=backup.attacks[1];
  restored.clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/NewPlayer/PlayableAttacks/NewPlayer_Attack02.anim");
  if(Mathf.Abs(restored.Frames-94)>.01f||Mathf.Abs(restored.chainFrame-21)>.01f)throw new Exception("Backup invalid");
  var liveController=c.GetComponent<BattlePlayerController>().animator.runtimeAnimatorController as AnimatorController;
  var wrong=set.attacks[1].clip;
  AssetDatabase.CreateAsset(clip,path);
  int previewReplaced=0,liveReplaced=0;
  foreach(var layer in controller.layers)previewReplaced+=Replace(layer.stateMachine,old,clip);
  foreach(var layer in liveController.layers)liveReplaced+=Replace(layer.stateMachine,wrong,restored.clip);
  set.attacks[1]=restored;
  UnityEngine.Object.DestroyImmediate(backup);
  EditorUtility.SetDirty(set);EditorUtility.SetDirty(controller);EditorUtility.SetDirty(liveController);AssetDatabase.SaveAssets();
  if(previewReplaced!=1||liveReplaced!=1)throw new Exception("Unexpected binding count");
  return new{passed=true,previewFrames=clip.length*30,compressedStart=2,compressedEnd=5,previousEnd=20,maximumCurveError=error,playableRestoredFrames=restored.Frames,playableChain=restored.chainFrame,playableDamage=restored.damageWindows.Select(w=>w.ToString()).ToArray(),path};
 }
}
