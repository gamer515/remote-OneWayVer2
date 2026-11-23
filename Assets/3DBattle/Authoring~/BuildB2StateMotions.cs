using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;

public static class BuildB2StateMotions
{
 public const string Folder="Assets/3DBattle/Boss2/BossAnim/StateMachine";
 static readonly string[] Props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
 static string Canon(string n)
 {
  n=n.Replace("mixamorig:","");if(n=="Spine1")return "Chest";if(n=="Spine2")return "UpperChest";
  if(n=="HeadTop_End"||n=="head_end")return "HeadEnd";
  foreach(string s in new[]{"Left","Right"})
  {
   if(n==s+"UpLeg")return s+"UpperLeg";if(n==s+"Leg")return s+"LowerLeg";if(n==s+"Arm")return s+"UpperArm";if(n==s+"ForeArm")return s+"LowerArm";
   if(n==s+"ToeBase")return s+"Toes";if(n==s+"Toe_End")return s+"ToesEnd";
   foreach(string finger in new[]{"Thumb","Middle"})for(int i=1;i<=4;i++)if(n==s+"Hand"+finger+i)return s+finger+new[]{"Proximal","Intermediate","Distal","DistalEnd"}[i-1];
  }
  return n;
 }
 static Dictionary<string,Transform> Bones(GameObject g)=>g.GetComponentsInChildren<Transform>(true).GroupBy(t=>Canon(t.name)).ToDictionary(x=>x.Key,x=>x.First());
 static Dictionary<string,Matrix4x4> Rest(GameObject g)
 {
  var d=new Dictionary<string,Matrix4x4>();
  foreach(var r in g.GetComponentsInChildren<SkinnedMeshRenderer>())for(int i=0;i<r.bones.Length;i++)if(r.bones[i])d[Canon(r.bones[i].name)]=g.transform.worldToLocalMatrix*r.transform.localToWorldMatrix*r.sharedMesh.bindposes[i].inverse;
  foreach(var t in g.GetComponentsInChildren<Transform>())if(!d.ContainsKey(Canon(t.name)))d[Canon(t.name)]=g.transform.worldToLocalMatrix*t.localToWorldMatrix;
  return d;
 }
 static Vector3 Pos(Matrix4x4 m)=>m.GetColumn(3);
 static void Save(AnimationClip c,string path)
 {
  var old=BattleCharacterAssetPaths.Load<AnimationClip>(path);
  if(old){EditorUtility.CopySerialized(c,old);EditorUtility.SetDirty(old);UnityEngine.Object.DestroyImmediate(c);}else AssetDatabase.CreateAsset(c,path);
 }
 static void SafeModel(GameObject g)
 {
  g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);g.transform.localScale=Vector3.one;
  foreach(var b in g.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
  foreach(var a in g.GetComponentsInChildren<Animator>(true))a.enabled=false;
 }
 static AnimationClip Bake(string name,GameObject target,Transform[] joints,int frames,Action<float> sample)
 {
  var data=joints.ToDictionary(t=>t,t=>Enumerable.Range(0,10).Select(_=>new AnimationCurve()).ToArray());
  var prev=new Dictionary<Transform,Quaternion>();
  for(int f=0;f<=frames*2;f++)
  {
   sample(f/2f);
   foreach(var t in joints)
   {
    var p=t.localPosition;var q=t.localRotation;var s=t.localScale;
    if(prev.TryGetValue(t,out var last)&&Quaternion.Dot(last,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);prev[t]=q;
    float[] v={p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};for(int k=0;k<10;k++)data[t][k].AddKey(new Keyframe(f/60f,v[k]));
   }
  }
  var clip=new AnimationClip{name=name,frameRate=30};
  foreach(var pair in data)for(int k=0;k<10;k++)
  {
   var c=pair.Value[k];for(int i=0;i<c.length;i++){AnimationUtility.SetKeyLeftTangentMode(c,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(c,i,AnimationUtility.TangentMode.Linear);}
   if(c.keys.All(v=>Mathf.Abs(v.value-c.keys[0].value)<.000001f))c=AnimationCurve.Constant(0,frames/30f,c.keys[0].value);
   AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(pair.Key,target.transform),typeof(Transform),Props[k]),c);
  }
  clip.EnsureQuaternionContinuity();return clip;
 }
 static void Loop(AnimationClip clip)
 {
  var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
 }
 class Pose
 {
  public Vector3[] p,s;public Quaternion[] q;
  public Pose(Transform[] joints){p=joints.Select(t=>t.localPosition).ToArray();s=joints.Select(t=>t.localScale).ToArray();q=joints.Select(t=>t.localRotation).ToArray();}
  public static void Blend(Transform[] joints,Pose a,Pose b,float w)
  {for(int i=0;i<joints.Length;i++){joints[i].localPosition=Vector3.Lerp(a.p[i],b.p[i],w);joints[i].localRotation=Quaternion.Slerp(a.q[i],b.q[i],w);joints[i].localScale=Vector3.Lerp(a.s[i],b.s[i],w);}}
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  if(AnimationMode.InAnimationMode())AnimationMode.StopAnimationMode();
  foreach(var w in Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w=>w.GetType().Name=="AnimationWindow"))w.Close();
  if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/3DBattle/Boss2/BossAnim","StateMachine");
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<BattleBoss>();
  var target=UnityEngine.Object.Instantiate(boss.animator.gameObject);SafeModel(target);
  var paths=AssetDatabase.FindAssets("t:Model",new[]{"Assets/3DBattle/Boss2/BossAnim"}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
  string[] keys={"Idle_02","run_fast_3_inplace","01a1a012","01a1bf9f","Axe_Stance"};
  string[] names={"B2_Idle_02","B2_Run","B2_HitReaction","B2_Death","B2_Axe_Stance"};
  var reports=new List<object>();
  try
  {
   var dst=Bones(target);var tr=Rest(target);
   var joints=target.GetComponentsInChildren<Transform>(true).Where(t=>tr.ContainsKey(Canon(t.name))&&t.IsChildOf(dst["Hips"])&&t!=dst["B2 Holy Sword"]&&!t.name.Contains("Sword")).ToArray();
   // Bind-pose matrices are authoritative; imported first animation frames are not bind poses.
   var grip=BattleCharacterAssetPaths.Load<AnimationClip>("Assets/3DBattle/Boss2/Runtime/Animations/B2_PowerSlash_Crisp.anim");grip.SampleAnimation(target,0);
   var fingers=joints.Where(t=>t.name.StartsWith("Right")&&(t.name.Contains("Thumb")||t.name.Contains("Middle"))).ToDictionary(t=>t,t=>t.localRotation);
   foreach(var t in joints)t.SetPositionAndRotation(Pos(tr[Canon(t.name)]),tr[Canon(t.name)].rotation);
   var restPose=new Pose(joints);
   for(int index=0;index<keys.Length;index++)
   {
    var path=paths.Single(p=>p.Contains(keys[index]));var srcGo=UnityEngine.Object.Instantiate(BattleCharacterAssetPaths.Load<GameObject>(path));SafeModel(srcGo);
    try
    {
     var src=Bones(srcGo);var sr=Rest(srcGo);var sourceClip=BattleCharacterAssetPaths.LoadAll(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
     sourceClip.SampleAnimation(srcGo,index==4?sourceClip.length:0);
     var forward=Vector3.ProjectOnPlane(src["headfront"].position-src["Head"].position,Vector3.up).normalized;
     var heading=forward.sqrMagnitude>.001f?Quaternion.FromToRotation(forward,Vector3.forward):Quaternion.identity;
     float ratio=Vector3.Distance(Pos(tr["Head"]),Pos(tr["Hips"]))/Vector3.Distance(Pos(sr["Head"]),Pos(sr["Hips"]));
     int frames=Mathf.RoundToInt(sourceClip.length*30);float minHips=999,maxHips=-999;
     var result=Bake(names[index],target,joints,frames,f=>
     {
      sourceClip.SampleAnimation(srcGo,f/30f);Pose.Blend(joints,restPose,restPose,0);
      foreach(var t in joints)
      {
       string n=Canon(t.name),sourceName=n=="UpperChest"&&!src.ContainsKey(n)?"Chest":n;
       if(src.ContainsKey(sourceName)&&sr.ContainsKey(sourceName))t.rotation=heading*src[sourceName].rotation*Quaternion.Inverse(sr[sourceName].rotation)*tr[n].rotation;
      }
      var hip=dst["Hips"];hip.position=Pos(tr["Hips"])+Vector3.up*((src["Hips"].position.y-Pos(sr["Hips"]).y)*ratio);
      foreach(var pair in fingers)pair.Key.localRotation=pair.Value;
      float low=Mathf.Min(dst["LeftToes"].position.y,dst["RightToes"].position.y);
      hip.position+=Vector3.up*Mathf.Max(0,-low);
      minHips=Mathf.Min(minHips,hip.position.y);maxHips=Mathf.Max(maxHips,hip.position.y);
     });
     if(index<=1)Loop(result);
     Save(result,Folder+"/"+names[index]+".anim");reports.Add(new{name=names[index],frames,minHips,maxHips,source=path,missingUpperChest=!src.ContainsKey("UpperChest")});
    }
    finally{UnityEngine.Object.DestroyImmediate(srcGo);}
   }
   var idle=BattleCharacterAssetPaths.Load<AnimationClip>(Folder+"/B2_Idle_02.anim");var axe=BattleCharacterAssetPaths.Load<AnimationClip>(Folder+"/B2_Axe_Stance.anim");
   idle.SampleAnimation(target,0);var standing=new Pose(joints);
   axe.SampleAnimation(target,0);var fallen=new Pose(joints);
   axe.SampleAnimation(target,axe.length);var recovered=new Pose(joints);
   int axeFrames=Mathf.RoundToInt(axe.length*30);const int fallFrames=24,holdFrames=8,returnFrames=15;
   var transition=Bake("B2_PhaseRecovery",target,joints,fallFrames+holdFrames+axeFrames+returnFrames,f=>
   {
    if(f<=fallFrames)Pose.Blend(joints,standing,fallen,Mathf.SmoothStep(0,1,f/fallFrames));
    else if(f<fallFrames+holdFrames)Pose.Blend(joints,fallen,fallen,0);
    else if(f<=fallFrames+holdFrames+axeFrames)axe.SampleAnimation(target,(f-fallFrames-holdFrames)/30f);
    else Pose.Blend(joints,recovered,standing,Mathf.SmoothStep(0,1,(f-fallFrames-holdFrames-axeFrames)/returnFrames));
    float low=Mathf.Min(dst["LeftToes"].position.y,dst["RightToes"].position.y);dst["Hips"].position+=Vector3.up*Mathf.Max(0,-low);
   });
   Save(transition,Folder+"/B2_PhaseRecovery.anim");
   // The newly authored support poses match the same bind rig as the existing attacks.
   AssetDatabase.SaveAssets();return new{clips=reports,phaseFrames=fallFrames+holdFrames+axeFrames+returnFrames,fallFrames,holdFrames,axeFrames,returnFrames,joints=joints.Length};
  }
  finally{UnityEngine.Object.DestroyImmediate(target);}
 }
}
