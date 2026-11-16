using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class BuildB2RetreatVanishPreview
{
 public const string Folder="Assets/3DBattle/Boss2/PhasePreview";
 public const string PreviewName="B2 - Retreat Smoke Vanish Preview";
 static readonly string[] Props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
 static Dictionary<string,Transform> Bones(GameObject g)=>g.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(x=>x.Key,x=>x.First());
 static Dictionary<string,Matrix4x4> Rest(GameObject g)
 {
  var d=new Dictionary<string,Matrix4x4>();
  foreach(var r in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))for(int i=0;i<r.bones.Length;i++)if(r.bones[i])d[r.bones[i].name]=g.transform.worldToLocalMatrix*r.transform.localToWorldMatrix*r.sharedMesh.bindposes[i].inverse;
  foreach(var t in g.GetComponentsInChildren<Transform>(true))if(!d.ContainsKey(t.name))d[t.name]=g.transform.worldToLocalMatrix*t.localToWorldMatrix;
  return d;
 }
 static Vector3 Pos(Matrix4x4 m)=>m.GetColumn(3);
 static void SafeModel(GameObject g)
 {
  g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);g.transform.localScale=Vector3.one;
  foreach(var b in g.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
  foreach(var a in g.GetComponentsInChildren<Animator>(true))a.enabled=false;
 }
 static AnimationClip Convert(GameObject target)
 {
  string path=AssetDatabase.FindAssets("t:Model",new[]{"Assets/3DBattle/Boss2/BossAnim"}).Select(AssetDatabase.GUIDToAssetPath).Single(p=>p.Contains("Walk_Backward_with_Sword"));
  var source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));SafeModel(source);
  try
  {
   var src=Bones(source);var dst=Bones(target);var sr=Rest(source);var tr=Rest(target);
   var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
   var joints=target.GetComponentsInChildren<Transform>(true).Where(t=>t.IsChildOf(dst["Hips"])&&!t.name.Contains("Sword")).ToArray();
   var grip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss2/BossAnim/Crisp/B2_PowerSlash_Crisp.anim");grip.SampleAnimation(target,0);
   var fingers=joints.Where(t=>t.name.StartsWith("Right")&&(t.name.Contains("Thumb")||t.name.Contains("Middle"))).ToDictionary(t=>t,t=>t.localRotation);
   foreach(var t in joints)t.SetPositionAndRotation(Pos(tr[t.name]),tr[t.name].rotation);
   var restP=joints.Select(t=>t.localPosition).ToArray();var restQ=joints.Select(t=>t.localRotation).ToArray();
   clip.SampleAnimation(source,0);
   var forward=Vector3.ProjectOnPlane(src["headfront"].position-src["Head"].position,Vector3.up).normalized;
   var heading=Quaternion.FromToRotation(forward,Vector3.forward);
   float ratio=Vector3.Distance(Pos(tr["Head"]),Pos(tr["Hips"]))/Vector3.Distance(Pos(sr["Head"]),Pos(sr["Hips"]));
   var curves=joints.ToDictionary(t=>t,t=>Enumerable.Range(0,10).Select(_=>new AnimationCurve()).ToArray());
   var previous=new Dictionary<Transform,Quaternion>();
   int count=Mathf.RoundToInt(clip.length*60);
   for(int f=0;f<=count;f++)
   {
    clip.SampleAnimation(source,f/60f);
    for(int i=0;i<joints.Length;i++){joints[i].localPosition=restP[i];joints[i].localRotation=restQ[i];}
    foreach(var t in joints)if(src.ContainsKey(t.name))t.rotation=heading*src[t.name].rotation*Quaternion.Inverse(sr[t.name].rotation)*tr[t.name].rotation;
    dst["Hips"].position=Pos(tr["Hips"])+Vector3.up*((src["Hips"].position.y-Pos(sr["Hips"]).y)*ratio);
    foreach(var pair in fingers)pair.Key.localRotation=pair.Value;
    float low=Mathf.Min(dst["LeftToes"].position.y,dst["RightToes"].position.y);dst["Hips"].position+=Vector3.up*Mathf.Max(0,-low);
    foreach(var t in joints)
    {
     var p=t.localPosition;var q=t.localRotation;var s=t.localScale;
     if(previous.TryGetValue(t,out var last)&&Quaternion.Dot(last,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[t]=q;
     float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};for(int k=0;k<10;k++)curves[t][k].AddKey(new Keyframe(f/60f,values[k]));
    }
   }
   var result=new AnimationClip{name="B2_WalkBackward_Preview",frameRate=30};
   foreach(var pair in curves)for(int k=0;k<10;k++)
   {
    var curve=pair.Value[k];
    for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
    if(curve.keys.All(v=>Mathf.Abs(v.value-curve.keys[0].value)<.000001f))curve=AnimationCurve.Constant(0,clip.length,curve.keys[0].value);
    AnimationUtility.SetEditorCurve(result,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(pair.Key,target.transform),typeof(Transform),Props[k]),curve);
   }
   result.EnsureQuaternionContinuity();
   string outPath=Folder+"/B2_WalkBackward_Preview.anim";var old=AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
   if(old){EditorUtility.CopySerialized(result,old);UnityEngine.Object.DestroyImmediate(result);EditorUtility.SetDirty(old);return old;}
   AssetDatabase.CreateAsset(result,outPath);return result;
  }
  finally{UnityEngine.Object.DestroyImmediate(source);}
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  if(AnimationMode.InAnimationMode())throw new Exception("Stop Animation window preview first");
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<BattleBoss>();
  if(boss.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Wrong scene");
  var originalController=boss.animator.runtimeAnimatorController;
  var target=UnityEngine.Object.Instantiate(boss.animator.gameObject);SafeModel(target);
  AnimationClip clip;
  try{clip=Convert(target);}finally{UnityEngine.Object.DestroyImmediate(target);}
  var old=GameObject.Find(PreviewName);if(old)Undo.DestroyObjectImmediate(old);
  var root=new GameObject(PreviewName);root.tag="EditorOnly";
  var moving=new GameObject("Retreat Motion");moving.transform.SetParent(root.transform,false);
  var model=UnityEngine.Object.Instantiate(boss.animator.gameObject,moving.transform);model.name="B2 Preview Model";
  foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(component);
  foreach(var component in model.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(component);
  foreach(var component in model.GetComponentsInChildren<ParticleSystem>(true))UnityEngine.Object.DestroyImmediate(component.gameObject);
  model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;model.transform.localScale=boss.animator.transform.lossyScale;
  foreach(var a in model.GetComponentsInChildren<Animator>(true)){a.runtimeAnimatorController=null;a.enabled=false;a.applyRootMotion=false;a.fireEvents=false;}
  var driver=root.AddComponent<B2RetreatVanishPreview>();driver.model=model;driver.movingRoot=moving.transform;driver.backwardClip=clip;
  driver.bodyRenderers=model.GetComponentsInChildren<Renderer>(true);
  var smokeRoot=new GameObject("Vanish Smoke - 8 Layers");smokeRoot.transform.SetParent(moving.transform,false);
  var slash=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Boss2/GoldenSlashVFX/B2GoldenSlash.prefab");
  var sources=slash.GetComponent<B2SlashTrailLayout>().center.GetComponentsInChildren<ParticleSystem>(true);
  var materials=new Material[2];
  for(int m=0;m<2;m++)
  {
   string mp=Folder+"/VanishSmoke_"+(m==0?"Ribbon":"Wisp")+".mat";
   var copy=new Material(sources[m].GetComponent<ParticleSystemRenderer>().sharedMaterial);
   copy.name="Vanish Smoke "+(m==0?"Ribbon":"Wisp");
   copy.SetFloat("_SoftParticles",100);copy.SetFloat("_AlphaClip",0);copy.SetFloat("_Clip",0);copy.DisableKeyword("_ALPHATEST_ON");
   materials[m]=AssetDatabase.LoadAssetAtPath<Material>(mp);
   if(materials[m]){EditorUtility.CopySerialized(copy,materials[m]);UnityEngine.Object.DestroyImmediate(copy);EditorUtility.SetDirty(materials[m]);}
   else{materials[m]=copy;AssetDatabase.CreateAsset(copy,mp);}
  }
  var systems=new List<ParticleSystem>();
  float[] sizes={.86f,1.13f,.96f,1.24f,1.04f,.78f,1.18f,.91f};
  for(int i=0;i<8;i++)
  {
   var ps=UnityEngine.Object.Instantiate(sources[i%2],smokeRoot.transform);
   ps.gameObject.name="Smoke "+(i+1).ToString("00")+" - "+(i%2==0?"Ribbon":"Wisp");
   ps.gameObject.SetActive(true);ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
   float yaw=i*45+12;float roll=i*47-35;
   var orientation=Quaternion.Euler(0,yaw,roll);
   // Cross eight broad ribbons around the body with varied headings and scales.
   ps.transform.localRotation=orientation*(i%2==0?Quaternion.identity:Quaternion.Euler(0,0,90));
   ps.transform.localPosition=new Vector3(0,3.5f+(i%3-1)*.65f,0)+Quaternion.Euler(0,yaw,0)*Vector3.forward*1.8f;
   ps.transform.localScale=Vector3.one*sizes[i];
   var main=ps.main;main.loop=false;main.playOnAwake=false;main.duration=driver.smokeEnd-driver.smokeStart;main.startLifetime=main.duration-i*.018f;
   main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Hierarchy;main.startSpeed=0;main.startDelay=0;main.maxParticles=1;main.stopAction=ParticleSystemStopAction.None;
   main.startSize3D=true;
   main.startSizeX=i%2==0?13:1;main.startSizeY=i%2==0?10:1;main.startSizeZ=i%2==0?1:1.3f;
   ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(731+i*71);
   var emission=ps.emission;emission.enabled=true;emission.rateOverTime=0;emission.rateOverDistance=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,1)});
   var shape=ps.shape;shape.enabled=false;
   var size=ps.sizeOverLifetime;size.enabled=true;size.separateAxes=false;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,.15f,.30f,1));
   var color=ps.colorOverLifetime;color.enabled=true;var gradient=new Gradient();
   gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.23f),new GradientAlphaKey(1,.64f),new GradientAlphaKey(0,1)});color.color=gradient;
   var r=ps.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=materials[i%2];r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;
   systems.Add(ps);
  }
  driver.smokeLayers=systems.ToArray();driver.SamplePreview(0);
  foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.tag="EditorOnly";
  PrefabUtility.SaveAsPrefabAsset(root,Folder+"/B2_RetreatVanish_Preview.prefab");
  root.transform.SetPositionAndRotation(new Vector3(-18,0,0),Quaternion.Euler(0,90,0));
  Undo.RegisterCreatedObjectUndo(root,"Create B2 retreat vanish preview");
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
  Selection.activeGameObject=root;
  if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(root.transform.position+new Vector3(-1.5f,3.4f,0),Quaternion.Euler(12,225,0),8);
  return new{preview=root.name,position=root.transform.position.ToString(),clip=AssetDatabase.GetAssetPath(clip),clip.length,smokeCount=systems.Count,scales=sizes,duration=driver.Duration,combatControllerUnchanged=boss.animator.runtimeAnimatorController==originalController};
 }
}
