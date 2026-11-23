using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;
public static class BuildB2CrispMotions
{
 const string Folder="Assets/3DBattle/Boss2/BossAnim/Crisp";
 static string Canon(string n)
 {
  n=n.Replace("mixamorig:","");
  if(n=="Spine1")return "Chest";if(n=="Spine2")return "UpperChest";if(n=="HeadTop_End"||n=="head_end")return "HeadEnd";
  foreach(var side in new[]{"Left","Right"}){if(n==side+"UpLeg")return side+"UpperLeg";if(n==side+"Leg")return side+"LowerLeg";if(n==side+"Arm")return side+"UpperArm";if(n==side+"ForeArm")return side+"LowerArm";if(n==side+"ToeBase")return side+"Toes";if(n==side+"Toe_End")return side+"ToesEnd";foreach(var f in new[]{"Thumb","Middle"})for(int i=1;i<=4;i++)if(n==side+"Hand"+f+i)return side+f+new[]{"Proximal","Intermediate","Distal","DistalEnd"}[i-1];}return n;
 }
 static Dictionary<string,Matrix4x4> Rest(GameObject g)
 {
  var result=new Dictionary<string,Matrix4x4>();
  foreach(var r in g.GetComponentsInChildren<SkinnedMeshRenderer>())for(int i=0;i<r.bones.Length;i++)if(r.bones[i])result[Canon(r.bones[i].name)]=g.transform.worldToLocalMatrix*r.transform.localToWorldMatrix*r.sharedMesh.bindposes[i].inverse;
  foreach(var t in g.GetComponentsInChildren<Transform>())if(!result.ContainsKey(Canon(t.name)))result[Canon(t.name)]=g.transform.worldToLocalMatrix*t.localToWorldMatrix;
  return result;
 }
 static float Map(Vector2[] knots,float frame)
 {for(int i=1;i<knots.Length;i++)if(frame<=knots[i].x)return Mathf.Lerp(knots[i-1].y,knots[i].y,Mathf.InverseLerp(knots[i-1].x,knots[i].x,frame));return knots.Last().y;}
 static float EventFrame(Vector2[] knots,float source){for(int i=1;i<knots.Length;i++)if(source<=knots[i].y&&knots[i].y>knots[i-1].y)return Mathf.Lerp(knots[i-1].x,knots[i].x,Mathf.InverseLerp(knots[i-1].y,knots[i].y,source));return knots.Last().x;}
 static void Save(UnityEngine.Object obj,string path){var old=BattleCharacterAssetPaths.Load<UnityEngine.Object>(path);if(old){EditorUtility.CopySerialized(obj,old);UnityEngine.Object.DestroyImmediate(obj);}else AssetDatabase.CreateAsset(obj,path);}
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/3DBattle/Boss2/BossAnim","Crisp");
  var sceneAnimator=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(a=>a.name.Contains("Heavy_Hammer")&&a.gameObject.activeInHierarchy);
  var targetAsset=BattleCharacterAssetPaths.Load<GameObject>(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(sceneAnimator.gameObject)));
  var paths=AssetDatabase.FindAssets("t:Model",new[]{"Assets/3DBattle/Boss2/BossAnim"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x).ToArray();
  Vector2[][] maps={new[]{new Vector2(0,0),new Vector2(16,20),new Vector2(19,24),new Vector2(21,24),new Vector2(26,32),new Vector2(29,32),new Vector2(42,65),new Vector2(54,89)},new[]{new Vector2(0,0),new Vector2(9,13),new Vector2(11,13),new Vector2(17,25),new Vector2(19,25),new Vector2(25,31),new Vector2(30,41),new Vector2(32,41),new Vector2(43,60),new Vector2(58,85)},new[]{new Vector2(0,0),new Vector2(9,12),new Vector2(11,12),new Vector2(17,25),new Vector2(20,25),new Vector2(26,35),new Vector2(28,35),new Vector2(34,45),new Vector2(37,45),new Vector2(44,59),new Vector2(46,59),new Vector2(52,72),new Vector2(55,72),new Vector2(69,110)}};
  float[][] strikes={new[]{28f},new[]{20f,35f},new[]{20f,40f,65f}};
  string[] names={"B2_PowerSlash_Crisp","B2_DoubleCombo_Crisp","B2_WeaponCombo_Crisp"};
  var reports=new List<object>();
  for(int index=0;index<3;index++)
  {
   var source=UnityEngine.Object.Instantiate(BattleCharacterAssetPaths.Load<GameObject>(paths[index]));var target=UnityEngine.Object.Instantiate(targetAsset);
   try
   {
    foreach(var a in source.GetComponentsInChildren<Animator>())a.enabled=false;foreach(var a in target.GetComponentsInChildren<Animator>())a.enabled=false;
    var sourceRest=Rest(source);var targetRest=Rest(target);
    var src=source.GetComponentsInChildren<Transform>().GroupBy(t=>Canon(t.name)).ToDictionary(g=>g.Key,g=>g.First());
    var dst=target.GetComponentsInChildren<Transform>().Where(t=>src.ContainsKey(Canon(t.name))&&t.name!="output_unwrapped"&&t.name!="target_character"&&t!=target.transform).ToArray();
    // Restore target bone offsets from its actual skin bind matrices, not the imported first animation pose.
    foreach(var t in dst){var rest=targetRest[Canon(t.name)];t.SetPositionAndRotation(rest.GetColumn(3),rest.rotation);}
    var positions=dst.Select(t=>t.localPosition).ToArray();var scales=dst.Select(t=>t.localScale).ToArray();
    var sourceClip=BattleCharacterAssetPaths.LoadAll(paths[index]).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
    sourceClip.SampleAnimation(source,0);var startHips=src["Hips"].position;
    var hips=dst.Single(t=>t.name=="Hips");var bindHips=(Vector3)targetRest["Hips"].GetColumn(3);
    // Keep every attack facing the same direction and remove unintended floor travel.
    Quaternion startDelta=src["Hips"].rotation*Quaternion.Inverse(sourceRest["Hips"].rotation);
    var initialForward=Vector3.ProjectOnPlane(src["headfront"].position-src["Head"].position,Vector3.up).normalized;
    Quaternion heading=initialForward.sqrMagnitude>.001f?Quaternion.FromToRotation(initialForward,Vector3.forward):Quaternion.identity;
    float ratio=Vector3.Distance(targetRest["Head"].GetColumn(3),targetRest["Hips"].GetColumn(3))/Mathf.Max(.001f,Vector3.Distance(sourceRest["Head"].GetColumn(3),sourceRest["Hips"].GetColumn(3)));
    int end=(int)maps[index].Last().x;var curves=new Dictionary<string,AnimationCurve[]>();
    foreach(var t in dst)curves[AnimationUtility.CalculateTransformPath(t,target.transform)]=Enumerable.Range(0,10).Select(_=>new AnimationCurve()).ToArray();
    var previous=new Dictionary<Transform,Quaternion>();float minElbow=180,maxElbow=0;
    for(int frame=0;frame<=end;frame++)
    {
     sourceClip.SampleAnimation(source,Map(maps[index],frame)/30f);
     for(int i=0;i<dst.Length;i++){dst[i].localPosition=positions[i];dst[i].localScale=scales[i];}
     foreach(var t in dst){string key=Canon(t.name);t.rotation=heading*src[key].rotation*Quaternion.Inverse(sourceRest[key].rotation)*targetRest[key].rotation;}
     hips.position=bindHips+Vector3.up*((src["Hips"].position.y-startHips.y)*ratio);
     if(index==0)
     {
      // Solve the elbows from shoulder/hand positions in a stable outward plane.
      // This replaces the source's twisted arm roll without changing the attack timing.
      var chest=dst.Single(t=>t.name=="UpperChest");
      var bodyRotation=chest.rotation*Quaternion.Inverse(targetRest["UpperChest"].rotation);
      var right=bodyRotation*Vector3.right;var forward=bodyRotation*Vector3.forward;
      foreach(var side in new[]{"Left","Right"})
      {
       var upper=dst.Single(t=>t.name==side+"UpperArm");var lower=dst.Single(t=>t.name==side+"LowerArm");var hand=dst.Single(t=>t.name==side+"Hand");
       var handRot=hand.rotation;float l1=Vector3.Distance(upper.position,lower.position),l2=Vector3.Distance(lower.position,hand.position);
       Vector3 desired=hand.position;float sign=Mathf.Sign(((Vector3)targetRest[side+"UpperArm"].GetColumn(3)).x-bindHips.x);
       Vector3 rel=desired-chest.position;float lateral=Vector3.Dot(rel,right)*sign;
       if(lateral<.035f)desired+=right*sign*(.035f-lateral);
       // Keep the wrist outside the chest volume during the backswing.
       float front=Vector3.Dot(desired-chest.position,forward);
       if(front<-.16f)desired+=forward*(-.16f-front);
       var direction=(desired-upper.position).normalized;float distance=Mathf.Clamp(Vector3.Distance(desired,upper.position),Mathf.Abs(l1-l2)+.015f,(l1+l2)*.97f);
       desired=upper.position+direction*distance;
       float along=(l1*l1-l2*l2+distance*distance)/(2*distance);float outward=Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
       Vector3 pole=Vector3.ProjectOnPlane(right*sign+forward*.3f,direction).normalized;
       Vector3 elbow=upper.position+direction*along+pole*outward;
       var ub=targetRest[side+"UpperArm"];var lb=targetRest[side+"LowerArm"];var hb=targetRest[side+"Hand"];
       upper.rotation=Quaternion.FromToRotation((Vector3)lb.GetColumn(3)-(Vector3)ub.GetColumn(3),elbow-upper.position)*ub.rotation;
       lower.rotation=Quaternion.FromToRotation((Vector3)hb.GetColumn(3)-(Vector3)lb.GetColumn(3),desired-lower.position)*lb.rotation;
       hand.rotation=handRot;
       float angle=Vector3.Angle(upper.position-lower.position,hand.position-lower.position);minElbow=Mathf.Min(minElbow,angle);maxElbow=Mathf.Max(maxElbow,angle);
      }
     }
     foreach(var t in dst)
     {
      var cs=curves[AnimationUtility.CalculateTransformPath(t,target.transform)];var p=t.localPosition;var q=t.localRotation;if(previous.TryGetValue(t,out var last)&&Quaternion.Dot(last,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[t]=q;var s=t.localScale;
      float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};for(int k=0;k<10;k++)cs[k].AddKey(new Keyframe(frame/30f,values[k]));
     }
    }
    var clip=new AnimationClip{name=names[index],frameRate=30};string[] props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
    foreach(var pair in curves)for(int k=0;k<10;k++){var curve=pair.Value[k];for(int j=0;j<curve.length;j++){AnimationUtility.SetKeyLeftTangentMode(curve,j,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,j,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(pair.Key,typeof(Transform),props[k]),curve);}
    clip.EnsureQuaternionContinuity();
    AnimationUtility.SetAnimationEvents(clip,strikes[index].Select(f=>new AnimationEvent{time=EventFrame(maps[index],f)/30f,functionName="EmitB2SwordSlash"}).ToArray());
    Save(clip,Folder+"/"+names[index]+".anim");
    reports.Add(new{name=names[index],sourceEnd=Mathf.RoundToInt(sourceClip.length*30),end,events=strikes[index].Select(f=>EventFrame(maps[index],f)).ToArray(),minElbow,maxElbow});
   }
   finally{UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target);}
  }
  AssetDatabase.SaveAssets();return reports;
 }
}

