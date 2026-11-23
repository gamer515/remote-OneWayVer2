using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;

public static class FixB2Skill03Entry
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var controller=BattleCharacterAssetPaths.Load<AnimatorController>("Assets/3DBattle/Boss2/BossAnim/Crisp/B2_CrispSequence.controller");
  var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Skill03").state;
  var clip=(AnimationClip)state.motion;
  var combo=UnityEngine.Object.FindObjectsByType<NewPlayerAttackCombo>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(x=>x.gameObject.activeInHierarchy);
  var source=combo.motions.attacks[4].clip;
  float fps=clip.frameRate;
  var incoming=controller.layers[0].stateMachine.states.SelectMany(s=>s.state.transitions).Where(t=>t.destinationState==state).ToArray();
  if(incoming.Any(t=>!t.hasFixedDuration||t.offset!=0))throw new Exception("Unexpected incoming transition configuration");
  int prefix=Mathf.CeilToInt(incoming.Max(t=>t.duration)*state.speed*fps)+2;
  int presentPrefix=Mathf.RoundToInt((clip.length-source.length)*fps);
  if(presentPrefix==prefix)return new{alreadyFixed=true,entryFrames=prefix};
  if(presentPrefix!=0)throw new Exception("Unexpected clip length: "+clip.length);
  float shift=prefix/fps;
  Undo.RecordObject(clip,"Show full B2 Skill03 entry");
  var bindings=AnimationUtility.GetCurveBindings(clip);
  var originals=bindings.Select(b=>AnimationUtility.GetEditorCurve(clip,b)).ToArray();
  for(int i=0;i<bindings.Length;i++)
  {
   var original=originals[i];var keys=original.keys;
   if(Mathf.Abs(keys[0].time)>0.00001f)throw new Exception("Missing first frame");
   for(int j=0;j<keys.Length;j++){keys[j].time+=shift;}
   keys[0].inTangent=0;
   var all=new Keyframe[keys.Length+1];all[0]=new Keyframe(0,keys[0].value,0,0);Array.Copy(keys,0,all,1,keys.Length);
   var curve=new AnimationCurve(all){preWrapMode=original.preWrapMode,postWrapMode=original.postWrapMode};
   AnimationUtility.SetEditorCurve(clip,bindings[i],curve);
  }
  var events=AnimationUtility.GetAnimationEvents(clip);foreach(var e in events)e.time+=shift;AnimationUtility.SetAnimationEvents(clip,events);
  var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.startTime=0;settings.stopTime=source.length+shift;settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
  float maxError=0;
  for(int i=0;i<bindings.Length;i++)
  {
   var actual=AnimationUtility.GetEditorCurve(clip,bindings[i]);
   for(int j=0;j<=144;j++){float time=source.length*j/144f;maxError=Mathf.Max(maxError,Mathf.Abs(originals[i].Evaluate(time)-actual.Evaluate(time+shift)));}
  }
  if(maxError>.001f)throw new Exception("Original motion changed: "+maxError);
  EditorUtility.SetDirty(clip);AssetDatabase.SaveAssets();
  return new{entryFrames=prefix,sourceFrames=source.length*fps,totalFrames=clip.length*fps,maxOriginalCurveError=maxError,slashFrame=events.Single().time*fps,stateSpeed=state.speed,transitionSeconds=incoming.Max(t=>t.duration)};
 }
}
