using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// Run after SmoothB2ApproachLowerBody. Preserve the first two knee movements,
// then extend both legs directly into the existing final pose without another tuck.
public static class RemoveB2DashLastKneeCycle
{
 const string Folder="Assets/3DBattle/Boss2/BossAnim/Approach/";
 const float Begin=60,End=89;
 static readonly string[] Lower={"Hips","LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes","RightUpperLeg","RightLowerLeg","RightFoot","RightToes"};
 public static object Preview()=>Run(false);
 public static object Apply()=>Run(true);
 static object Run(bool apply)
 {
  if(Application.isPlaying)throw new Exception("Stop Play Mode before editing clips.");
  var full=BattleCharacterAssetPaths.Load<AnimationClip>(Folder+"B2_ApproachDash.anim");
  var inplace=BattleCharacterAssetPaths.Load<AnimationClip>(Folder+"B2_ApproachDash_InPlace.anim");
  string backup=Folder+"Backups/B2_ApproachDash_BeforeThirdKneeRemoval.anim";
  var source=BattleCharacterAssetPaths.Load<AnimationClip>(backup)??full;
  var candidate=UnityEngine.Object.Instantiate(source);
  var model=UnityEngine.Object.Instantiate(BattleCharacterAssetPaths.Load<GameObject>(Folder+"B2_ApproachDash_Preview.prefab"));
  model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);model.transform.localScale=Vector3.one;model.GetComponent<Animator>().enabled=false;
  try
  {
   var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
   var hip=bones["Hips"];
   source.SampleAnimation(model,Begin/30);
   var start=Lower.ToDictionary(n=>n,n=>bones[n].localRotation);float startY=hip.position.y;
   float startFloor=Mathf.Min(bones["LeftToes"].position.y,bones["RightToes"].position.y);
   source.SampleAnimation(model,End/30);
   var finish=Lower.ToDictionary(n=>n,n=>bones[n].localRotation);float endY=hip.position.y;
   float endFloor=Mathf.Min(bones["LeftToes"].position.y,bones["RightToes"].position.y);
   var changed=Lower.Concat(new[]{"Spine"}).ToArray();
   var values=changed.ToDictionary(n=>n,n=>Enumerable.Range(0,4).Select(_=>new List<Keyframe>()).ToArray());
   var hipY=new List<Keyframe>();var previous=new Dictionary<string,Quaternion>();float maxGroundLift=0,minToe=999;
   var frames=new List<object>();
   for(int i=(int)Begin*2;i<=(int)End*2;i++)
   {
    float time=i/60f,t=Mathf.InverseLerp(Begin,End,i/2f),blend=t*t*(3-2*t);
    source.SampleAnimation(model,time);var spineWorld=bones["Spine"].rotation;var originalHip=hip.position;
    foreach(var n in Lower)bones[n].localRotation=Quaternion.Slerp(start[n],finish[n],blend);
    // The pelvis rises once as the knees extend; retain all original horizontal travel.
    float y=Mathf.Lerp(startY,endY,blend);hip.position=new Vector3(originalHip.x,y,originalHip.z);
    float floor=Mathf.Lerp(startFloor,endFloor,blend);
    float low=Mathf.Min(bones["LeftToes"].position.y,bones["RightToes"].position.y);
    float lift=Mathf.Max(0,floor-low);maxGroundLift=Mathf.Max(maxGroundLift,lift);
    hip.position+=Vector3.up*lift;bones["Spine"].rotation=spineWorld;
    minToe=Mathf.Min(minToe,bones["LeftToes"].position.y,bones["RightToes"].position.y);
    hipY.Add(new Keyframe(time,hip.localPosition.y));
    foreach(var n in changed)
    {
     var q=bones[n].localRotation;
     if(!previous.ContainsKey(n))
     {
      string path=AnimationUtility.CalculateTransformPath(bones[n],model.transform);
      float[] original=new float[4];for(int k=0;k<4;k++)original[k]=AnimationUtility.GetEditorCurve(source,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[k])).Evaluate(time);
      previous[n]=new Quaternion(original[0],original[1],original[2],original[3]);
     }
     if(Quaternion.Dot(previous[n],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[n]=q;
     float[] v={q.x,q.y,q.z,q.w};for(int k=0;k<4;k++)values[n][k].Add(new Keyframe(time,v[k]));
    }
    if(i%6==0||i==178)frames.Add(new{frame=i/2f,leftBend=Bend(bones,"Left"),rightBend=Bend(bones,"Right"),hipY=hip.position.y});
   }
   foreach(var n in changed)
   {
    string path=AnimationUtility.CalculateTransformPath(bones[n],model.transform);
    for(int k=0;k<4;k++)ReplaceTail(candidate,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[k]),values[n][k]);
   }
   ReplaceTail(candidate,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(hip,model.transform),typeof(Transform),"m_LocalPosition.y"),hipY);
   candidate.EnsureQuaternionContinuity();
   // Check the actual resampled clip, including interpolation between baked keys.
   float maximumRebend=0,earlyPoseError=0,upperAngleError=0;float[] last={999,999};
   var upper=new[]{"Spine","Chest","UpperChest","Head","LeftHand","RightHand"};
   var originalBindings=AnimationUtility.GetCurveBindings(source);
   for(int i=0;i<=356;i++)
   {
    float time=i/120f;source.SampleAnimation(model,time);
    var poses=bones.ToDictionary(p=>p.Key,p=>p.Value.localRotation);
    var worlds=upper.ToDictionary(n=>n,n=>bones[n].rotation);
    candidate.SampleAnimation(model,time);
    if(time<=Begin/30f)foreach(var n in changed)earlyPoseError=Mathf.Max(earlyPoseError,Quaternion.Angle(poses[n],bones[n].localRotation));
    foreach(var n in upper)upperAngleError=Mathf.Max(upperAngleError,Quaternion.Angle(worlds[n],bones[n].rotation));
    if(time>=Begin/30f)for(int s=0;s<2;s++){float bend=Bend(bones,s==0?"Left":"Right");maximumRebend=Mathf.Max(maximumRebend,bend-last[s]);last[s]=bend;}
   }
   if(maximumRebend>.1f||earlyPoseError>.1f||upperAngleError>.1f)throw new Exception("Pose verification failed: rebend="+maximumRebend+", early="+earlyPoseError+", upper="+upperAngleError);
   if(apply)
   {
    if(AnimationMode.InAnimationMode())AnimationMode.StopAnimationMode();
    foreach(var window in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w=>w.GetType().Name=="AnimationWindow"))window.Close();
    if(!BattleCharacterAssetPaths.Load<AnimationClip>(backup))AssetDatabase.CopyAsset(Folder+"B2_ApproachDash.anim",backup);
    string ib=Folder+"Backups/B2_ApproachDash_InPlace_BeforeThirdKneeRemoval.anim";
    if(!BattleCharacterAssetPaths.Load<AnimationClip>(ib))AssetDatabase.CopyAsset(Folder+"B2_ApproachDash_InPlace.anim",ib);
    Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{full,inplace},"Remove final B2 dash knee tuck");
    foreach(var binding in AnimationUtility.GetCurveBindings(candidate))
    {
     bool rotation=changed.Any(n=>binding.path==AnimationUtility.CalculateTransformPath(bones[n],model.transform))&&binding.propertyName.StartsWith("m_LocalRotation.");
     bool vertical=binding.path==AnimationUtility.CalculateTransformPath(hip,model.transform)&&binding.propertyName=="m_LocalPosition.y";
     if(!rotation&&!vertical)continue;
     var curve=AnimationUtility.GetEditorCurve(candidate,binding);AnimationUtility.SetEditorCurve(full,binding,curve);AnimationUtility.SetEditorCurve(inplace,binding,curve);
    }
    full.EnsureQuaternionContinuity();inplace.EnsureQuaternionContinuity();EditorUtility.SetDirty(full);EditorUtility.SetDirty(inplace);AssetDatabase.SaveAssets();
    var preview=GameObject.Find("B2 - Approach Dash Preview");if(preview)Selection.activeGameObject=preview;
   }
   return new{applied=apply,startFrame=Begin,endFrame=End,duration=candidate.length,maximumRebend,earlyPoseError,upperAngleError,maxGroundLift,minToe,frames};
  }
  finally{UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(candidate);}
 }
 static float Bend(Dictionary<string,Transform>b,string s)=>180-Vector3.Angle(b[s+"UpperLeg"].position-b[s+"LowerLeg"].position,b[s+"Foot"].position-b[s+"LowerLeg"].position);
 static void ReplaceTail(AnimationClip clip,EditorCurveBinding binding,List<Keyframe> tail)
 {
  var old=AnimationUtility.GetEditorCurve(clip,binding);var keys=old.keys.Where(k=>k.time<Begin/30f-.00001f).ToList();int prefix=keys.Count;
  var originalStart=old.keys.OrderBy(k=>Mathf.Abs(k.time-Begin/30f)).First();keys.AddRange(tail);
  var curve=new AnimationCurve(keys.ToArray()){preWrapMode=old.preWrapMode,postWrapMode=old.postWrapMode};
  for(int i=prefix;i<curve.length;i++)
  {
   AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.ClampedAuto);
  }
  var first=curve.keys[prefix];first.inTangent=originalStart.inTangent;curve.MoveKey(prefix,first);
  AnimationUtility.SetEditorCurve(clip,binding,curve);
 }
}
