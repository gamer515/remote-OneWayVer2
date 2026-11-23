using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using OneWay.Battle3D;

// Copy the selected frame's upper-body pose while preserving the existing lower-body animation.
// Run CaptureReference once before Apply. No scene, controller, or production clips are changed.
public static class SetDebugFinisherFrame20Guard
{
 const string ReferencePath="Assets/3DBattle/Authoring~/DebugFinisherFrame20Reference.json";
 const string Folder="Assets/3DBattle/3DBattleDebug/Finisher/";
 const float ReadyTime=13f/30f;
 static readonly string[] Upper={"Chest","UpperChest","Neck","Head","LeftShoulder","LeftUpperArm","LeftLowerArm","LeftHand","RightShoulder","RightUpperArm","RightLowerArm","RightHand","Sword"};
 static readonly string[] Lower={"Armature","Hips","LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes","RightUpperLeg","RightLowerLeg","RightFoot","RightToes"};
 sealed class BonePose { public string name,path;public float[] p,q;[JsonIgnore]public Vector3 Position=>new Vector3(p[0],p[1],p[2]);[JsonIgnore]public Quaternion Rotation=>new Quaternion(q[0],q[1],q[2],q[3]); }
 sealed class Reference {public BonePose[] frame13,frame20,frame26,frame29;}
 static NewPlayerAttackCombo Player()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
  var c=UnityEngine.Object.FindObjectsByType<NewPlayerAttackCombo>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(x=>x.name=="New Player - Attack Combo");
  if(c.gameObject.scene.path!="Assets/Scenes/3DBattleDebugScene.unity")throw new Exception("Debug scene required");return c;
 }
 static BonePose[] Pose(GameObject clone)=>clone.GetComponentsInChildren<Transform>(true).Where(t=>Upper.Contains(t.name)||Lower.Contains(t.name)).Select(t=>new BonePose{name=t.name,path=AnimationUtility.CalculateTransformPath(t,clone.transform),p=new[]{t.localPosition.x,t.localPosition.y,t.localPosition.z},q=new[]{t.localRotation.x,t.localRotation.y,t.localRotation.z,t.localRotation.w}}).ToArray();
 static GameObject Clone(NewPlayerAttackCombo c)
 {
  var live=c.GetComponent<BattlePlayerController>().animator;
  var clone=UnityEngine.Object.Instantiate(live.gameObject);clone.GetComponent<Animator>().enabled=false;
  clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);clone.transform.localScale=live.transform.lossyScale;return clone;
 }
 public static string CaptureReference()
 {
  var c=Player();var clone=Clone(c);var clip=c.motions.attacks[0].clip;
  if(clip.name!="DebugFinisher_RetreatRush")throw new Exception("Unexpected finisher clip");
  try {
   Func<int,BonePose[]> sample=f=>{clip.SampleAnimation(clone,f/30f);return Pose(clone);};
   var reference=new Reference{frame13=sample(13),frame20=sample(20),frame26=sample(26),frame29=sample(29)};
   File.WriteAllText(ReferencePath,JsonConvert.SerializeObject(reference,Formatting.Indented));
   var live=c.GetComponent<BattlePlayerController>().animator;
   File.WriteAllText("Temp/BeforeFrame20GuardLive.json",JsonConvert.SerializeObject(new{scene=c.gameObject.scene.path,dirty=c.gameObject.scene.isDirty,animationMode=AnimationMode.InAnimationMode(),pose=Pose(live.gameObject),windows=Resources.FindObjectsOfTypeAll<AnimationWindow>().Select(w=>new{clip=w.animationClip?w.animationClip.name:null,w.frame,w.previewing,w.recording,w.playing}).ToArray()},Formatting.Indented));
   File.Copy(AssetDatabase.GetAssetPath(clip),"Temp/BeforeFrame20Guard_RetreatRush.anim",true);
   File.Copy(AssetDatabase.GetAssetPath(c.GetComponent<DebugPlayerSpecialCombo>().finisherPreparation),"Temp/BeforeFrame20Guard_Preparation.anim",true);
   return JsonConvert.SerializeObject(new{path=ReferencePath,clip=clip.name,bones=reference.frame26.Length});
  } finally{UnityEngine.Object.DestroyImmediate(clone);}
 }
 static Dictionary<EditorCurveBinding,AnimationCurve> Curves(AnimationClip clip)=>AnimationUtility.GetCurveBindings(clip).ToDictionary(b=>b,b=>AnimationUtility.GetEditorCurve(clip,b));
 static void Replace(Dictionary<EditorCurveBinding,AnimationCurve> curves,BonePose from,BonePose to,float start,float end,bool keepOutside,float holdUntil=-1)
 {
  var qs=new List<Quaternion>();var ps=new List<Vector3>();var times=new List<float>();
  int count=Mathf.CeilToInt((end-start)*120);
  for(int i=0;i<=count;i++){
   float time=Mathf.Lerp(start,end,i/(float)count);float t=holdUntil>=0?(time<=holdUntil?0:Mathf.SmoothStep(0,1,Mathf.InverseLerp(holdUntil,end,time))):Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,end,time));
   var q=Quaternion.Slerp(from.Rotation,to.Rotation,t);if(i>0&&Quaternion.Dot(q,qs[i-1])<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
   times.Add(time);qs.Add(q);ps.Add(Vector3.Lerp(from.Position,to.Position,t));
  }
  for(int channel=0;channel<7;channel++){
   var binding=EditorCurveBinding.FloatCurve(from.path,typeof(Transform),channel<3?"m_LocalPosition."+"xyz"[channel]:"m_LocalRotation."+"xyzw"[channel-3]);
   curves.TryGetValue(binding,out var old);var keys=keepOutside&&old!=null?old.keys.Where(k=>k.time<start-.000001f||k.time>end+.000001f).ToList():new List<Keyframe>();
   for(int i=0;i<times.Count;i++)keys.Add(new Keyframe(times[i],channel<3?ps[i][channel]:qs[i][channel-3]));
   var curve=new AnimationCurve(keys.OrderBy(k=>k.time).ToArray());
   if(old!=null){curve.preWrapMode=old.preWrapMode;curve.postWrapMode=old.postWrapMode;}
   for(int i=0;i<curve.length;i++)if(curve.keys[i].time>=start-.000001f&&curve.keys[i].time<=end+.000001f){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
   curves[binding]=curve;
  }
 }
 static void Save(AnimationClip clip,Dictionary<EditorCurveBinding,AnimationCurve> curves)
 {Undo.RecordObject(clip,"Match frame 20 upper body and frame 26 lower body");AnimationUtility.SetEditorCurves(clip,curves.Keys.ToArray(),curves.Values.ToArray());EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);}
 public static string Apply()
 {
  var c=Player();var reference=JsonConvert.DeserializeObject<Reference>(File.ReadAllText(ReferencePath));
  foreach(var w in Resources.FindObjectsOfTypeAll<AnimationWindow>()){w.playing=false;w.recording=false;w.previewing=false;}
  if(AnimationMode.InAnimationMode())AnimationMode.StopAnimationMode();
  var main=c.motions.attacks[0].clip;var prep=c.GetComponent<DebugPlayerSpecialCombo>().finisherPreparation;
  var mainCurves=Curves(main);
  foreach(var p in reference.frame20.Where(b=>Upper.Contains(b.name)))Replace(mainCurves,p,reference.frame29.Single(b=>b.name==p.name),20f/30f,29f/30f,true,26f/30f);
  Save(main,mainCurves);
  var target=reference.frame26.Select(b=>Upper.Contains(b.name)?reference.frame20.Single(x=>x.name==b.name):b).ToArray();
  var prepCurves=Curves(prep);
  foreach(var p in target){
   Replace(prepCurves,reference.frame13.Single(b=>b.name==p.name),p,0,ReadyTime,false);
   Replace(prepCurves,p,p,ReadyTime,prep.length,true);
  }
  Save(prep,prepCurves);
  return Verify();
 }
 public static string Verify()
 {
  var c=Player();var reference=JsonConvert.DeserializeObject<Reference>(File.ReadAllText(ReferencePath));var clone=Clone(c);
  float upperRotationError=0,lowerRotationError=0,upperPositionError=0,lowerPositionError=0,holdPositionDrift=0,holdRotationDrift=0;
  try {
   c.motions.attacks[0].clip.SampleAnimation(clone,26f/30f);var combined=Pose(clone);
   foreach(var p in combined){var expected=(Upper.Contains(p.name)?reference.frame20:reference.frame26).Single(b=>b.name==p.name);float angle=Quaternion.Angle(p.Rotation,expected.Rotation),distance=Vector3.Distance(p.Position,expected.Position);
    if(Upper.Contains(p.name)){upperRotationError=Mathf.Max(upperRotationError,angle);upperPositionError=Mathf.Max(upperPositionError,distance);}else{lowerRotationError=Mathf.Max(lowerRotationError,angle);lowerPositionError=Mathf.Max(lowerPositionError,distance);}}
   var prep=c.GetComponent<DebugPlayerSpecialCombo>().finisherPreparation;
   for(int i=0;i<=60;i++){prep.SampleAnimation(clone,Mathf.Lerp(ReadyTime,prep.length,i/60f));foreach(var p in Pose(clone)){var expected=combined.Single(b=>b.name==p.name);holdPositionDrift=Mathf.Max(holdPositionDrift,Vector3.Distance(p.Position,expected.Position));holdRotationDrift=Mathf.Max(holdRotationDrift,Quaternion.Angle(p.Rotation,expected.Rotation));}}
   if(upperRotationError>.1f||lowerRotationError>.1f||upperPositionError>.001f||lowerPositionError>.001f||holdPositionDrift>.001f||holdRotationDrift>.1f)throw new Exception("Reference pose or held pose mismatch");
   var result=JsonConvert.SerializeObject(new{passed=true,upperRotationError,lowerRotationError,upperPositionError,lowerPositionError,holdPositionDrift,holdRotationDrift,holdSeconds=c.GetComponent<DebugPlayerSpecialCombo>().finisherHoldSeconds,changed=new[]{AssetDatabase.GetAssetPath(c.motions.attacks[0].clip),AssetDatabase.GetAssetPath(prep)}},Formatting.Indented);
   File.WriteAllText("Assets/3DBattle/Authoring~/DebugFinisherFrame20Verification.json",result);return result;
  } finally{UnityEngine.Object.DestroyImmediate(clone);}
 }
 public static string Preview()
 {
  var c=Player();Selection.activeGameObject=c.GetComponent<BattlePlayerController>().animator.gameObject;
  var w=Resources.FindObjectsOfTypeAll<AnimationWindow>().FirstOrDefault()??EditorWindow.GetWindow<AnimationWindow>();
  w.playing=false;w.recording=false;w.animationClip=c.motions.attacks[0].clip;w.previewing=true;w.frame=26;w.Repaint();SceneView.RepaintAll();
  return JsonConvert.SerializeObject(new{w.frame,w.previewing,w.recording,clip=w.animationClip.name});
 }
}
