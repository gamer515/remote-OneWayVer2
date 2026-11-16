using System;using System.Linq;using UnityEngine;using UnityEditor;using OneWay.Battle3D;
public static class KeepPrimaryWeaponJump
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play mode first");
  var player=GameObject.Find("New Player - Attack Combo").GetComponent<NewPlayerAttackCombo>();var set=player.motions;var motion=set.attacks[5];var original=motion.jump.keys;int peak=Array.FindIndex(original,k=>k.value==original.Max(v=>v.value));int first=peak,last=peak;while(first>0&&original[first-1].value>.000001f)first--;while(last<original.Length-1&&original[last+1].value>.000001f)last++;
  var assetPath=AssetDatabase.GetAssetPath(set);var backup="Assets/3DBattle/Authoring~/WeaponJumpBeforeSingleArc.json";if(!System.IO.File.Exists(backup))System.IO.File.WriteAllText(backup,Newtonsoft.Json.JsonConvert.SerializeObject(original.Select(k=>new{frame=k.time,height=k.value}),Newtonsoft.Json.Formatting.Indented));
  var corrected=original.ToArray();for(int i=0;i<corrected.Length;i++){var k=corrected[i];if(i<first||i>last)k.value=0;else if(i>peak)k.value=Mathf.Min(k.value,corrected[i-1].value);k.inTangent=k.outTangent=0;corrected[i]=k;}
  var curve=new AnimationCurve(corrected);for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
  int arcs=0;bool air=false;for(float f=0;f<=motion.Frames;f+=.05f){bool next=curve.Evaluate(f)>.000001f;if(next&&!air)arcs++;air=next;}
  if(arcs!=1||Mathf.Abs(curve.Evaluate(original[peak].time)-original[peak].value)>.000001f)throw new Exception("Jump verification failed");Undo.RecordObject(set,"Remove small extra weapon jumps");motion.jump=curve;EditorUtility.SetDirty(set);AssetDatabase.SaveAssetIfDirty(set);
  return new{saved=assetPath,airborneArcs=arcs,mainStart=original[first].time,mainEnd=original[last].time,peakFrame=original[peak].time,peakHeight=original[peak].value,multiplierPreserved=player.jumpHeightMultiplier,initialHeight=curve.Evaluate(0),earlyMax=Enumerable.Range(0,20).Max(i=>curve.Evaluate(i*.5f)),recoveryMax=Enumerable.Range(0,80).Max(i=>curve.Evaluate(39+i*.5f))};
 }
}
