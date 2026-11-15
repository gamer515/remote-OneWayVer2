using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

// Post-process only the approach clips. Run after BuildB2ApproachDash when rebuilding from the FBX.
public static class SmoothB2ApproachLowerBody
{
 const string Folder="Assets/3DBattle/Boss2/BossAnim/Approach/";
 const string Backup=Folder+"Backups/B2_ApproachDash_BeforeLowerBodySmoothing.anim";
 static readonly string[] Lower={"Hips","LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes","RightUpperLeg","RightLowerLeg","RightFoot","RightToes"};
 static float Smooth(float a,float b,float f)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,f));
 static float Sigma(float frame)
 {
  float settle=Smooth(27,35,frame)*(1-Smooth(60,68,frame));
  return Mathf.Lerp(1.05f,3.4f,settle)*2; // Samples at 60 Hz, authored frames at 30 Hz.
 }
 static float Strength(float frame)=>Smooth(0,3,frame)*(1-Smooth(86,89,frame));
 static Vector3 Filter(Vector3[] values,int index,float sigma)
 {
  Vector3 sum=Vector3.zero;float weights=0;int radius=Mathf.CeilToInt(sigma*2.5f);
  for(int j=Mathf.Max(0,index-radius);j<=Mathf.Min(values.Length-1,index+radius);j++)
  {float w=Mathf.Exp(-.5f*(j-index)*(j-index)/(sigma*sigma));sum+=values[j]*w;weights+=w;}
  return Vector3.Lerp(values[index],sum/weights,Strength(index/2f));
 }
 static Quaternion Filter(Quaternion[] values,int index,float sigma)
 {
  Quaternion reference=values[index];Vector4 sum=Vector4.zero;int radius=Mathf.CeilToInt(sigma*2.5f);
  for(int j=Mathf.Max(0,index-radius);j<=Mathf.Min(values.Length-1,index+radius);j++)
  {
   var q=values[j];float w=Mathf.Exp(-.5f*(j-index)*(j-index)/(sigma*sigma));if(Quaternion.Dot(reference,q)<0)w=-w;
   sum+=new Vector4(q.x,q.y,q.z,q.w)*w;
  }
  sum.Normalize();return Quaternion.Slerp(reference,new Quaternion(sum.x,sum.y,sum.z,sum.w),Strength(index/2f));
 }
 static void Leg(Dictionary<string,Transform> b,string side,Vector3 ankle,Quaternion footRotation)
 {
  var upper=b[side+"UpperLeg"];var lower=b[side+"LowerLeg"];var foot=b[side+"Foot"];
  Vector3 u=upper.position,k=lower.position,a=foot.position;
  float l1=Vector3.Distance(u,k),l2=Vector3.Distance(k,a);
  Vector3 direction=(ankle-u).normalized;
  float d=Mathf.Clamp(Vector3.Distance(u,ankle),Mathf.Abs(l1-l2)+.0001f,(l1+l2)*.999f);
  Vector3 pole=Vector3.ProjectOnPlane(k-u,direction).normalized;
  if(pole.sqrMagnitude<.5f)pole=Vector3.ProjectOnPlane(Vector3.forward,direction).normalized;
  float along=(l1*l1-l2*l2+d*d)/(2*d);
  Vector3 knee=u+direction*along+pole*Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
  upper.rotation=Quaternion.FromToRotation(k-u,knee-u)*upper.rotation;
  lower.rotation=Quaternion.FromToRotation(foot.position-lower.position,u+direction*d-lower.position)*lower.rotation;
  foot.rotation=footRotation;
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop Play Mode before editing clips");
  // Avoid stale Animation-window key selections while replacing curves.
  bool wasPreview=AnimationMode.InAnimationMode();if(wasPreview)AnimationMode.StopAnimationMode();
  var windows=Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w=>w.GetType().Name=="AnimationWindow").ToArray();
  foreach(var window in windows)window.Close();
  if(!AssetDatabase.IsValidFolder(Folder+"Backups"))AssetDatabase.CreateFolder(Folder.TrimEnd('/'),"Backups");
  if(!AssetDatabase.LoadAssetAtPath<AnimationClip>(Backup))AssetDatabase.CopyAsset(Folder+"B2_ApproachDash.anim",Backup);
  if(!AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"Backups/B2_ApproachDash_InPlace_BeforeLowerBodySmoothing.anim"))AssetDatabase.CopyAsset(Folder+"B2_ApproachDash_InPlace.anim",Folder+"Backups/B2_ApproachDash_InPlace_BeforeLowerBodySmoothing.anim");
  var original=AssetDatabase.LoadAssetAtPath<AnimationClip>(Backup);
  var full=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"B2_ApproachDash.anim");
  var inPlace=AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"B2_ApproachDash_InPlace.anim");
  var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"B2_ApproachDash_Preview.prefab"));
  go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);go.transform.localScale=Vector3.one;go.GetComponent<Animator>().enabled=false;
  try
  {
   var bones=go.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
   int count=Mathf.RoundToInt(original.length*60)+1;
   var rotations=Lower.ToDictionary(n=>n,n=>new Quaternion[count]);
   var hipPositions=new Vector3[count];var upperWorld=new Quaternion[count];
   var footPositions=new[]{new Vector3[count],new Vector3[count]};var footWorld=new[]{new Quaternion[count],new Quaternion[count]};
   for(int i=0;i<count;i++)
   {
    original.SampleAnimation(go,i/60f);var hip=bones["Hips"].position;hipPositions[i]=hip;
    upperWorld[i]=bones["Spine"].rotation;
    foreach(string n in Lower)rotations[n][i]=bones[n].localRotation;
    for(int s=0;s<2;s++)
    {
     var foot=bones[s==0?"LeftFoot":"RightFoot"];footPositions[s][i]=foot.position-new Vector3(hip.x,0,hip.z);footWorld[s][i]=foot.rotation;
    }
   }
   var changed=Lower.Concat(new[]{"Spine"}).ToArray();
   var output=changed.ToDictionary(n=>n,n=>Enumerable.Range(0,4).Select(_=>new AnimationCurve()).ToArray());
   var hipY=new AnimationCurve();var previous=new Dictionary<string,Quaternion>();float maxAnkleError=0,maxVerticalChange=0,upperRotationError=0;
   for(int i=0;i<count;i++)
   {
    original.SampleAnimation(go,i/60f);float sigma=Sigma(i/2f);
    foreach(string n in Lower)bones[n].localRotation=Filter(rotations[n],i,sigma);
    var hip=bones["Hips"];var pos=hipPositions[i];float y=Filter(hipPositions,i,sigma).y;
    maxVerticalChange=Mathf.Max(maxVerticalChange,Mathf.Abs(y-pos.y));hip.position=new Vector3(pos.x,y,pos.z);
    // Counter the pelvis smoothing at the spine so the already-corrected upper body keeps its orientation.
    bones["Spine"].rotation=upperWorld[i];
    for(int s=0;s<2;s++)
    {
     string side=s==0?"Left":"Right";
     var ankle=Filter(footPositions[s],i,sigma)+new Vector3(pos.x,0,pos.z);
     Leg(bones,side,ankle,Filter(footWorld[s],i,sigma));
     maxAnkleError=Mathf.Max(maxAnkleError,Vector3.Distance(ankle,bones[side+"Foot"].position));
    }
    upperRotationError=Mathf.Max(upperRotationError,Quaternion.Angle(bones["Spine"].rotation,upperWorld[i]));
    hipY.AddKey(new Keyframe(i/60f,hip.localPosition.y));
    foreach(string n in changed)
    {
     var q=bones[n].localRotation;if(previous.TryGetValue(n,out var prev)&&Quaternion.Dot(prev,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[n]=q;
     float[] values={q.x,q.y,q.z,q.w};for(int k=0;k<4;k++)output[n][k].AddKey(new Keyframe(i/60f,values[k]));
    }
   }
   Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[]{full,inPlace},"Smooth B2 approach lower body");
   foreach(var pair in output)
   {
    string path=AnimationUtility.CalculateTransformPath(bones[pair.Key],go.transform);
    for(int k=0;k<4;k++)
    {
     var curve=pair.Value[k];SetTangents(curve);
     var binding=EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+"xyzw"[k]);
     AnimationUtility.SetEditorCurve(full,binding,curve);AnimationUtility.SetEditorCurve(inPlace,binding,curve);
    }
   }
   SetTangents(hipY);var yBinding=EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones["Hips"],go.transform),typeof(Transform),"m_LocalPosition.y");
   AnimationUtility.SetEditorCurve(full,yBinding,hipY);AnimationUtility.SetEditorCurve(inPlace,yBinding,hipY);
   full.EnsureQuaternionContinuity();inPlace.EnsureQuaternionContinuity();EditorUtility.SetDirty(full);EditorUtility.SetDirty(inPlace);AssetDatabase.SaveAssets();
   var result=new{duration=full.length,maxAnkleError,maxVerticalChange,upperRotationError,wasPreview,closedAnimationWindows=windows.Length,before=Jitter(go,original),after=Jitter(go,full)};
   System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/B2DashSmoothingReport.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
   var preview=GameObject.Find("B2 - Approach Dash Preview");if(preview)Selection.activeGameObject=preview;
   return result;
  }
  finally{UnityEngine.Object.DestroyImmediate(go);}
 }
 static void SetTangents(AnimationCurve curve)
 {
  for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.ClampedAuto);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.ClampedAuto);}
 }
 static object[] Jitter(GameObject model,AnimationClip clip)
 {
  var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
  var rotations=Lower.ToDictionary(n=>n,n=>new Quaternion[90]);var hipY=new float[90];
  for(int f=0;f<=89;f++){clip.SampleAnimation(model,f/30f);foreach(string n in Lower)rotations[n][f]=bones[n].localRotation;hipY[f]=bones["Hips"].position.y;}
  return Lower.Select(n=>new{bone=n,settleJitter=Enumerable.Range(32,31).Average(f=>Quaternion.Angle(rotations[n][f],Quaternion.Slerp(rotations[n][f-1],rotations[n][f+1],.5f))),maxFrameRotation=Enumerable.Range(1,89).Max(f=>Quaternion.Angle(rotations[n][f-1],rotations[n][f])),verticalJitter=n=="Hips"?Enumerable.Range(32,31).Average(f=>Mathf.Abs(hipY[f]-(hipY[f-1]+hipY[f+1])*.5f)):0}).Cast<object>().ToArray();
 }
}
