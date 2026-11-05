using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class AdjustParryShield
{
 const string Path="Assets/3DBattle/NewPlayer/Converted/NewKnight_ShieldParry_Preview.anim";
 static Transform Find(GameObject g,string n)=>g.GetComponentsInChildren<Transform>().First(t=>t.name==n);
 static void Pose(GameObject root,Transform arm,Transform hand,Transform shield,float drop,float angle)
 {
  Vector3 dir=(hand.position-arm.position).normalized;
  arm.rotation=Quaternion.FromToRotation(dir,Vector3.RotateTowards(dir,Vector3.down,drop*Mathf.Deg2Rad,0))*arm.rotation;
  Vector3 normal=shield.right;Vector3 horizontal=-root.transform.forward;
  if(horizontal.sqrMagnitude<.1f)horizontal=root.transform.forward;
  Vector3 target=horizontal*Mathf.Sin(angle*Mathf.Deg2Rad)+Vector3.up*Mathf.Cos(angle*Mathf.Deg2Rad);
  hand.rotation=Quaternion.FromToRotation(normal,target)*hand.rotation;
 }
 static List<Vector3> Points(GameObject root,SkinnedMeshRenderer skin)
 {
  var mesh=skin.sharedMesh;var verts=mesh.vertices;var weights=mesh.boneWeights;var binds=mesh.bindposes;
  var matrices=skin.bones.Select((b,i)=>b.localToWorldMatrix*binds[i]).ToArray();var world=new Vector3[verts.Length];
  for(int i=0;i<verts.Length;i++){var w=weights[i];world[i]=matrices[w.boneIndex0].MultiplyPoint3x4(verts[i])*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(verts[i])*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(verts[i])*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(verts[i])*w.weight3;}
  var result=new List<Vector3>();var tri=mesh.triangles;
  for(int i=0;i<tri.Length;i+=3){int j=tri[i];string bone=skin.bones[weights[j].boneIndex0].name;if(bone=="LeftHand")continue;result.Add(world[j]);result.Add((world[j]+world[tri[i+1]]+world[tri[i+2]])/3);}
  foreach(var f in root.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Helmet3"||f.name=="ShoulderPads")){var v=f.sharedMesh.vertices;var t=f.sharedMesh.triangles;for(int i=0;i<t.Length;i+=3){result.Add(f.transform.TransformPoint(v[t[i]]));result.Add(f.transform.TransformPoint((v[t[i]]+v[t[i+1]]+v[t[i+2]])/3));}}
  return result;
 }
 public static object Verify()
 {
  var live=GameObject.Find("New Knight Parry Preview");var root=UnityEngine.Object.Instantiate(live);var a=root.GetComponentInChildren<Animator>();a.enabled=false;
  var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Path);var source=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/NewPlayer/Converted/NewKnight_ShieldParry_BeforeArmAdjustment.anim");var hand=Find(root,"LeftHand");var shield=Find(root,"Shield4");float min=90,max=0,lower=100;int changed=0;
  try{for(int i=0;i<=120;i++){float time=clip.length*i/120;source.SampleAnimation(a.gameObject,time);float y=hand.position.y;clip.SampleAnimation(a.gameObject,time);lower=Mathf.Min(lower,y-hand.position.y);float angle=Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(shield.right,Vector3.up))))*Mathf.Rad2Deg;min=Mathf.Min(min,angle);max=Mathf.Max(max,angle);}
   foreach(var b in AnimationUtility.GetCurveBindings(source)){if((b.path.EndsWith("LeftHand")||b.path.EndsWith("LeftLowerArm"))&&b.propertyName.StartsWith("m_LocalRotation"))continue;var before=AnimationUtility.GetEditorCurve(source,b);var after=AnimationUtility.GetEditorCurve(clip,b);for(int i=0;i<40;i++)if(Mathf.Abs(before.Evaluate(clip.length*i/39)-after.Evaluate(clip.length*i/39))>.00001f){changed++;break;}}
   return new{minimumAngle=min,maximumAngle=max,minimumHandLowering=lower,unrelatedCurvesChanged=changed,scale=shield.localScale.ToString(),frames=121};
  }finally{UnityEngine.Object.DestroyImmediate(root);}
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play first");
  var live=GameObject.Find("New Knight Parry Preview");var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Path);
  const string backup="Assets/3DBattle/NewPlayer/Converted/NewKnight_ShieldParry_BeforeArmAdjustment.anim";
  if(AssetDatabase.LoadAssetAtPath<AnimationClip>(backup)==null)AssetDatabase.CopyAsset(Path,backup);
  var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(backup);var root=UnityEngine.Object.Instantiate(live);root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  var a=root.GetComponentInChildren<Animator>();a.enabled=false;var arm=Find(root,"LeftLowerArm");var hand=Find(root,"LeftHand");var shield=Find(root,"Shield4");var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();var bounds=shield.GetComponent<MeshFilter>().sharedMesh.bounds;
  float best=float.MaxValue,bestAngle=0,bestDrop=0;var results=new List<object>();
  try{
   for(int angle=70;angle<=85;angle++)for(int drop=20;drop<=70;drop+=5){float score=0;int hits=0;
    for(int f=0;f<=16;f++){source.SampleAnimation(a.gameObject,source.length*f/16);Pose(root,arm,hand,shield,drop,angle);foreach(var p in Points(root,skin)){var q=shield.InverseTransformPoint(p)-bounds.center;var e=bounds.extents;if(Mathf.Abs(q.x)<e.x&&Mathf.Abs(q.y)<e.y&&Mathf.Abs(q.z)<e.z){hits++;score+=1+(1-Mathf.Abs(q.x)/e.x);}}}
    score+=drop*.005f;results.Add(new{angle,drop,hits,score});if(score<best){best=score;bestAngle=angle;bestDrop=drop;}
   }
   var curves=new Dictionary<string,List<Keyframe>[]>();foreach(var t in new[]{arm,hand})curves[AnimationUtility.CalculateTransformPath(t,a.transform)]=Enumerable.Range(0,4).Select(_=>new List<Keyframe>()).ToArray();
   int count=Mathf.CeilToInt(source.length*60);for(int f=0;f<=count;f++){float time=source.length*f/count;source.SampleAnimation(a.gameObject,time);Pose(root,arm,hand,shield,bestDrop,bestAngle);foreach(var t in new[]{arm,hand}){var k=curves[AnimationUtility.CalculateTransformPath(t,a.transform)];var q=t.localRotation;for(int c=0;c<4;c++)k[c].Add(new Keyframe(time,q[c]));}}
   Undo.RecordObject(clip,"Lower parry shield arm");foreach(var pair in curves)for(int c=0;c<4;c++){var curve=new AnimationCurve(pair.Value[c].ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(pair.Key,typeof(Transform),"m_LocalRotation."+"xyzw"[c]),curve);}
   clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);AssetDatabase.SaveAssets();
   var liveA=live.GetComponentInChildren<Animator>();clip.SampleAnimation(liveA.gameObject,.25f);EditorSceneManager.MarkSceneDirty(live.scene);EditorSceneManager.SaveScene(live.scene);
   System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/ParryShieldAngleSearch.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{bestAngle,bestDrop,best,results},Newtonsoft.Json.Formatting.Indented));
   return new{bestAngle,bestDrop,score=best,scale=Find(live,"Shield4").localScale.ToString(),candidates=results.Count};
  }finally{UnityEngine.Object.DestroyImmediate(root);}
 }
}

