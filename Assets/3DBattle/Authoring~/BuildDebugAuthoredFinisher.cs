using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;
using Newtonsoft.Json;

// Reproducible authoring tool. Originals and the production player's controller are never edited.
public static class BuildDebugAuthoredFinisher
{
 const string Folder="Assets/3DBattle/3DBattleDebug/Finisher";
 const string Original="Assets/3DBattle/NewPlayer/PlayableAttacks/NewPlayer_Attack01.anim";
 const string Controller="Assets/3DBattle/NewPlayer/PlayableAttacks/NewPlayerAttackCombo.controller";
 const float ReadyTime=13f/30f, Windup=.15f;
 const float StanceTurn=33.8967f;
 static readonly string[] Names={"Armature","Hips","Chest","UpperChest","Neck","Head","LeftShoulder","LeftUpperArm","LeftLowerArm","LeftHand","RightShoulder","RightUpperArm","RightLowerArm","RightHand","LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes","RightUpperLeg","RightLowerLeg","RightFoot","RightToes","Sword"};
 sealed class Pose {public Vector3[] p; public Quaternion[] q;}
 sealed class Rig:IDisposable
 {
  public GameObject root; public Transform[] bones; public AnimationClip source;
  public Dictionary<string,Transform> map;
  public Quaternion leftFoot,rightFoot,leftToe,rightToe,swordMount,handRelative;
  public Vector3 swordMountPosition,rightGrip,leftGrip;
  public MeshCollider helmetProbe,swordProbe;
  Quaternion stanceRotation=Quaternion.identity;
  public Rig(Animator live)
  {
   root=UnityEngine.Object.Instantiate(live.gameObject);
   root.name="Finisher authoring temporary";root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);root.transform.localScale=live.transform.lossyScale;
   root.GetComponent<Animator>().enabled=false;
   map=root.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
   bones=Names.Where(map.ContainsKey).Select(n=>map[n]).ToArray();
   source=AssetDatabase.LoadAssetAtPath<AnimationClip>(Original);
   source.SampleAnimation(root,104f/30f); leftFoot=map["LeftFoot"].rotation;rightFoot=map["RightFoot"].rotation;
   leftToe=map["LeftToes"].localRotation;rightToe=map["RightToes"].localRotation;
   source.SampleAnimation(root,26f/30f);
   swordMount=map["Sword"].localRotation;swordMountPosition=map["Sword"].localPosition;
   var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();
   rightGrip=Grip(skin,"Right");leftGrip=Grip(skin,"Left");
   int rh=Array.FindIndex(skin.bones,t=>t.name=="RightHand"),lh=Array.FindIndex(skin.bones,t=>t.name=="LeftHand");
   var rb=(skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[rh].inverse).rotation;
   var lb=(skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[lh].inverse).rotation;
   handRelative=Quaternion.Inverse(rb)*lb;
   helmetProbe=map["Helmet3"].gameObject.AddComponent<MeshCollider>();helmetProbe.sharedMesh=map["Helmet3"].GetComponent<MeshFilter>().sharedMesh;helmetProbe.convex=true;helmetProbe.isTrigger=true;
   swordProbe=map["Sword"].gameObject.AddComponent<MeshCollider>();swordProbe.sharedMesh=map["Sword"].GetComponent<MeshFilter>().sharedMesh;swordProbe.convex=true;swordProbe.isTrigger=true;
  }
  public Pose Capture()=>new Pose{p=bones.Select(t=>t.localPosition).ToArray(),q=bones.Select(t=>t.localRotation).ToArray()};
  public Pose Source(float f){source.SampleAnimation(root,f/30f);map["Armature"].localRotation=Quaternion.identity;stanceRotation=Quaternion.identity;return Capture();}
  public void Apply(Pose p){for(int i=0;i<bones.Length;i++){bones[i].localPosition=p.p[i];bones[i].localRotation=p.q[i];}}
  public void Blend(Pose a,Pose b,float t){for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(a.p[i],b.p[i],t);bones[i].localRotation=Quaternion.Slerp(a.q[i],b.q[i],t);}}
  public void Leg(string side,Vector3 foot,Quaternion rotation)
  {IK(map[side+"UpperLeg"],map[side+"LowerLeg"],map[side+"Foot"],stanceRotation*foot,stanceRotation*new Vector3(side=="Left"?-.1f:.1f,0,1));map[side+"Foot"].rotation=stanceRotation*rotation;}
  void Body(Vector3 hips,Vector3 pelvisAngles,Vector3 chestAngles,float headYaw,float stanceTurn=0)
  {
   // Distribute the user's frame-26 turn through the whole stance, not the spine alone.
   stanceRotation=Quaternion.AngleAxis(stanceTurn,Vector3.up);
   map["Armature"].localRotation=stanceRotation;
   map["Hips"].position=stanceRotation*hips;map["Hips"].rotation=stanceRotation*Quaternion.Euler(pelvisAngles);
   map["Chest"].rotation=Quaternion.Euler(chestAngles);
   map["UpperChest"].rotation=Quaternion.Euler(chestAngles+new Vector3(-2,3,0));
   if(map.ContainsKey("Neck"))map["Neck"].rotation=Quaternion.Euler(0,headYaw,0);
  }
  void Hands(Vector3 wrist,Quaternion swordRotation)
  {
   var handRotation=swordRotation*Quaternion.Inverse(swordMount);
   // Keep both wrists on the same handle throughout the windup and cut.
   var ru=map["RightUpperArm"];var rl=map["RightLowerArm"];var rh=map["RightHand"];
   var lu=map["LeftUpperArm"];var ll=map["LeftLowerArm"];var lh=map["LeftHand"];
   var leftRotation=handRotation*handRelative;
   var offset=handRotation*Vector3.Scale(rightGrip,rh.lossyScale)-swordRotation*Vector3.forward*.16f-
    leftRotation*Vector3.Scale(leftGrip,lh.lossyScale);
   float rr=Vector3.Distance(ru.position,rl.position)+Vector3.Distance(rl.position,rh.position)-.002f;
   float lr=Vector3.Distance(lu.position,ll.position)+Vector3.Distance(ll.position,lh.position)-.002f;
   for(int clear=0;clear<16;clear++)
   {
    for(int i=0;i<20;i++){wrist=lu.position-offset+Vector3.ClampMagnitude(wrist-(lu.position-offset),lr);wrist=ru.position+Vector3.ClampMagnitude(wrist-ru.position,rr);}
    var chest=map["UpperChest"];
    IK(ru,rl,rh,wrist,chest.right*.7f+chest.forward*.45f+Vector3.down*.5f);rh.rotation=handRotation;
    IK(lu,ll,lh,rh.position+offset,-chest.right*.5f+chest.forward*.65f+Vector3.down*.7f);lh.rotation=leftRotation;
    map["Sword"].localPosition=swordMountPosition;map["Sword"].localRotation=swordMount;
    // Offline authoring only: keep the blade and guard outside the character's actual helmet hull.
    if(!Physics.ComputePenetration(swordProbe,swordProbe.transform.position,swordProbe.transform.rotation,
     helmetProbe,helmetProbe.transform.position,helmetProbe.transform.rotation,out var away,out float depth))break;
    wrist=rh.position+away*(depth+.025f);
   }
  }
  static readonly Vector3 LeadLeft=new Vector3(-.22f,.135f,.26f),TrailRight=new Vector3(.22f,.129f,-.22f);
  static readonly Vector3 LeftPlant=new Vector3(-.18f,.135f,.48f),LeftAfterPass=new Vector3(-.20f,.135f,-.32f),RightPlant=new Vector3(.20f,.129f,.52f);
  static readonly Vector3 ReadyWrist=new Vector3(.58f,1.40f,-.42f),PullWrist=new Vector3(.56f,1.17f,-.54f),StepWrist=new Vector3(.58f,1.24f,-.39f);
  static readonly Quaternion ReadySword=Quaternion.Euler(-65,135,65),PullSword=Quaternion.Euler(-40,155,65),StepSword=Quaternion.Euler(-42,150,65);
  void Toes(){map["LeftToes"].localRotation=leftToe;map["RightToes"].localRotation=rightToe;}
  public Pose Ready(float pull)
  {
   Source(26);
   // Reference: left side leads; neither pelvis, sternum nor helmet faces straight forward.
   Body(new Vector3(.015f,Mathf.Lerp(.85f,.65f,pull),Mathf.Lerp(0,-.04f,pull)),
    new Vector3(Mathf.Lerp(0,8,pull),Mathf.Lerp(55,65,pull),0),
    new Vector3(Mathf.Lerp(-4,5.1078f,pull),Mathf.Lerp(68,77.5236f,pull),Mathf.Lerp(-7,-11.955f,pull)),Mathf.Lerp(45,50.801f,pull),StanceTurn);
   Leg("Left",LeadLeft,Quaternion.AngleAxis(25,Vector3.up)*leftFoot);
   Leg("Right",TrailRight,Quaternion.AngleAxis(55,Vector3.up)*rightFoot);Toes();
   var swordRotation=Quaternion.Slerp(ReadySword,PullSword,pull);
   Hands(Vector3.Lerp(ReadyWrist,PullWrist,pull),swordRotation);
   return Capture();
  }
  public Pose Cut(float frame)
  {
   Source(26);Toes();
   if(frame<=29)
   {
    float t=Smooth((frame-26)/3);
    Body(new Vector3(.015f,Mathf.Lerp(.65f,.70f,t),Mathf.Lerp(-.04f,.05f,t)),
     new Vector3(Mathf.Lerp(8,10,t),Mathf.Lerp(65,50,t),0),
     new Vector3(Mathf.Lerp(5.1078f,14,t),Mathf.Lerp(77.5236f,65,t),Mathf.Lerp(-11.955f,0,t)),Mathf.Lerp(50.801f,35,t),StanceTurn);
    Leg("Left",Vector3.Lerp(LeadLeft,LeftPlant,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*.075f),Quaternion.AngleAxis(Mathf.Lerp(25,10,t),Vector3.up)*leftFoot);
    Leg("Right",TrailRight,Quaternion.AngleAxis(55,Vector3.up)*rightFoot);
    Hands(Vector3.Lerp(PullWrist,StepWrist,t),Quaternion.Slerp(PullSword,StepSword,t));
   }
   else if(frame<=34)
   {
    float t=Smooth((frame-29)/5);float cut=Mathf.Pow(t,1.2f);
    Body(new Vector3(Mathf.Lerp(.015f,0,t),Mathf.Lerp(.70f,.63f,t),Mathf.Lerp(.05f,.12f,t)),
     new Vector3(Mathf.Lerp(10,23,t),Mathf.Lerp(50,-35,t),0),
     new Vector3(Mathf.Lerp(14,35,t),Mathf.Lerp(65,-60,t),0),Mathf.Lerp(35,-10,t),Mathf.Lerp(StanceTurn,0,t));
    // The planted left foot goes behind the moving pelvis; the right foot passes it to strike.
    Leg("Left",Vector3.Lerp(LeftPlant,LeftAfterPass,t),Quaternion.AngleAxis(Mathf.Lerp(10,-20,t),Vector3.up)*leftFoot);
    Leg("Right",Vector3.Lerp(TrailRight,RightPlant,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*.14f),Quaternion.AngleAxis(Mathf.Lerp(55,-5,t),Vector3.up)*rightFoot);
    float angle=Mathf.Lerp(138,-40,cut)*Mathf.Deg2Rad;float yaw=Mathf.Lerp(-30,-10,cut)*Mathf.Deg2Rad;
    var blade=new Vector3(Mathf.Sin(yaw)*Mathf.Cos(angle),Mathf.Sin(angle),Mathf.Cos(yaw)*Mathf.Cos(angle));
    var rotation=Quaternion.Slerp(StepSword,Quaternion.Euler(40,-10,275),cut);
    rotation=Quaternion.FromToRotation(rotation*Vector3.forward,blade)*rotation;
    var outside=new Vector3(.64f,1.20f,.54f);
    var wrist=cut<.5f?Vector3.Lerp(StepWrist,outside,Smooth(cut*2)):
     Vector3.Lerp(outside,new Vector3(-.24f,.72f,.55f),Smooth((cut-.5f)*2));
    Hands(wrist,rotation);
   }
   else
   {
    float t=Smooth((frame-34)/9);
    Body(new Vector3(0,Mathf.Lerp(.63f,.60f,t),Mathf.Lerp(.12f,.10f,t)),
     new Vector3(Mathf.Lerp(23,26,t),Mathf.Lerp(-35,-45,t),0),
     new Vector3(Mathf.Lerp(35,42,t),Mathf.Lerp(-60,-75,t),0),Mathf.Lerp(-10,-25,t));
    Leg("Left",LeftAfterPass,Quaternion.AngleAxis(-20,Vector3.up)*leftFoot);
    Leg("Right",RightPlant,Quaternion.AngleAxis(-5,Vector3.up)*rightFoot);
    Hands(Vector3.Lerp(new Vector3(-.24f,.72f,.55f),new Vector3(-.46f,.64f,.24f),t),Quaternion.Slerp(Quaternion.Euler(40,-10,275),Quaternion.Euler(30,-65,275),t));
   }
   return Capture();
  }
  public void Dispose()=>UnityEngine.Object.DestroyImmediate(root);
 }
 static Vector3 Grip(SkinnedMeshRenderer skin,string side)
 {
  var mesh=skin.sharedMesh;int h=Array.FindIndex(skin.bones,t=>t.name==side+"Hand"),e=Array.FindIndex(skin.bones,t=>t.name==side+"LowerArm");
  var axis=((Vector3)mesh.bindposes[h].inverse.GetColumn(3)-(Vector3)mesh.bindposes[e].inverse.GetColumn(3)).normalized;
  return mesh.bindposes[h].MultiplyVector(axis*.095f-Vector3.ProjectOnPlane(Vector3.up,axis).normalized*.025f);
 }
 static void IK(Transform upper,Transform lower,Transform end,Vector3 target,Vector3 pole)
 {
  Vector3 origin=upper.position;float l1=Vector3.Distance(origin,lower.position),l2=Vector3.Distance(lower.position,end.position);
  Vector3 dir=(target-origin).normalized;float d=Mathf.Clamp(Vector3.Distance(origin,target),Mathf.Abs(l1-l2)+.00001f,l1+l2-.00001f);
  var bend=Vector3.ProjectOnPlane(pole,dir).normalized;
  float x=(l1*l1-l2*l2+d*d)/(2*d);var knee=origin+dir*x+bend*Mathf.Sqrt(Mathf.Max(0,l1*l1-x*x));
  upper.rotation=Quaternion.FromToRotation(lower.position-origin,knee-origin)*upper.rotation;
  lower.rotation=Quaternion.FromToRotation(end.position-lower.position,origin+dir*d-lower.position)*lower.rotation;
 }
 static float Smooth(float t)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(t));
 static NewPlayerAttackCombo Player()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
  var s=SceneManager.GetActiveScene();if(s.path!="Assets/Scenes/3DBattleDebugScene.unity")throw new Exception("Open debug scene first");
  return s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<NewPlayerAttackCombo>(true)).Single(c=>c.name=="New Player - Attack Combo");
 }
 static AnimationClip Bake(Rig r,string name,float duration,Action<float> sample)
 {
  string path=Folder+"/"+name+".anim";
  var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
  if(clip==null){clip=new AnimationClip();clip.name=name;AssetDatabase.CreateAsset(clip,path);}
  else {var empty=new AnimationClip();EditorUtility.CopySerialized(empty,clip);UnityEngine.Object.DestroyImmediate(empty);clip.name=name;}
  // Preserve all source bindings (including scale and armor), trimming to the authored duration.
  var bindings=AnimationUtility.GetCurveBindings(r.source);
  var curves=bindings.ToDictionary(b=>b,b=>AnimationUtility.GetEditorCurve(r.source,b));
  foreach(var b in bindings){var curve=curves[b];if(duration<r.source.length){var keys=curve.keys.Where(k=>k.time<duration).ToList();keys.Add(new Keyframe(duration,curve.Evaluate(duration)));curves[b]=new AnimationCurve(keys.ToArray());}}
  var records=r.bones.Select(t=>Enumerable.Range(0,7).Select(_=>new List<Keyframe>()).ToArray()).ToArray();
  int count=Mathf.CeilToInt(duration*120);
  for(int i=0;i<=count;i++)
  {
   float time=duration*i/count;sample(time);
   for(int b=0;b<r.bones.Length;b++){var t=r.bones[b];var q=t.localRotation;
    if(i>0){var prev=records[b];var old=new Quaternion(prev[3][i-1].value,prev[4][i-1].value,prev[5][i-1].value,prev[6][i-1].value);if(Quaternion.Dot(q,old)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);}
    for(int c=0;c<7;c++)records[b][c].Add(new Keyframe(time,c<3?t.localPosition[c]:q[c-3]));}
  }
  for(int i=0;i<r.bones.Length;i++)
  {
   var pathBone=AnimationUtility.CalculateTransformPath(r.bones[i],r.root.transform);
   foreach(var b in bindings.Where(b=>b.path==pathBone&&(b.propertyName.Contains("Rotation")||b.propertyName.Contains("Euler")||b.propertyName.Contains("Position"))))curves.Remove(b);
   for(int c=0;c<7;c++){var curve=new AnimationCurve(records[i][c].ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}curves[EditorCurveBinding.FloatCurve(pathBone,typeof(Transform),c<3?"m_LocalPosition."+"xyz"[c]:"m_LocalRotation."+"xyzw"[c-3])]=curve;}
  }
  // Submit all quaternion channels together; replacing them individually causes transient
  // mismatched key counts and Unity's Key index out of range assertion during authoring.
  AnimationUtility.SetEditorCurves(clip,curves.Keys.ToArray(),curves.Values.ToArray());
  AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
  var settings=AnimationUtility.GetAnimationClipSettings(r.source);settings.loopTime=false;settings.startTime=0;settings.stopTime=duration;AnimationUtility.SetAnimationClipSettings(clip,settings);
  clip.frameRate=30;clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);return clip;
 }
 public static string Build()=>BuildInternal(true);
 public static string UpdateRightSideGuard()=>BuildInternal(false);
 public static string UpdateTorsoDistribution()
 {
  var live=Player().GetComponent<BattlePlayerController>().animator;
  foreach(var window in Resources.FindObjectsOfTypeAll<AnimationWindow>()){window.playing=false;window.previewing=false;}
  if(AnimationMode.InAnimationMode())AnimationMode.StopAnimationMode();
  // The unsaved reference moved/turned the visual root. Its turn is now baked into
  // this finisher only; restore the saved root to avoid turning every other attack.
  Undo.RecordObject(live.transform,"Bake reference stance into debug finisher");
  live.transform.localPosition=Vector3.zero;live.transform.localRotation=Quaternion.identity;
  return BuildInternal(false);
 }
 static string BuildInternal(bool configure)
 {
  var c=Player();var live=c.GetComponent<BattlePlayerController>().animator;
  foreach(var window in Resources.FindObjectsOfTypeAll<AnimationWindow>()){window.playing=false;window.previewing=false;}
  if(AnimationMode.InAnimationMode())AnimationMode.StopAnimationMode();
  if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/3DBattle/3DBattleDebug","Finisher");
  AnimationClip prep,attack;
  using(var r=new Rig(live))
  {
   var start=r.Source(13);var ready=r.Ready(0);var pulled=r.Ready(1);
   prep=Bake(r,"DebugFinisher_Preparation",ReadyTime+Windup,t=>{
    r.Source(26);
    if(t<=ReadyTime)r.Blend(start,ready,Smooth(t/ReadyTime));
    else r.Ready(Smooth((t-ReadyTime)/Windup));
   });
   var follow=r.Cut(43);var idle=r.Source(104);
   attack=Bake(r,"DebugFinisher_RetreatRush",r.source.length,t=>{
    float f=t*30;
    r.Source(f);
    if(f>13&&f<=26)r.Blend(start,pulled,Smooth((f-13)/13));
    else if(f>26&&f<=43)r.Cut(f);
    else if(f>43)r.Blend(follow,idle,Smooth((f-43)/(104-43)));
   });
  }
  if(!configure)return JsonConvert.SerializeObject(new{updated=new[]{AssetDatabase.GetAssetPath(prep),AssetDatabase.GetAssetPath(attack)},scene=c.gameObject.scene.path,settingsPreserved=true});
  string controllerPath=Folder+"/DebugFinisher.controller";
  if(AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)==null&&!AssetDatabase.CopyAsset(Controller,controllerPath))throw new Exception("Controller copy failed");
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
  var sm=controller.layers[0].stateMachine;
  foreach(var clip in new[]{prep,attack}){var s=sm.states.Select(x=>x.state).FirstOrDefault(s=>s.name==clip.name);if(s==null)s=sm.AddState(clip.name);s.motion=clip;s.writeDefaultValues=true;EditorUtility.SetDirty(s);}
  EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
  Undo.RecordObjects(new UnityEngine.Object[]{live,c.motions,c.GetComponent<DebugPlayerSpecialCombo>()},"Author debug special finisher");
  live.runtimeAnimatorController=controller;
  c.motions.attacks[0].clip=attack;
  // Leading left-foot step first, then rush with the passing right foot and overhead cut.
  c.motions.attacks[0].damageWindows=new[]{new Vector2(32.5f,36)};
  var rules=c.GetComponent<DebugPlayerSpecialCombo>();rules.finisherPreparation=prep;rules.finisherReadyTime=ReadyTime;rules.finisherWindupSeconds=Windup;rules.finisherHoldSeconds=.8f;rules.finisherPoseFrame=26;
  rules.finisherDashStartFrame=29;rules.finisherDashEndFrame=34;
  var feel=c.GetComponent<DebugPlayerCombatFeel>();Undo.RecordObject(feel,"Match finisher footsteps and slash timing");
  feel.attacks[0].segments=new[]{new DebugPlayerCombatFeel.Segment(13,.18f),new DebugPlayerCombatFeel.Segment(26,.11f),new DebugPlayerCombatFeel.Segment(29,.09f),new DebugPlayerCombatFeel.Segment(34,.12f),new DebugPlayerCombatFeel.Segment(43,.10f),new DebugPlayerCombatFeel.Segment(86,.16f),new DebugPlayerCombatFeel.Segment(0,.18f)};
  EditorUtility.SetDirty(feel);
  EditorUtility.SetDirty(c.motions);EditorUtility.SetDirty(live);EditorUtility.SetDirty(rules);AssetDatabase.SaveAssetIfDirty(c.motions);
  EditorSceneManager.MarkSceneDirty(c.gameObject.scene);if(!EditorSceneManager.SaveScene(c.gameObject.scene))throw new Exception("Save failed");
  Selection.activeGameObject=c.gameObject;
  return JsonConvert.SerializeObject(new{preparation=AssetDatabase.GetAssetPath(prep),attack=AssetDatabase.GetAssetPath(attack),controller=controllerPath,hold=rules.finisherHoldSeconds,windup=Windup,scene=c.gameObject.scene.path});
 }
 public static string Review()=>RenderReview(new Vector3(4,2.2f,3),"DebugAuthoredFinisherReview.png");
 public static string ReviewRear()=>RenderReview(new Vector3(4,2.2f,-3),"DebugAuthoredFinisherRear.png");
 static string RenderReview(Vector3 view,string filename)
 {
  var c=Player();var live=c.GetComponent<BattlePlayerController>().animator;
  var rules=c.GetComponent<DebugPlayerSpecialCombo>();
  var oldDirty=c.gameObject.scene.isDirty;
  using(var r=new Rig(live))
  {
   var cameraObject=new GameObject("Finisher review camera");var camera=cameraObject.AddComponent<Camera>();
   camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.16f,.20f);camera.orthographic=true;camera.orthographicSize=1.9f;camera.nearClipPlane=.01f;camera.farClipPlane=30;camera.cullingMask=1<<31;
   foreach(var t in r.root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
   // Force a fresh skin evaluation for each sample rendered within this one Editor frame.
   var skins=r.root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
   var baked=skins.Select(s=>new Mesh()).ToArray();
   var proxies=skins.Select(s=>{var g=new GameObject("Baked review skin");g.layer=31;g.transform.SetParent(s.transform,false);g.AddComponent<MeshFilter>();g.AddComponent<MeshRenderer>().sharedMaterials=s.sharedMaterials;s.enabled=false;return g;}).ToArray();
   var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.layer=31;floor.transform.position=new Vector3(0,-.045f,0);floor.transform.localScale=new Vector3(10,.05f,10);
   var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.color=new Color(.22f,.24f,.26f);floor.GetComponent<Renderer>().sharedMaterial=mat;
   const int w=500,h=600;var sheet=new Texture2D(w*4,h*2,TextureFormat.RGB24,false);
   var clip=new[]{rules.finisherPreparation,rules.finisherPreparation,c.motions.attacks[0].clip,c.motions.attacks[0].clip,c.motions.attacks[0].clip,c.motions.attacks[0].clip,c.motions.attacks[0].clip,c.motions.attacks[0].clip};
   var time=new[]{ReadyTime,ReadyTime+Windup,28f/30,29f/30,31f/30,32.5f/30,34f/30,43f/30};
   var report=new List<object>();
   try {
    for(int i=0;i<8;i++){
     clip[i].SampleAnimation(r.root,time[i]);
     for(int b=0;b<skins.Length;b++){skins[b].BakeMesh(baked[b],true);proxies[b].GetComponent<MeshFilter>().sharedMesh=baked[b];}
     camera.transform.position=view;camera.transform.LookAt(new Vector3(0,1.3f,0));
     var rt=RenderTexture.GetTemporary(w,h,24);var old=RenderTexture.active;
     camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
     var tex=new Texture2D(w,h,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();sheet.SetPixels(i%4*w,(1-i/4)*h,w,h,tex.GetPixels());
     camera.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(tex);
     report.Add(new{index=i,clip=clip[i].name,time=time[i],hips=r.map["Hips"].position.ToString("F3"),left=r.map["LeftFoot"].position.ToString("F3"),right=r.map["RightFoot"].position.ToString("F3")});
    }
    sheet.Apply();string path="Assets/3DBattle/Authoring~/"+filename;File.WriteAllBytes(path,sheet.EncodeToPNG());
    return JsonConvert.SerializeObject(new{path,report});
   } finally{foreach(var m in baked)UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(sheet);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(floor);UnityEngine.Object.DestroyImmediate(mat);}
  }
 }
 public static string InspectSkin()
 {
  var c=Player();using(var r=new Rig(c.GetComponent<BattlePlayerController>().animator)) {
   r.Ready(0);
   return JsonConvert.SerializeObject(r.root.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s=>{
    var m=new Mesh();s.BakeMesh(m,false);var noScale=m.bounds;s.BakeMesh(m,true);var withScale=m.bounds;UnityEngine.Object.DestroyImmediate(m);
    return new{name=s.name,scale=s.transform.lossyScale.ToString(),local=s.transform.localScale.ToString(),mesh=s.sharedMesh.name,bodyBounds=s.sharedMesh.bounds.ToString(),noScale=noScale.ToString(),withScale=withScale.ToString(),bones=s.bones.Select(t=>new{name=t.name,position=t.position.ToString()}).Where(t=>t.name=="RightHand"||t.name=="RightFoot").ToArray()};
   }).ToArray());
  }
 }
 public static string InspectHelmet()
 {
  var c=Player();using(var r=new Rig(c.GetComponent<BattlePlayerController>().animator)) {
   r.Ready(0);
   return JsonConvert.SerializeObject(new{bones=r.map.Values.Where(t=>new[]{"Head","Neck","RightHand","LeftHand","RightUpperArm","LeftUpperArm","Sword"}.Contains(t.name)).Select(t=>new{t.name,position=t.position.ToString("F3"),rotation=t.rotation.eulerAngles.ToString("F1")}).ToArray(),renderers=r.root.GetComponentsInChildren<Renderer>(true).Select(s=>new{s.name,path=AnimationUtility.CalculateTransformPath(s.transform,r.root.transform),bounds=s.bounds.ToString(),mesh=s.GetComponent<MeshFilter>()!=null?s.GetComponent<MeshFilter>().sharedMesh.name:"skin"}).ToArray()});
  }
 }
 public static string VerifyHelmetClearance()
 {
  var c=Player();var rules=c.GetComponent<DebugPlayerSpecialCombo>();
  using(var r=new Rig(c.GetComponent<BattlePlayerController>().animator))
  {
   var helmet=r.helmetProbe;var sword=r.swordProbe;
   var probeObject=new GameObject("Temporary arm clearance probe");var probe=probeObject.AddComponent<CapsuleCollider>();probe.radius=.04f;
   var stages=new List<object>();
   try {
    foreach(string stage in new[]{"settle-into-ready","ready-pull","left-step","right-step-cut","recovery"}){
     float maxSword=0,maxArm=0,maxForearm=0,worstSword=0,worstArm=0;string arm="";
     for(int i=0;i<=120;i++){
      bool preparation=stage=="ready-pull"||stage=="settle-into-ready";
      float value=preparation?Mathf.Lerp(stage=="ready-pull"?ReadyTime:0,stage=="ready-pull"?ReadyTime+Windup:ReadyTime,i/120f):Mathf.Lerp(stage=="left-step"?26:stage=="recovery"?43:29,stage=="left-step"?29:stage=="recovery"?104:43,i/120f);
      var clip=preparation?rules.finisherPreparation:c.motions.attacks[0].clip;clip.SampleAnimation(r.root,preparation?value:value/30f);
      Vector3 direction;float depth;
      if(Physics.ComputePenetration(helmet,helmet.transform.position,helmet.transform.rotation,sword,sword.transform.position,sword.transform.rotation,out direction,out depth)&&depth>maxSword){maxSword=depth;worstSword=value;}
      foreach(string side in new[]{"Left","Right"})foreach(string part in new[]{"Upper","Lower"}){
       var a=r.map[side+(part=="Upper"?"UpperArm":"LowerArm")].position;var b=r.map[side+(part=="Upper"?"LowerArm":"Hand")].position;
       probeObject.transform.SetPositionAndRotation((a+b)*.5f,Quaternion.FromToRotation(Vector3.up,b-a));probe.height=Vector3.Distance(a,b)+2*probe.radius;
       if(Physics.ComputePenetration(helmet,helmet.transform.position,helmet.transform.rotation,probe,probe.transform.position,probe.transform.rotation,out direction,out depth)){
        if(part=="Lower")maxForearm=Mathf.Max(maxForearm,depth);
        if(depth>maxArm){maxArm=depth;worstArm=value;arm=side+part;}
       }
      }
     }
     stages.Add(new{stage,maxSwordPenetration=maxSword,worstSword,maxForearmPenetration=maxForearm,maxArmPenetration=maxArm,worstArm,arm});
    }
    var result=JsonConvert.SerializeObject(new{stages},Formatting.Indented);File.WriteAllText("Assets/3DBattle/Authoring~/DebugFinisherHelmetClearance.json",result);return result;
   } finally{UnityEngine.Object.DestroyImmediate(probeObject);}
  }
 }
 public static string VerifyTorsoClearance()
 {
  var c=Player();var rules=c.GetComponent<DebugPlayerSpecialCombo>();
  using(var r=new Rig(c.GetComponent<BattlePlayerController>().animator))
  {
   var skin=r.root.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=skin.sharedMesh;var weights=mesh.boneWeights;
   var torsoBones=new HashSet<int>(Enumerable.Range(0,skin.bones.Length).Where(i=>new[]{"Hips","Chest","UpperChest"}.Contains(skin.bones[i].name)));
   Func<BoneWeight,float> weight=w=>(torsoBones.Contains(w.boneIndex0)?w.weight0:0)+(torsoBones.Contains(w.boneIndex1)?w.weight1:0)+(torsoBones.Contains(w.boneIndex2)?w.weight2:0)+(torsoBones.Contains(w.boneIndex3)?w.weight3:0);
   var indices=Enumerable.Range(0,weights.Length).Where(i=>weight(weights[i])>.75f).ToArray();
   var remap=indices.Select((v,i)=>new{v,i}).ToDictionary(x=>x.v,x=>x.i);var triangles=new List<int>();var source=mesh.triangles;
   for(int i=0;i<source.Length;i+=3)if(remap.ContainsKey(source[i])&&remap.ContainsKey(source[i+1])&&remap.ContainsKey(source[i+2])){triangles.Add(remap[source[i]]);triangles.Add(remap[source[i+1]]);triangles.Add(remap[source[i+2]]);}
   var baked=new Mesh();var hull=new Mesh();var torsoObject=new GameObject("Temporary torso hull");torsoObject.transform.SetParent(skin.transform,false);var torso=torsoObject.AddComponent<MeshCollider>();torso.convex=true;torso.isTrigger=true;
   var probeObject=new GameObject("Temporary forearm probe");var arm=probeObject.AddComponent<CapsuleCollider>();arm.radius=.035f;arm.isTrigger=true;
   var handObject=new GameObject("Temporary hand probe");var hand=handObject.AddComponent<SphereCollider>();hand.radius=.065f;hand.isTrigger=true;
   var results=new List<object>();
   try {
    foreach(string stage in new[]{"ready-pull","left-step","right-step-cut"}){
     float maxArm=0,maxHand=0,worstArm=0,worstHand=0;
     for(int i=0;i<=60;i++){
      float value=stage=="ready-pull"?Mathf.Lerp(ReadyTime,ReadyTime+Windup,i/60f):Mathf.Lerp(stage=="left-step"?26:29,stage=="left-step"?29:43,i/60f);
      var clip=stage=="ready-pull"?rules.finisherPreparation:c.motions.attacks[0].clip;clip.SampleAnimation(r.root,stage=="ready-pull"?value:value/30f);
      skin.BakeMesh(baked,true);var vertices=baked.vertices;torso.sharedMesh=null;hull.Clear();hull.vertices=indices.Select(j=>vertices[j]).ToArray();hull.triangles=triangles.ToArray();hull.RecalculateBounds();torso.sharedMesh=hull;
      Vector3 a=r.map["LeftLowerArm"].position,b=r.map["LeftHand"].position;
      probeObject.transform.SetPositionAndRotation((a+b)*.5f,Quaternion.FromToRotation(Vector3.up,b-a));arm.height=Vector3.Distance(a,b)+arm.radius*2;
      Vector3 away;float depth;
      if(Physics.ComputePenetration(arm,arm.transform.position,arm.transform.rotation,torso,torso.transform.position,torso.transform.rotation,out away,out depth)&&depth>maxArm){maxArm=depth;worstArm=value;}
      handObject.transform.position=r.map["LeftHand"].TransformPoint(r.leftGrip);
      if(Physics.ComputePenetration(hand,hand.transform.position,hand.transform.rotation,torso,torso.transform.position,torso.transform.rotation,out away,out depth)&&depth>maxHand){maxHand=depth;worstHand=value;}
     }
     results.Add(new{stage,maxLeftForearmPenetration=maxArm,worstArm,maxLeftHandPenetration=maxHand,worstHand});
    }
    return JsonConvert.SerializeObject(new{torsoVertexCount=indices.Length,results},Formatting.Indented);
   }finally{UnityEngine.Object.DestroyImmediate(probeObject);UnityEngine.Object.DestroyImmediate(handObject);UnityEngine.Object.DestroyImmediate(torsoObject);UnityEngine.Object.DestroyImmediate(hull);UnityEngine.Object.DestroyImmediate(baked);}
  }
 }
}
