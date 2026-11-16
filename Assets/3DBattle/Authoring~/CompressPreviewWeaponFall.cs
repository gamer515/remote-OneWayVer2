using System;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.Animations;
public static class CompressPreviewWeaponFall
{
 static float Map(float f)=>f<=19?f:f<=32?19+(f-19)*4f/13f:f-9;
 static AnimationCurve Retime(AnimationCurve source){var keys=source.keys;for(int i=0;i<keys.Length;i++){var k=keys[i];float f=k.time*30;k.time=Map(f)/30;if(f>19.00001f&&f<=32.00001f)k.inTangent*=3.25f;if(f>=18.99999f&&f<31.99999f)k.outTangent*=3.25f;keys[i]=k;}return new AnimationCurve(keys){preWrapMode=source.preWrapMode,postWrapMode=source.postWrapMode};}
 static int Replace(AnimatorStateMachine sm,AnimationClip old,AnimationClip clip){int count=0;foreach(var s in sm.states)if(s.state.motion==old){s.state.motion=clip;EditorUtility.SetDirty(s.state);count++;}foreach(var s in sm.stateMachines)count+=Replace(s.stateMachine,old,clip);return count;}
 public static object Run(){
  if(EditorApplication.isPlaying)throw new Exception("Stop play first");
  var animator=GameObject.Find("Attack 06 - Weapon_Combo_1").GetComponentInChildren<Animator>();var controller=animator.runtimeAnimatorController as AnimatorController;var old=controller.animationClips.Single();
  if(Mathf.Abs(old.length*30-81)>.01f)throw new Exception("Unexpected source duration");
  const string path="Assets/3DBattle/NewPlayer/Attack/Recovery20/NewKnight_Weapon_Combo_1_Return20_FastFall.anim";
  if(AssetDatabase.LoadAssetAtPath<AnimationClip>(path)!=null)throw new Exception("Output already exists");
  var bindings=AnimationUtility.GetCurveBindings(old);var curves=bindings.Select(b=>AnimationUtility.GetEditorCurve(old,b)).ToArray();
  var clip=new AnimationClip{name="NewKnight_Weapon_Combo_1_Return20_FastFall",frameRate=30,wrapMode=old.wrapMode};
  AnimationUtility.SetEditorCurves(clip,bindings,curves.Select(Retime).ToArray());
  foreach(var b in AnimationUtility.GetObjectReferenceCurveBindings(old)){var keys=AnimationUtility.GetObjectReferenceCurve(old,b);for(int i=0;i<keys.Length;i++)keys[i].time=Map(keys[i].time*30)/30;AnimationUtility.SetObjectReferenceCurve(clip,b,keys);}
  var events=AnimationUtility.GetAnimationEvents(old);foreach(var e in events)e.time=Map(e.time*30)/30;AnimationUtility.SetAnimationEvents(clip,events);
  var settings=AnimationUtility.GetAnimationClipSettings(old);settings.stopTime=Map(old.length*30)/30;AnimationUtility.SetAnimationClipSettings(clip,settings);
  float error=0;for(int i=0;i<bindings.Length;i++){var result=AnimationUtility.GetEditorCurve(clip,bindings[i]);for(float f=0;f<=81;f+=.125f)error=Mathf.Max(error,Mathf.Abs(curves[i].Evaluate(f/30)-result.Evaluate(Map(f)/30)));}
  if(error>.002f||Mathf.Abs(clip.length*30-72)>.01f)throw new Exception("Validation failed "+error);
  AssetDatabase.CreateAsset(clip,path);int replaced=0;foreach(var layer in controller.layers)replaced+=Replace(layer.stateMachine,old,clip);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
  if(replaced!=1)throw new Exception("Unexpected reference count "+replaced);
  return new{passed=true,oldFrames=81,newFrames=clip.length*30,range="19-32 -> 19-23",curves=bindings.Length,maximumCurveError=error,original=AssetDatabase.GetAssetPath(old),output=path};
 }
}
