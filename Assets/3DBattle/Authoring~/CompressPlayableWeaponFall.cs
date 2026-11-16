using System;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.Animations;using OneWay.Battle3D;
public static class CompressPlayableWeaponFall
{
 static float Map(float f)=>f<=16?f:f<=29?16+(f-16)*4f/13f:f-9;
 static AnimationCurve Retime(AnimationCurve source,float units){
  var keys=source.keys;
  for(int i=0;i<keys.Length;i++){
   var k=keys[i];float f=k.time*units;
   k.time=Map(f)/units;
   if(f>16.00001f&&f<=29.00001f)k.inTangent*=3.25f;
   if(f>=15.99999f&&f<28.99999f)k.outTangent*=3.25f;
   keys[i]=k;
  }
  return new AnimationCurve(keys){preWrapMode=source.preWrapMode,postWrapMode=source.postWrapMode};
 }
 static int Replace(AnimatorStateMachine sm,AnimationClip old,AnimationClip clip){int n=0;foreach(var state in sm.states)if(state.state.motion==old){state.state.motion=clip;state.state.name=clip.name;EditorUtility.SetDirty(state.state);n++;}foreach(var child in sm.stateMachines)n+=Replace(child.stateMachine,old,clip);return n;}
 public static object Run(){
  if(EditorApplication.isPlaying)throw new Exception("Stop play first");
  var c=UnityEngine.Object.FindFirstObjectByType<NewPlayerAttackCombo>();var set=c.motions;var m=set.attacks[5];var old=m.clip;
  const string path="Assets/3DBattle/NewPlayer/PlayableAttacks/NewPlayer_Attack06_FastFall.anim";
  if(AssetDatabase.LoadAssetAtPath<AnimationClip>(path)!=null)throw new Exception("Already compressed");
  if(Mathf.Abs(m.Frames-78)>.01f)throw new Exception("Unexpected source duration");
  var controller=c.GetComponent<BattlePlayerController>().animator.runtimeAnimatorController as AnimatorController;if(controller==null)throw new Exception("Controller missing");
  System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/AttackSetBeforeWeaponFallCompression.json",EditorJsonUtility.ToJson(set,true));
  var bindings=AnimationUtility.GetCurveBindings(old);var curves=bindings.Select(b=>AnimationUtility.GetEditorCurve(old,b)).ToArray();
  var clip=new AnimationClip{name=old.name,frameRate=old.frameRate,wrapMode=old.wrapMode};
  AnimationUtility.SetEditorCurves(clip,bindings,curves.Select(x=>Retime(x,30)).ToArray());
  foreach(var b in AnimationUtility.GetObjectReferenceCurveBindings(old)){var ks=AnimationUtility.GetObjectReferenceCurve(old,b);for(int i=0;i<ks.Length;i++)ks[i].time=Map(ks[i].time*30)/30;AnimationUtility.SetObjectReferenceCurve(clip,b,ks);}
  var events=AnimationUtility.GetAnimationEvents(old);foreach(var e in events)e.time=Map(e.time*30)/30;AnimationUtility.SetAnimationEvents(clip,events);
  var settings=AnimationUtility.GetAnimationClipSettings(old);settings.stopTime=Map(old.length*30)/30;AnimationUtility.SetAnimationClipSettings(clip,settings);
  float error=0;
  for(int i=0;i<bindings.Length;i++){var result=AnimationUtility.GetEditorCurve(clip,bindings[i]);for(float f=0;f<=78;f+=.25f)error=Mathf.Max(error,Mathf.Abs(curves[i].Evaluate(f/30)-result.Evaluate(Map(f)/30)));}
  if(error>.002f)throw new Exception("Pose mismatch "+error);
  if(Mathf.Abs(clip.length*30-69)>.01f)throw new Exception("Wrong duration");
  AssetDatabase.CreateAsset(clip,path);
  int replaced=0;foreach(var layer in controller.layers)replaced+=Replace(layer.stateMachine,old,clip);
  if(replaced!=1)throw new Exception("Unexpected controller replacements "+replaced);
  m.clip=clip;m.chainFrame=Map(m.chainFrame);m.damageWindows=m.damageWindows.Select(w=>new Vector2(Map(w.x),Map(w.y))).ToArray();m.forward=Retime(m.forward,1);m.sideways=Retime(m.sideways,1);m.jump=Retime(m.jump,1);
  EditorUtility.SetDirty(set);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
  return new{passed=true,frames=m.Frames,chain=m.chainFrame,damage=m.damageWindows.Select(x=>x.ToString()).ToArray(),maximumCurveError=error,curves=bindings.Length,original=AssetDatabase.GetAssetPath(old),output=path};
 }
}


