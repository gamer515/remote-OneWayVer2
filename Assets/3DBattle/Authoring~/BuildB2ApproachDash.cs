using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

// Creates new approach clips and a non-combat preview. Does not alter the source FBX or combat controller.
public static class BuildB2ApproachDash
{
 public const string Folder="Assets/3DBattle/Boss2/BossAnim/Approach";
 public const string PreviewName="B2 - Approach Dash Preview";
 static readonly string[] Properties={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w","m_LocalScale.x","m_LocalScale.y","m_LocalScale.z"};
 static string Canon(string n)
 {
  n=n.Replace("mixamorig:","");
  if(n=="Spine1")return "Chest";if(n=="Spine2")return "UpperChest";
  if(n=="HeadTop_End"||n=="head_end")return "HeadEnd";
  foreach(string side in new[]{"Left","Right"})
  {
   if(n==side+"UpLeg")return side+"UpperLeg";if(n==side+"Leg")return side+"LowerLeg";
   if(n==side+"Arm")return side+"UpperArm";if(n==side+"ForeArm")return side+"LowerArm";
   if(n==side+"ToeBase")return side+"Toes";if(n==side+"Toe_End")return side+"ToesEnd";
   foreach(string finger in new[]{"Thumb","Middle"})for(int i=1;i<=4;i++)
    if(n==side+"Hand"+finger+i)return side+finger+new[]{"Proximal","Intermediate","Distal","DistalEnd"}[i-1];
  }
  return n;
 }
 static Dictionary<string,Transform> Bones(GameObject g)=>g.GetComponentsInChildren<Transform>(true).GroupBy(t=>Canon(t.name)).ToDictionary(x=>x.Key,x=>x.First());
 static Dictionary<string,Matrix4x4> Rest(GameObject g)
 {
  var result=new Dictionary<string,Matrix4x4>();
  foreach(var r in g.GetComponentsInChildren<SkinnedMeshRenderer>())
   for(int i=0;i<r.bones.Length;i++)if(r.bones[i])result[Canon(r.bones[i].name)]=g.transform.worldToLocalMatrix*r.transform.localToWorldMatrix*r.sharedMesh.bindposes[i].inverse;
  foreach(var t in g.GetComponentsInChildren<Transform>(true))if(!result.ContainsKey(Canon(t.name)))result[Canon(t.name)]=g.transform.worldToLocalMatrix*t.localToWorldMatrix;
  return result;
 }
 static Vector3 Pos(Matrix4x4 m)=>m.GetColumn(3);
 static float Smooth(float a,float b,float f)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,f));
 static void Save(UnityEngine.Object obj,string path)
 {
  var old=BattleCharacterAssetPaths.Load<UnityEngine.Object>(path);
  if(old){EditorUtility.CopySerialized(obj,old);UnityEngine.Object.DestroyImmediate(obj);EditorUtility.SetDirty(old);}
  else AssetDatabase.CreateAsset(obj,path);
 }
 static void Linear(AnimationCurve curve)
 {
  for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
 }
 static void Arm(Dictionary<string,Transform> bones,Dictionary<string,Matrix4x4> rest,string side,Vector3 wrist,Vector3 pole,Quaternion? handRotation)
 {
  var upper=bones[side+"UpperArm"];var lower=bones[side+"LowerArm"];var hand=bones[side+"Hand"];
  var ub=rest[side+"UpperArm"];var lb=rest[side+"LowerArm"];var hb=rest[side+"Hand"];
  float l1=Vector3.Distance(Pos(ub),Pos(lb)),l2=Vector3.Distance(Pos(lb),Pos(hb));
  Vector3 direction=(wrist-upper.position).normalized;
  float distance=Mathf.Clamp(Vector3.Distance(upper.position,wrist),Mathf.Abs(l1-l2)+.01f,(l1+l2)*.96f);
  wrist=upper.position+direction*distance;
  float along=(l1*l1-l2*l2+distance*distance)/(2*distance);
  Vector3 elbow=upper.position+direction*along+Vector3.ProjectOnPlane(pole,direction).normalized*Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
  upper.rotation=Quaternion.FromToRotation(Pos(lb)-Pos(ub),elbow-upper.position)*ub.rotation;
  lower.rotation=Quaternion.FromToRotation(Pos(hb)-Pos(lb),wrist-lower.position)*lb.rotation;
  hand.rotation=handRotation??(lower.rotation*Quaternion.Inverse(lb.rotation)*hb.rotation);
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  if(AnimationMode.InAnimationMode())throw new Exception("Close Animation preview before building clips");
  if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/3DBattle/Boss2/BossAnim","Approach");
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss");
  var sceneAnimator=boss.GetComponentsInChildren<Animator>(true).Single(a=>a.name.Contains("Heavy_Hammer"));
  string sourcePath=AssetDatabase.FindAssets("t:Model",new[]{"Assets/3DBattle/Boss2/BossAnim"}).Select(AssetDatabase.GUIDToAssetPath).Single(p=>p.Contains("01a1bf98"));
  var sourceClip=BattleCharacterAssetPaths.LoadAll(sourcePath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
  var source=UnityEngine.Object.Instantiate(BattleCharacterAssetPaths.Load<GameObject>(sourcePath));
  var target=UnityEngine.Object.Instantiate(sceneAnimator.gameObject);
  var curves=new Dictionary<string,AnimationCurve[]>();
  var travelX=new AnimationCurve();var travelZ=new AnimationCurve();
  float lowerBodyError=0,minElbow=180,maxElbow=0,maxLean=0;
  try
  {
   foreach(var g in new[]{source,target})
   {
    g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);g.transform.localScale=Vector3.one;
    foreach(var b in g.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
    foreach(var a in g.GetComponentsInChildren<Animator>(true))a.enabled=false;
   }
   var src=Bones(source);var dst=Bones(target);var sr=Rest(source);var tr=Rest(target);
   var joints=target.GetComponentsInChildren<Transform>(true).Where(t=>src.ContainsKey(Canon(t.name))&&tr.ContainsKey(Canon(t.name))&&t!=target.transform&&t.name!="target_character"&&t.name!="output_unwrapped").ToArray();
   var sword=dst["B2 Holy Sword"];var hand=dst["RightHand"];
   Quaternion swordMount=Quaternion.Inverse(hand.rotation)*sword.rotation;
   var gripPose=BattleCharacterAssetPaths.Load<AnimationClip>("Assets/3DBattle/Boss2/Runtime/Animations/B2_PowerSlash_Crisp.anim");
   gripPose.SampleAnimation(target,0);
   var fingerRotations=joints.Where(t=>Canon(t.name).Contains("Thumb")||Canon(t.name).Contains("Middle")).ToDictionary(t=>t,t=>t.localRotation);
   foreach(var t in joints)t.SetPositionAndRotation(Pos(tr[Canon(t.name)]),tr[Canon(t.name)].rotation);
   var positions=joints.Select(t=>t.localPosition).ToArray();var scales=joints.Select(t=>t.localScale).ToArray();
   sourceClip.SampleAnimation(source,0);var startHip=src["Hips"].position;
   var forward=Vector3.ProjectOnPlane(src["headfront"].position-src["Head"].position,Vector3.up).normalized;
   var heading=Quaternion.FromToRotation(forward,Vector3.forward);
   float ratio=Vector3.Distance(Pos(tr["Head"]),Pos(tr["Hips"]))/Vector3.Distance(Pos(sr["Head"]),Pos(sr["Hips"]));
   foreach(var t in joints)curves[AnimationUtility.CalculateTransformPath(t,target.transform)]=Enumerable.Range(0,10).Select(_=>new AnimationCurve()).ToArray();
   var previous=new Dictionary<Transform,Quaternion>();int count=Mathf.RoundToInt(sourceClip.length*60);
   for(int sample=0;sample<=count;sample++)
   {
    float time=Mathf.Min(sourceClip.length,sample/60f),frame=time*30;
    sourceClip.SampleAnimation(source,time);
    for(int i=0;i<joints.Length;i++){joints[i].localPosition=positions[i];joints[i].localScale=scales[i];}
    foreach(var t in joints){string key=Canon(t.name);t.rotation=heading*src[key].rotation*Quaternion.Inverse(sr[key].rotation)*tr[key].rotation;}
    Vector3 travel=heading*(src["Hips"].position-startHip)*ratio;
    dst["Hips"].position=Pos(tr["Hips"])+travel;
    var lower=joints.Where(t=>Canon(t.name)=="Hips"||Canon(t.name).Contains("Leg")||Canon(t.name).Contains("Foot")||Canon(t.name).Contains("Toe")).ToDictionary(t=>t,t=>t.rotation);
    float charge=Smooth(0,13,frame)*(1-Smooth(59,86,frame));
    float lean=Mathf.Lerp(3,22,charge);
    // Distribute the lean across the spine while removing the source's side bend and shoulder shrug.
    dst["Spine"].rotation=Quaternion.Euler(lean*.38f,0,0)*tr["Spine"].rotation;
    dst["Chest"].rotation=Quaternion.Euler(lean*.72f,0,0)*tr["Chest"].rotation;
    dst["UpperChest"].rotation=Quaternion.Euler(lean,0,0)*tr["UpperChest"].rotation;
    dst["Neck"].rotation=Quaternion.Euler(lean*.30f,0,0)*tr["Neck"].rotation;
    dst["Head"].rotation=Quaternion.Euler(2,0,0)*tr["Head"].rotation;
    var torso=Quaternion.Euler(lean,0,0);
    foreach(string side in new[]{"Left","Right"})dst[side+"Shoulder"].rotation=torso*tr[side+"Shoulder"].rotation;
    var hip=dst["Hips"].position;
    Vector3 rightWrist=hip+new Vector3(.43f,Mathf.Lerp(.12f,.17f,charge),Mathf.Lerp(.03f,-.10f,charge));
    Vector3 bladeDirection=Vector3.Slerp(new Vector3(.2f,.65f,.65f).normalized,new Vector3(.28f,.20f,-1).normalized,charge);
    var bladeRotation=Quaternion.LookRotation(bladeDirection,Vector3.up);
    Arm(dst,tr,"Right",rightWrist,new Vector3(1,-.35f,-.40f),bladeRotation*Quaternion.Inverse(swordMount));
    Vector3 leftWrist=hip+new Vector3(-.30f,Mathf.Lerp(.18f,.32f,charge),Mathf.Lerp(.12f,.24f,charge));
    Arm(dst,tr,"Left",leftWrist,new Vector3(-1,-.65f,.15f),null);
    foreach(var pair in fingerRotations)pair.Key.localRotation=pair.Value;
    foreach(var pair in lower)lowerBodyError=Mathf.Max(lowerBodyError,Quaternion.Angle(pair.Value,pair.Key.rotation));
    foreach(string side in new[]{"Left","Right"})
    {
     float angle=Vector3.Angle(dst[side+"UpperArm"].position-dst[side+"LowerArm"].position,dst[side+"Hand"].position-dst[side+"LowerArm"].position);
     minElbow=Mathf.Min(minElbow,angle);maxElbow=Mathf.Max(maxElbow,angle);
    }
    maxLean=Mathf.Max(maxLean,lean);
    travelX.AddKey(time,travel.x);travelZ.AddKey(time,travel.z);
    foreach(var t in joints)
    {
     var cs=curves[AnimationUtility.CalculateTransformPath(t,target.transform)];var p=t.localPosition;var q=t.localRotation;var s=t.localScale;
     if(previous.TryGetValue(t,out var last)&&Quaternion.Dot(last,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);previous[t]=q;
     float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};
     for(int k=0;k<10;k++)cs[k].AddKey(new Keyframe(time,values[k]));
    }
   }
   var clip=new AnimationClip{name="B2_ApproachDash",frameRate=30};
   foreach(var pair in curves)for(int k=0;k<10;k++)
   {
    var c=pair.Value[k];Linear(c);
    if(c.keys.All(key=>Mathf.Abs(key.value-c.keys[0].value)<.000001f))c=AnimationCurve.Constant(0,sourceClip.length,c.keys[0].value);
    AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(pair.Key,typeof(Transform),Properties[k]),c);
   }
   clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
   var inPlace=UnityEngine.Object.Instantiate(clip);inPlace.name="B2_ApproachDash_InPlace";
   // Keep a code-driven variant whose visual skeleton stays over the gameplay collider.
   string hipPath=AnimationUtility.CalculateTransformPath(dst["Hips"],target.transform);
   foreach(string property in new[]{"m_LocalPosition.x","m_LocalPosition.z"})
   {
    var binding=EditorCurveBinding.FloatCurve(hipPath,typeof(Transform),property);var c=AnimationUtility.GetEditorCurve(inPlace,binding);
    AnimationUtility.SetEditorCurve(inPlace,binding,AnimationCurve.Constant(0,clip.length,c.Evaluate(0)));
   }
   Save(clip,Folder+"/B2_ApproachDash.anim");Save(inPlace,Folder+"/B2_ApproachDash_InPlace.anim");
   Linear(travelX);Linear(travelZ);
   var travelClip=new AnimationClip{name="B2_ApproachDash_Travel",frameRate=30};
   AnimationUtility.SetEditorCurve(travelClip,EditorCurveBinding.FloatCurve("",typeof(Transform),"m_LocalPosition.x"),travelX);
   AnimationUtility.SetEditorCurve(travelClip,EditorCurveBinding.FloatCurve("",typeof(Transform),"m_LocalPosition.z"),travelZ);
   Save(travelClip,Folder+"/B2_ApproachDash_Travel.anim");
   // The animation window and an isolated scene preview use the full-travel clip.
   string controllerPath=Folder+"/B2_ApproachDash_Preview.controller";
   var controller=BattleCharacterAssetPaths.Load<AnimatorController>(controllerPath)??AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
   var sm=controller.layers[0].stateMachine;
   var state=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="ApproachDash")??sm.AddState("ApproachDash");
   state.motion=BattleCharacterAssetPaths.Load<AnimationClip>(Folder+"/B2_ApproachDash.anim");sm.defaultState=state;
   var transition=state.transitions.FirstOrDefault()??state.AddTransition(state);
   transition.hasExitTime=true;transition.exitTime=1.35f;transition.duration=0;transition.hasFixedDuration=true;transition.canTransitionToSelf=true;
   var oldPreview=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(a=>a.name==PreviewName);
   if(oldPreview)Undo.DestroyObjectImmediate(oldPreview.gameObject);
   var preview=UnityEngine.Object.Instantiate(sceneAnimator.gameObject);preview.name=PreviewName;
   foreach(var component in preview.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(component);
   foreach(var component in preview.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(component);
   foreach(var component in preview.GetComponentsInChildren<ParticleSystem>(true))UnityEngine.Object.DestroyImmediate(component.gameObject);
   preview.transform.SetPositionAndRotation(new Vector3(-18,0,24),Quaternion.Euler(0,90,0));preview.transform.localScale=sceneAnimator.transform.lossyScale;
   foreach(var t in preview.GetComponentsInChildren<Transform>(true))t.gameObject.tag="EditorOnly";
   var pa=preview.GetComponent<Animator>();pa.runtimeAnimatorController=controller;pa.applyRootMotion=false;pa.fireEvents=false;pa.cullingMode=AnimatorCullingMode.AlwaysAnimate;pa.enabled=true;
   ((AnimationClip)state.motion).SampleAnimation(preview,0);
   Undo.RegisterCreatedObjectUndo(preview,"Create B2 approach dash preview");
   var previewPosition=preview.transform.position;var previewRotation=preview.transform.rotation;
   preview.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   PrefabUtility.SaveAsPrefabAsset(preview,Folder+"/B2_ApproachDash_Preview.prefab");
   preview.transform.SetPositionAndRotation(previewPosition,previewRotation);
   UnityEngine.Object.DestroyImmediate(source);
   UnityEngine.Object.DestroyImmediate(target);
   Selection.activeGameObject=preview;
   EditorSceneManager.MarkSceneDirty(preview.scene);EditorSceneManager.SaveScene(preview.scene);AssetDatabase.SaveAssets();
   return new{clip=Folder+"/B2_ApproachDash.anim",frames=Mathf.RoundToInt(sourceClip.length*30),sourceDuration=sourceClip.length,maxLean,minElbow,maxElbow,lowerBodyError,localForwardTravel=travelZ.Evaluate(sourceClip.length),worldForwardTravel=travelZ.Evaluate(sourceClip.length)*sceneAnimator.transform.lossyScale.z,preview=PreviewName,combatController=sceneAnimator.runtimeAnimatorController.name};
  }
  finally{if(source)UnityEngine.Object.DestroyImmediate(source);if(target)UnityEngine.Object.DestroyImmediate(target);}
 }
}
