using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
public static class ValidateB2ApproachDash
{
 public static object Run()
 {
  const string folder="Assets/3DBattle/Boss2/BossAnim/Approach/";
  var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"B2_ApproachDash.anim");
  var inPlace=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"B2_ApproachDash_InPlace.anim");
  var travel=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"B2_ApproachDash_Travel.anim");
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"B2_ApproachDash_Preview.prefab");
  var model=UnityEngine.Object.Instantiate(prefab);model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);model.transform.localScale=Vector3.one;
  var animator=model.GetComponent<Animator>();animator.enabled=false;
  var collisions=new List<GameObject>();var meshes=new List<Mesh>();
  try
  {
   var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
   var hips=bones["Hips"];var sword=bones["B2 Holy Sword"];
   var bindings=AnimationUtility.GetCurveBindings(clip);
   var missing=bindings.Where(b=>b.path!=""&&model.transform.Find(b.path)==null).Select(b=>b.path).Distinct().ToArray();
   if(missing.Length>0)throw new Exception("Missing bones: "+string.Join(",",missing));
   if(AnimationUtility.GetAnimationEvents(clip).Length!=0)throw new Exception("Approach must have no attack events");
   if(model.GetComponentsInChildren<MonoBehaviour>(true).Length>0||model.GetComponentsInChildren<Collider>(true).Length>0)throw new Exception("Combat components leaked into preview");
   var tx=AnimationUtility.GetEditorCurve(travel,EditorCurveBinding.FloatCurve("",typeof(Transform),"m_LocalPosition.x"));
   var tz=AnimationUtility.GetEditorCurve(travel,EditorCurveBinding.FloatCurve("",typeof(Transform),"m_LocalPosition.z"));
   var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();
   foreach(var skin in skins)
   {
    var go=new GameObject("Approach collision check");go.transform.SetParent(skin.transform,false);go.transform.localScale=Vector3.one/skin.transform.lossyScale.x;
    go.AddComponent<MeshCollider>();collisions.Add(go);meshes.Add(new Mesh());
   }
   var intersections=new List<float>();float reconstructionError=0,minimumFoot=999,maximumStepRotation=0;
   var monitored=new[]{"Spine","Chest","UpperChest","Head","RightUpperArm","RightLowerArm","RightHand","LeftUpperArm","LeftLowerArm","LeftHand"};
   var previous=new Dictionary<string,Quaternion>();
   for(int i=0;i<=178;i++)
   {
    float time=i/60f;
    clip.SampleAnimation(model,time);var fullPosition=hips.localPosition;
    inPlace.SampleAnimation(model,time);var reconstructed=hips.localPosition+hips.parent.InverseTransformVector(new Vector3(tx.Evaluate(time),0,tz.Evaluate(time)));
    reconstructionError=Mathf.Max(reconstructionError,Vector3.Distance(fullPosition,reconstructed));
    foreach(var name in monitored)
    {
     var q=bones[name].rotation;if(previous.TryGetValue(name,out var old))maximumStepRotation=Mathf.Max(maximumStepRotation,Quaternion.Angle(old,q));previous[name]=q;
    }
    minimumFoot=Mathf.Min(minimumFoot,bones["LeftToes"].position.y,bones["RightToes"].position.y);
    if(i%2!=0)continue;
    for(int k=0;k<skins.Length;k++)
    {
     var mc=collisions[k].GetComponent<MeshCollider>();mc.sharedMesh=null;skins[k].BakeMesh(meshes[k]);mc.sharedMesh=meshes[k];
    }
    Physics.SyncTransforms();
    Vector3 start=sword.TransformPoint(new Vector3(0,0,-.0035f));Vector3 tip=sword.TransformPoint(new Vector3(0,0,.0095f));
    Vector3 blade=tip-start;
    foreach(var go in collisions)if(go.GetComponent<MeshCollider>().Raycast(new Ray(start,blade.normalized),out var hit,blade.magnitude))intersections.Add(i/2f);
   }
   if(reconstructionError>.0001f)throw new Exception("Travel extraction mismatch "+reconstructionError);
   // Check real Animator playback, including its one-second pause and second playback.
   foreach(var go in collisions)UnityEngine.Object.DestroyImmediate(go);collisions.Clear();
   animator.enabled=true;animator.applyRootMotion=false;animator.fireEvents=false;animator.Rebind();animator.Update(0);
   float firstZ=hips.position.z,maxTravel=0;int repeats=0;float lastNormalized=0;
   for(int frame=0;frame<600;frame++)
   {
    animator.Update(1/60f);var state=animator.GetCurrentAnimatorStateInfo(0);
    if(state.normalizedTime<lastNormalized)repeats++;lastNormalized=state.normalizedTime;
    maxTravel=Mathf.Max(maxTravel,hips.position.z-firstZ);
   }
   if(repeats<2||maxTravel<4)throw new Exception("Preview did not play the full dash and repeat: repeats="+repeats+", distance="+maxTravel);
   return new{missingBindings=missing.Length,attackEvents=0,reconstructionError,maximumHalfFrameRotation=maximumStepRotation,minimumToeHeight=minimumFoot,bladeBodyIntersectionFrames=intersections.Distinct().ToArray(),previewRepeats=repeats,previewTravel=maxTravel,clipFrames=Mathf.RoundToInt(clip.length*clip.frameRate)};
  }
  finally{UnityEngine.Object.DestroyImmediate(model);foreach(var go in collisions)if(go)UnityEngine.Object.DestroyImmediate(go);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);}
 }
}
