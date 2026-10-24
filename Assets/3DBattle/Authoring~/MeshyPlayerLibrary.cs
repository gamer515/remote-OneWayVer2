using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;

// Source ranges are Blender frames at 30 FPS. Blender import places source t=0 at frame 1.
public static class MeshyPlayerLibrary {
 const string Root="Assets/3DBattle/MeshyPlayer/";
 const string Folder=Root+"Converted/";
 const string Prefab="Assets/3DBattle/Prefabs/BattleKnight.prefab";
 const string Controller="Assets/3DBattle/Animations/KnightBattle.controller";
 const float FPS=60, Prep=.4f, Clearance=.015f;
 class Spec {
  public string source,name,state;public int start,end;public bool roll,shield,loop;
  public Spec(string s,string n,int a,int b,string stateName=null){source=s;name=n;start=a;end=b;state=stateName??n;}
 }
 static Spec[] Specs()=>new[]{
  new Spec("Weapon_Combo","MeshyWeaponCombo_Player",0,75,"AttackNew"),
  new Spec("Double_Combo_Attack","MeshyDoubleComboAttack_Player",30,45),
  new Spec("Attack","MeshyAttack_Player",22,85),
  new Spec("Female_Crouch_Pick_Fr","MeshyCrouchPick_Player",0,60),
  new Spec("Run_03","MeshyRun_Player",1,-1){loop=true},
  new Spec("Shield_Push_Left","MeshyShieldPushLeft_Player",1,-1){shield=true},
  new Spec("Run_Jump_and_Roll","MeshyGroundRoll_Player",57,104){roll=true}
 };
 class Pose {public Vector3[] p;public Quaternion[] q;public float time;}
 static Pose Capture(Transform[] bones,float time)=>new Pose{p=bones.Select(t=>t.localPosition).ToArray(),q=bones.Select(t=>t.localRotation).ToArray(),time=time};
 static void Apply(Transform[] bones,Pose p){for(int i=0;i<bones.Length;i++){bones[i].localPosition=p.p[i];bones[i].localRotation=p.q[i];}}
 static string ClipPath(Spec s)=>s.state=="AttackNew"?"Assets/3DBattle/Animations/MeshyWeaponCombo_Player.anim":Folder+s.name+".anim";
 public static string Run()=>Build(false);
 public static string Repair()=>Build(true);
 static string Build(bool repairsOnly){
  if(EditorApplication.isPlaying)throw new Exception("Stop Play Mode first");
  if(!AssetDatabase.IsValidFolder(Folder.TrimEnd('/')))AssetDatabase.CreateFolder(Root.TrimEnd('/'),"Converted");
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
  var idle=controller.animationClips.First(c=>c.name.EndsWith("Idle_swordRight"));
  string backup="Assets/3DBattle/Authoring~/Backups/BeforeMeshyPlayerLibrary_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");Directory.CreateDirectory(backup);
  File.Copy(Controller,backup+"/KnightBattle.controller");
  var oldAttack="Assets/3DBattle/Animations/MeshyWeaponCombo_Player.anim";if(File.Exists(oldAttack))File.Copy(oldAttack,backup+"/MeshyWeaponCombo_Player.anim");
  var report=new List<string>{"Blender frame ranges at 30 FPS; frame 1 = source time 0; frame 0 holds first pose. Output samples at 60 FPS.","Root horizontal travel removed; movement remains controlled by gameplay code. Ground roll corrects body/helmet/shoulder armour; sword excluded from ground solver."};
  var results=new List<AnimationClip>();
  foreach(var spec in Specs()){
   if(repairsOnly&&(spec.roll||spec.loop))continue;
   if(File.Exists(ClipPath(spec)))File.Copy(ClipPath(spec),backup+"/"+spec.name+"_before.anim");
   string sourcePath=Root+"Meshy_AI_KnightCharacter_"+spec.source+".fbx";
   var sourceClip=AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview__"));
   var src=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
   var dst=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
   src.hideFlags=dst.hideFlags=HideFlags.HideAndDontSave;dst.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   try{
    idle.SampleAnimation(dst,0);
    var skin=dst.GetComponentInChildren<SkinnedMeshRenderer>();
    var sourceSkin=src.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="Knight");
    var bones=dst.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();
    var target=bones.ToDictionary(t=>t.name);var transforms=dst.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
    var source=src.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
    var sb=sourceSkin.bones.Select((b,i)=>new{b.name,m=sourceSkin.transform.localToWorldMatrix*sourceSkin.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.m);
    var db=skin.bones.Select((b,i)=>new{b.name,m=skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.m);
    var map=new Dictionary<string,string>{{"Body","Hips"},{"Hips","Hips"},{"Abdomen","Spine02"},{"Torso","Spine"},{"Neck","neck"},{"Head","Head"}};
    foreach(string side in new[]{"L","R"}){string s=side=="L"?"Left":"Right";if(!(spec.shield&&side=="R")){map["Shoulder."+side]=s+"Shoulder";map["UpperArm."+side]=s+"Arm";map["LowerArm."+side]=s+"ForeArm";map["Palm."+side]=s+"Hand";}map["UpperLeg."+side]=s+"UpLeg";map["LowerLeg."+side]=s+"Leg";map["Foot."+side]=s+"Foot";}
    Vector3 a=sb["RightArm"].GetColumn(3)-sb["LeftArm"].GetColumn(3);a.y=0;
    Vector3 b=db["UpperArm.R"].GetColumn(3)-db["UpperArm.L"].GetColumn(3);b.y=0;
    Quaternion align=Quaternion.FromToRotation(a,b);
    float scale=Vector3.Distance(db["Head"].GetColumn(3),db["Body"].GetColumn(3))/Vector3.Distance(sb["Head"].GetColumn(3),sb["Hips"].GetColumn(3));
    var idlePose=Capture(bones,0);int last=spec.end<0?Mathf.RoundToInt(sourceClip.length*30)+1:spec.end;
    if((last-1)/30f>sourceClip.length+.002f)throw new Exception("Frame beyond source: "+spec.source);
    float duration=(last-spec.start)/30f;int count=Mathf.RoundToInt(duration*FPS);var poses=new List<Pose>();
    sourceClip.SampleAnimation(src,Mathf.Max(0,(spec.start-1)/30f));Vector3 initialHip=source["Hips"].position;
    for(int f=0;f<=count;f++){
     float t=duration*f/count;Apply(bones,idlePose);
     sourceClip.SampleAnimation(src,Mathf.Clamp((spec.start-1)/30f+t,0,sourceClip.length));
     // The roll is cut after several metres of source travel: remove its absolute offset too.
     var travel=source["Hips"].position-(spec.roll?(Vector3)sb["Hips"].GetColumn(3):initialHip);travel.y=0;
     foreach(var bone in bones)if(map.TryGetValue(bone.name,out string s)){
      var rest=sb[s];var targetBind=db[bone.name];
      bone.SetPositionAndRotation((Vector3)targetBind.GetColumn(3)+align*(source[s].position-(Vector3)rest.GetColumn(3)-travel)*scale,
       align*source[s].rotation*Quaternion.Inverse(rest.rotation)*Quaternion.Inverse(align)*targetBind.rotation);
     }
     if(!spec.roll&&!spec.loop&&!spec.shield){
      // Auto-rigged wrist axes do not encode the original sword grip. Reconstruct
      // the thumb-side grip axis from the source fingers, then rotate hand + thumb together.
      var wrist=source["RightHand"];
      Vector3 fingers=source["RightHandMiddle1"].position-wrist.position;
      Vector3 thumb=source["RightHandThumb1"].position-source["RightHandMiddle1"].position;
      Vector3 blade=align*Vector3.ProjectOnPlane(thumb,fingers.normalized).normalized;
      if(blade.sqrMagnitude>.5f){
       var palm=target["Palm.R"];var sword=transforms["Sword"];
       Quaternion delta=Quaternion.FromToRotation(sword.forward,blade);
       var thumbBone=target["Thumb.L"]; // Original Knight's right thumb is named Thumb.L.
       Vector3 thumbPosition=palm.position+delta*(thumbBone.position-palm.position);
       Quaternion thumbRotation=delta*thumbBone.rotation;
       palm.rotation=delta*palm.rotation;thumbBone.SetPositionAndRotation(thumbPosition,thumbRotation);
      }
     }
     poses.Add(Capture(bones,t+(spec.roll?Prep:0)));
    }
    if(spec.loop){ // Blend a few ending samples to the first pose without discarding the source cycle.
     var first=poses[0];for(int i=Math.Max(1,poses.Count-4);i<poses.Count;i++){float w=(i-(poses.Count-4))/3f;w=Mathf.SmoothStep(0,1,w);for(int j=0;j<bones.Length;j++){poses[i].p[j]=Vector3.Lerp(poses[i].p[j],first.p[j],w);poses[i].q[j]=Quaternion.Slerp(poses[i].q[j],first.q[j],w);}}
    }
    if(spec.roll){
     Apply(bones,idlePose);
     // Keep existing hand grip and create a stable left-leading stance before crouching.
     target["Body"].position+=Vector3.down*.035f;
     foreach(string side in new[]{"L","R"}){
      var foot=target["Foot."+side];var goal=foot.position+Vector3.forward*(side=="L"?.24f:-.12f);
      var end=transforms["LowerLeg."+side+"_end"];var endGoal=end.position+(goal-foot.position);
      SolveLeg(target["UpperLeg."+side],target["LowerLeg."+side],end,endGoal);
      foot.position=goal;
     }
     var stance=Capture(bones,0);var crouch=poses[0];var lead=new List<Pose>();int prepFrames=Mathf.RoundToInt(Prep*FPS);
     for(int f=0;f<prepFrames;f++){
      float w=Mathf.SmoothStep(0,1,f/(float)prepFrames);var p=new Pose{time=f/FPS,p=new Vector3[bones.Length],q=new Quaternion[bones.Length]};
      for(int j=0;j<bones.Length;j++){p.p[j]=Vector3.Lerp(stance.p[j],crouch.p[j],w);p.q[j]=Quaternion.Slerp(stance.q[j],crouch.q[j],w);}lead.Add(p);
     }
     poses.InsertRange(0,lead);
     var corrections=new float[poses.Count];for(int i=0;i<poses.Count;i++){Apply(bones,poses[i]);corrections[i]=Clearance-MinHeight(dst,skin);}
     int rootIndex=Array.FindIndex(bones,t=>t.name=="Bone");float maxLift=0;
     for(int i=0;i<poses.Count;i++){
      float smooth=0,total=0;for(int d=-2;d<=2;d++){float w=3-Mathf.Abs(d);smooth+=corrections[Mathf.Clamp(i+d,0,poses.Count-1)]*w;total+=w;}
      float lift=Mathf.Max(corrections[i],smooth/total);maxLift=Mathf.Max(maxLift,lift);
      poses[i].p[rootIndex]+=bones[rootIndex].parent.InverseTransformVector(Vector3.up*lift);
     }
     report.Add("Roll: source Blender 57..104; 0.4s left-leading stance-to-crouch added; maximum vertical correction="+maxLift.ToString("F4")+"m at authoring scale.");
    }
    if(!spec.roll&&!spec.loop&&!spec.shield){
     // Aim the tip along the actual cutting stroke, measured relative to the hips
     // so floor correction and body bobbing cannot steer the sword independently.
     var wristPositions=new Vector3[poses.Count];var palm=target["Palm.R"];var sword=transforms["Sword"];
     for(int i=0;i<poses.Count;i++){Apply(bones,poses[i]);wristPositions[i]=palm.position-target["Body"].position;}
     float beforeAngle=0,afterAngle=0;int active=0;
     for(int i=0;i<poses.Count;i++){
      Apply(bones,poses[i]);int lo=Mathf.Max(0,i-3),hi=Mathf.Min(poses.Count-1,i+3);
      var velocity=(wristPositions[hi]-wristPositions[lo])/Mathf.Max(.001f,poses[hi].time-poses[lo].time);
      float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.08f,.35f,velocity.magnitude));
      if(weight>0){
       var direction=Vector3.Slerp(sword.forward,velocity.normalized,weight).normalized;
       if(weight>.999f){beforeAngle+=Vector3.Angle(sword.forward,velocity);active++;}
       var delta=Quaternion.FromToRotation(sword.forward,direction);var thumb=target["Thumb.L"];
       var thumbPosition=palm.position+delta*(thumb.position-palm.position);var thumbRotation=delta*thumb.rotation;
       palm.rotation=delta*palm.rotation;thumb.SetPositionAndRotation(thumbPosition,thumbRotation);
       if(weight>.999f)afterAngle+=Vector3.Angle(sword.forward,velocity);
       poses[i]=Capture(bones,poses[i].time);
      }
     }
     report.Add(spec.name+" sword tip vs cutting trajectory: active samples="+active+"; mean angle before="+(beforeAngle/Mathf.Max(1,active)).ToString("F2")+"; after="+(afterAngle/Mathf.Max(1,active)).ToString("F2")+" degrees; slow poses smoothly blend to source grip.");
    }
    if(!spec.roll&&!spec.loop){
     var lifts=new float[poses.Count];float before=100,maxLift=0;
     for(int i=0;i<poses.Count;i++){Apply(bones,poses[i]);float y=MinHeight(dst,skin);before=Mathf.Min(before,y);lifts[i]=Mathf.Max(0,Clearance-y);}
     int rootIndex=Array.FindIndex(bones,t=>t.name=="Bone");
     for(int i=0;i<poses.Count;i++){
      float average=0,total=0;for(int d=-2;d<=2;d++){float w=3-Mathf.Abs(d);average+=lifts[Mathf.Clamp(i+d,0,poses.Count-1)]*w;total+=w;}
      float lift=Mathf.Max(lifts[i],average/total);maxLift=Mathf.Max(maxLift,lift);
      poses[i].p[rootIndex]+=bones[rootIndex].parent.InverseTransformVector(Vector3.up*lift);
     }
     report.Add(spec.name+" ground repair: before minimum="+before.ToString("F5")+"; max lift="+maxLift.ToString("F5"));
    }
    var clip=WriteClip(spec,bones,dst,poses);
    // Sample twice as densely as the keys to catch interpolation penetration.
    float min=100,footLead=0;int missing=AnimationUtility.GetCurveBindings(clip).Count(c=>c.path!=""&&dst.transform.Find(c.path)==null);
    if(missing!=0)throw new Exception("Missing paths "+spec.name);
    if(spec.roll){
     clip.SampleAnimation(dst,0);footLead=target["Foot.L"].position.z-target["Foot.R"].position.z;if(footLead<=.05f)throw new Exception("Left foot not leading");
     for(int f=0;f<=Mathf.CeilToInt(clip.length*120);f++){clip.SampleAnimation(dst,Mathf.Min(clip.length,f/120f));min=Mathf.Min(min,MinHeight(dst,skin));}
     if(min<-.002f)throw new Exception("Roll penetrates ground: "+min);
     report.Add("Roll validation: minimum body/armour Y="+min.ToString("F5")+"; initial left-foot lead="+footLead.ToString("F4"));
    }
    if(!spec.roll&&!spec.loop){
     for(int f=0;f<=Mathf.CeilToInt(clip.length*120);f++){clip.SampleAnimation(dst,Mathf.Min(clip.length,f/120f));min=Mathf.Min(min,MinHeight(dst,skin));}
     if(min<0)throw new Exception("Ground repair failed: "+spec.name+" minimum="+min);
     report.Add(spec.name+" verified minimum Y="+min.ToString("F5")+" at 120 FPS");
    }
    if(spec.shield){float left=0,right=0;Quaternion l0=poses[0].q[Array.FindIndex(bones,t=>t.name=="UpperArm.L")],r0=poses[0].q[Array.FindIndex(bones,t=>t.name=="UpperArm.R")];foreach(var p in poses){left=Mathf.Max(left,Quaternion.Angle(l0,p.q[Array.FindIndex(bones,t=>t.name=="UpperArm.L")]));right=Mathf.Max(right,Quaternion.Angle(r0,p.q[Array.FindIndex(bones,t=>t.name=="UpperArm.R")]));}if(left<15||right>.1f)throw new Exception("Shield arm test failed");report.Add("Shield local upper-arm range: left="+left+" degrees; right="+right+" degrees (sword guard retained).");}
    var machine=controller.layers[0].stateMachine;var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==spec.state);if(state==null)state=machine.AddState(spec.state);state.motion=clip;
    if(spec.state!="AttackNew"){state.speed=1;state.speedParameterActive=false;}
    EditorUtility.SetDirty(state);results.Add(clip);report.Add(spec.name+": Blender "+spec.start+".."+last+" -> "+clip.length.ToString("F4")+"s; bindings OK; state="+spec.state);
    Render(dst,skin,clip,spec.name);
   }finally{UnityEngine.Object.DestroyImmediate(src);UnityEngine.Object.DestroyImmediate(dst);}
  }
  EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
  if(!repairsOnly){
  string previewPath=Folder+"PlayerMotionPreview.prefab";var preview=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
  try{preview.name="PlayerMotionPreview";var debug=preview.GetComponent<MeshyAnimationDebugger>();if(debug==null)debug=preview.AddComponent<MeshyAnimationDebugger>();debug.clips=results.ToArray();debug.playOnEnable=true;debug.autoAdvance=true;PrefabUtility.SaveAsPrefabAsset(preview,previewPath);}finally{UnityEngine.Object.DestroyImmediate(preview);}
  }
  File.WriteAllLines("Assets/3DBattle/Authoring~/"+(repairsOnly?"MeshyPlayerGroundSwordValidation":"MeshyPlayerLibraryValidation")+".txt",report);
  return string.Join("\n",report);
 }
 public static string BuildFiveHitCombo(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);var idle=controller.animationClips.First(c=>c.name.EndsWith("Idle_swordRight"));
  string backup="Assets/3DBattle/Authoring~/Backups/FiveHit_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");Directory.CreateDirectory(backup);File.Copy(Controller,backup+"/KnightBattle.controller");
  var weapon=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Animations/MeshyWeaponCombo_Player.anim");
  var sources=new[]{weapon,weapon,weapon,AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"MeshyDoubleComboAttack_Player.anim"),AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"MeshyAttack_Player.anim")};
  int[] starts={0,60,100,0,0},ends={60,100,Mathf.RoundToInt(weapon.length*FPS),Mathf.RoundToInt(sources[3].length*FPS),Mathf.RoundToInt(sources[4].length*FPS)},entry={12,0,0,8,8};
  var clips=new List<AnimationClip>();var gates=new List<int>();var report=new List<string>();
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();idle.SampleAnimation(g,0);var resting=Capture(bones,0);Pose previousEnd=resting;
   var body=bones.First(b=>b.name=="Body");var motionRoot=bones.First(b=>b.name=="Bone");weapon.SampleAnimation(g,0);Vector3 horizontalAnchor=body.position;
   Action<AnimationClip,float> sampleAligned=(source,time)=>{source.SampleAnimation(g,time);Vector3 shift=horizontalAnchor-body.position;shift.y=0;motionRoot.position+=shift;};
   for(int n=0;n<5;n++){
    var spec=new Spec("","MeshyCombo0"+(n+1),0,0);if(File.Exists(ClipPath(spec)))File.Copy(ClipPath(spec),backup+"/"+spec.name+".anim");
    var poses=new List<Pose>();sampleAligned(sources[n],starts[n]/FPS);var beginning=Capture(bones,0);
    for(int f=0;f<entry[n];f++)poses.Add(BlendPose(previousEnd,beginning,f/FPS,Mathf.SmoothStep(0,1,f/(float)entry[n])));
    for(int f=starts[n];f<=ends[n];f++){sampleAligned(sources[n],f/FPS);poses.Add(Capture(bones,(entry[n]+f-starts[n])/FPS));}
    int gate=poses.Count-1;gates.Add(gate);previousEnd=poses[gate];
    for(int f=1;f<=18;f++)poses.Add(BlendPose(previousEnd,resting,(gate+f)/FPS,Mathf.SmoothStep(0,1,f/18f)));
    // Retain authored attack poses; lift blended transitions only if they cross the floor.
    int root=Array.FindIndex(bones,b=>b.name=="Bone");float liftMax=0;
    for(int f=0;f<poses.Count;f++){Apply(bones,poses[f]);float lift=Mathf.Max(0,.012f-MinHeight(g,skin));liftMax=Mathf.Max(liftMax,lift);poses[f].p[root]+=bones[root].parent.InverseTransformVector(Vector3.up*lift);}
    var clip=WriteClip(spec,bones,g,poses);
    // EnsureQuaternionContinuity can alter tangent modes: restore linear interpolation.
    foreach(var binding in AnimationUtility.GetCurveBindings(clip)){var curve=AnimationUtility.GetEditorCurve(clip,binding);for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyBroken(curve,k,true);AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,binding,curve);}
    EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);clips.Add(clip);
    var sm=controller.layers[0].stateMachine;var state=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name==spec.name);if(state==null)state=sm.AddState(spec.name);state.motion=clip;state.speed=1;state.speedParameter="NormalAttackSpeed";state.speedParameterActive=true;EditorUtility.SetDirty(state);
    float minimum=100;for(int f=0;f<=(poses.Count-1)*2;f++){clip.SampleAnimation(g,f/120f);minimum=Mathf.Min(minimum,MinHeight(g,skin));}
    if(minimum<-.002f)throw new Exception(spec.name+" floor penetration "+minimum);
    report.Add(spec.name+": source="+sources[n].name+" "+starts[n]+".."+ends[n]+"; entry="+entry[n]+"; attack ends="+gate+"; recovery=18; final="+(poses.Count-1)+"; minY="+minimum+"; maxLift="+liftMax);
    Render(g,skin,clip,spec.name,new float[]{0,entry[n],(entry[n]+gate)*.5f,gate,gate+9,gate+18});
   }
   float joinError=0,idleError=0,maxPlanarStep=0,maxAttackOffset=0;
   for(int n=0;n<5;n++){
    Vector3 prior=Vector3.zero;for(int f=0;f<=Mathf.RoundToInt(clips[n].length*120);f++){
     float time=f/120f;clips[n].SampleAnimation(g,time);Vector3 position=body.position;position.y=0;
     if(f>0)maxPlanarStep=Mathf.Max(maxPlanarStep,Vector3.Distance(prior,position));prior=position;
     if(time>=entry[n]/FPS&&time<=gates[n]/FPS){var delta=body.position-horizontalAnchor;delta.y=0;maxAttackOffset=Mathf.Max(maxAttackOffset,delta.magnitude);}
    }
    clips[n].SampleAnimation(g,clips[n].length);var end=Capture(bones,0);for(int j=0;j<bones.Length;j++)idleError=Mathf.Max(idleError,Quaternion.Angle(end.q[j],resting.q[j])+Vector3.Distance(end.p[j],resting.p[j]));
    if(n<4){clips[n].SampleAnimation(g,gates[n]/FPS);var a=Capture(bones,0);clips[n+1].SampleAnimation(g,0);var b=Capture(bones,0);for(int j=0;j<bones.Length;j++)joinError=Mathf.Max(joinError,Quaternion.Angle(a.q[j],b.q[j])+Vector3.Distance(a.p[j],b.p[j]));}
   }
   report.Add("Join pose error="+joinError+"; idle recovery pose error="+idleError+"; max horizontal step at 120 FPS="+maxPlanarStep+"m; max attack horizontal offset="+maxAttackOffset+"m. Fifth hit recovers before restarting first.");
   if(joinError>.1f||idleError>.1f||maxPlanarStep>.03f||maxAttackOffset>.005f)throw new Exception(string.Join("\n",report));
   EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
   var preview=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));try{preview.name="FiveHitComboPreview";var debugger=preview.GetComponent<MeshyAnimationDebugger>();if(debugger==null)debugger=preview.AddComponent<MeshyAnimationDebugger>();debugger.clips=clips.ToArray();debugger.playOnEnable=true;debugger.autoAdvance=true;PrefabUtility.SaveAsPrefabAsset(preview,Folder+"FiveHitComboPreview.prefab");}finally{UnityEngine.Object.DestroyImmediate(preview);}
   File.WriteAllLines("Assets/3DBattle/Authoring~/FiveHitComboValidation.txt",report);return string.Join("\n",report);
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 static Pose BlendPose(Pose a,Pose b,float time,float weight){
  var p=new Pose{time=time,p=new Vector3[a.p.Length],q=new Quaternion[a.q.Length]};for(int j=0;j<p.p.Length;j++){p.p[j]=Vector3.Lerp(a.p[j],b.p[j],weight);p.q[j]=Quaternion.Slerp(a.q[j],b.q[j],weight);}return p;
 }
 public static string LimitWeaponWrist(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.source=="Weapon_Combo");var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  string backup="Assets/3DBattle/Authoring~/Backups/WeaponWristLimit_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   var hand=ts["Palm.R"];var thumb=ts["Thumb.L"];int wrist=Array.FindIndex(bones,t=>t==hand),thumbIndex=Array.FindIndex(bones,t=>t==thumb);
   int last=Mathf.RoundToInt(clip.length*FPS);var original=new List<Pose>();for(int f=0;f<=last;f++){clip.SampleAnimation(g,f/FPS);original.Add(Capture(bones,f/FPS));}
   Quaternion start=original[1].q[wrist],raised=original[40].q[wrist],held=original[50].q[wrist];var poses=new List<Pose>();
   for(int f=0;f<=last;f++){
    Apply(bones,original[f]);
    if(f>1&&f<40){Quaternion q=Quaternion.Slerp(start,raised,Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,40,f)));RotateHand(hand,thumb,hand.parent.rotation*q);hand.localRotation=q;}
    else if(f>50){RotateHand(hand,thumb,hand.parent.rotation*held);hand.localRotation=held;}
    poses.Add(Capture(bones,f/FPS));
   }
   clip=WriteClip(spec,bones,g,poses);
   string wristPath=AnimationUtility.CalculateTransformPath(hand,g.transform);
   foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.path==wristPath&&b.propertyName.StartsWith("m_LocalRotation."))){
    var curve=AnimationUtility.GetEditorCurve(clip,binding);
    for(int k=0;k<curve.length;k++)if(curve.keys[k].time>=50f/FPS-.00001f){AnimationUtility.SetKeyBroken(curve,k,true);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Constant);if(curve.keys[k].time>50f/FPS+.00001f)AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
    AnimationUtility.SetEditorCurve(clip,binding,curve);
   }
   EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);
   float total=0,holdError=0,preservedError=0,otherError=0;Quaternion prev=start;
   for(int i=2;i<=last*2;i++){
    float frame=i*.5f;clip.SampleAnimation(g,frame/FPS);
    if(frame<=40){total+=Quaternion.Angle(prev,hand.localRotation);prev=hand.localRotation;}
    if(frame>=50)holdError=Mathf.Max(holdError,Quaternion.Angle(hand.localRotation,held));
    if(i%2==0){int f=i/2;for(int j=0;j<bones.Length;j++){
     float error=Quaternion.Angle(bones[j].localRotation,original[f].q[j])+Vector3.Distance(bones[j].localPosition,original[f].p[j]);
     if(f>=40&&f<=50)preservedError=Mathf.Max(preservedError,error);
     if(j!=wrist&&j!=thumbIndex)otherError=Mathf.Max(otherError,error);
    }}
   }
   var report="MeshyWeaponCombo_Player: frame 1->40 shortest wrist arc; frame 40..50 unchanged; frame 50..150 wrist held at frame 50 local angle.\nAccumulated wrist rotation 1..40="+total+" degrees; held angle error="+holdError+" degrees; preserved 40..50 error="+preservedError+"; other bones error="+otherError+"; length="+clip.length+"s.\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyWeaponWristLimitValidation.txt",report);Render(g,skin,clip,"MeshyWeaponWristLimit",new float[]{1,20,40,50,100,150});
   if(total>=360||holdError>.1f||preservedError>.1f||otherError>.1f)throw new Exception(report);return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 public static string SwapWeaponComboWrists(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.source=="Weapon_Combo");var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  string backup="Assets/3DBattle/Authoring~/Backups/WeaponWristSwap_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   var hand=ts["Palm.R"];var thumb=ts["Thumb.L"];int wrist=Array.FindIndex(bones,t=>t==hand),thumbIndex=Array.FindIndex(bones,t=>t==thumb);
   int last=Mathf.RoundToInt(clip.length*FPS);var original=new List<Pose>();for(int f=0;f<=last;f++){clip.SampleAnimation(g,f/FPS);original.Add(Capture(bones,f/FPS));}
   int[][] ranges={new[]{34,50,74,86},new[]{74,86,34,50},new[]{86,110,124,142},new[]{124,142,86,110}};
   var poses=new List<Pose>();float maxChange=0;int fullSamples=0;
   for(int f=0;f<=last;f++){
    Apply(bones,original[f]);
    foreach(var r in ranges)if(f>r[0]&&f<r[1]){
     float mapped=Mathf.Lerp(r[2],r[3],Mathf.InverseLerp(r[0],r[1],f));int a=Mathf.FloorToInt(mapped),b=Mathf.Min(last,a+1);
     Quaternion target=Quaternion.Slerp(original[a].q[wrist],original[b].q[wrist],mapped-a);
     float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Min(f-r[0],r[1]-f)/3f));
     Quaternion q=Quaternion.Slerp(hand.localRotation,target,blend);maxChange=Mathf.Max(maxChange,Quaternion.Angle(hand.localRotation,q));if(blend>.999f)fullSamples++;
     RotateHand(hand,thumb,hand.parent.rotation*q);break;
    }
    poses.Add(Capture(bones,f/FPS));
   }
   clip=WriteClip(spec,bones,g,poses);float untouchedRotation=0,untouchedPosition=0;
   for(int f=0;f<=last;f++){clip.SampleAnimation(g,f/FPS);for(int j=0;j<bones.Length;j++)if(j!=wrist&&j!=thumbIndex){untouchedRotation=Mathf.Max(untouchedRotation,Quaternion.Angle(bones[j].localRotation,original[f].q[j]));untouchedPosition=Mathf.Max(untouchedPosition,Vector3.Distance(bones[j].localPosition,original[f].p[j]));}}
   var report="MeshyWeaponCombo_Player: wrist local rotations swapped by matching normalized phase. Up 34..50 <-> down 74..86; up 86..110 <-> down 124..142. Three-frame boundary blends.\nFull swap samples="+fullSamples+"; maximum wrist change="+maxChange+" degrees; untouched bones rotation error="+untouchedRotation+"; position error="+untouchedPosition+"; duration="+clip.length+"s.\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyWeaponWristSwapValidation.txt",report);Render(g,skin,clip,"MeshyWeaponWristSwap",new float[]{42,80,96,104,130,137});
   if(fullSamples==0||maxChange<1||untouchedRotation>.1f||untouchedPosition>.0001f)throw new Exception(report);return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 public static string RepairRunSagittalSwing(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.loop);var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  var idle=AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller).animationClips.First(c=>c.name.EndsWith("Idle_swordRight"));
  string backup="Assets/3DBattle/Authoring~/Backups/RunSagittal_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   var upper=ts["UpperArm.R"];var lower=ts["LowerArm.R"];var palm=ts["Palm.R"];var sword=ts["Sword"];
   int last=Mathf.RoundToInt(clip.length*FPS);var originals=new List<Pose>();var angles=new List<float>();
   for(int f=0;f<=last;f++){clip.SampleAnimation(g,f/FPS);originals.Add(Capture(bones,f/FPS));Vector3 d=ts["Torso"].InverseTransformDirection(lower.position-upper.position);angles.Add(Mathf.Atan2(d.z,-d.y)*Mathf.Rad2Deg);}
   float lo=angles.Min(),hi=angles.Max();idle.SampleAnimation(g,0);var rest=Capture(bones,0);Quaternion elbow=lower.localRotation,wrist=palm.localRotation;
   Vector3 neutralDirection=new Vector3(0,Mathf.Cos(35*Mathf.Deg2Rad),Mathf.Sin(35*Mathf.Deg2Rad));
   Quaternion neutralUpper=Quaternion.FromToRotation(sword.forward,neutralDirection)*upper.rotation;
   string[] names={"Shoulder.R","UpperArm.R","LowerArm.R","Palm.R","MiddleHand.R","Fingers.R","Thumb.L","Thumb2.L"};var poses=new List<Pose>();
   for(int f=0;f<=last;f++){
    Apply(bones,originals[f]);foreach(string name in names){int j=Array.FindIndex(bones,t=>t.name==name);bones[j].localPosition=rest.p[j];bones[j].localRotation=rest.q[j];}
    float swing=Mathf.Lerp(-15,15,Mathf.InverseLerp(lo,hi,angles[f]));
    upper.rotation=Quaternion.AngleAxis(swing,Vector3.right)*neutralUpper;poses.Add(Capture(bones,f/FPS));
   }
   clip=WriteClip(spec,bones,g,poses);float lateral=0,elbowError=0,wristError=0,minForward=1;
   for(int f=0;f<=last*2;f++){clip.SampleAnimation(g,f/120f);lateral=Mathf.Max(lateral,Mathf.Abs(sword.forward.x));minForward=Mathf.Min(minForward,sword.forward.z);elbowError=Mathf.Max(elbowError,Quaternion.Angle(lower.localRotation,elbow));wristError=Mathf.Max(wristError,Quaternion.Angle(palm.localRotation,wrist));}
   float loop=0;for(int j=0;j<bones.Length;j++)loop=Mathf.Max(loop,Quaternion.Angle(poses[0].q[j],poses[last].q[j]));
   var report="MeshyRun_Player: right arm swings only in forward/up plane, 30 degree shoulder range. Blade stays 20..50 degrees forward from vertical. Elbow/wrist use idle relative angles.\nMax lateral blade component="+lateral+"; min forward component="+minForward+"; elbow error="+elbowError+" degrees; wrist error="+wristError+" degrees; loop rotation error="+loop+"; length="+clip.length+"s.\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyRunSagittalValidation.txt",report);Render(g,skin,clip,"MeshyRunSagittal");
   if(lateral>.015f||minForward<.3f||elbowError>.1f||wristError>.1f||loop>.1f)throw new Exception(report);return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 public static string RestoreRunShoulder(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.loop);var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  string referencePath=Folder+"RunShoulderReference_"+Guid.NewGuid().ToString("N")+".anim";
  string backup="Assets/3DBattle/Authoring~/Backups/RunShoulder_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  File.Copy("Assets/3DBattle/Authoring~/Backups/RunIdleArm_20261021_224025.anim",referencePath);AssetDatabase.ImportAsset(referencePath);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var reference=AssetDatabase.LoadAssetAtPath<AnimationClip>(referencePath);var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   int last=Mathf.RoundToInt(clip.length*FPS);var poses=new List<Pose>();int shoulder=Array.FindIndex(bones,t=>t.name=="Shoulder.R"),upper=Array.FindIndex(bones,t=>t.name=="UpperArm.R");
   clip.SampleAnimation(g,0);Quaternion elbow=ts["LowerArm.R"].localRotation,wrist=ts["Palm.R"].localRotation;
   for(int f=0;f<=last;f++){
    clip.SampleAnimation(g,f/FPS);var p=Capture(bones,f/FPS);reference.SampleAnimation(g,f/FPS);
    foreach(int j in new[]{shoulder,upper}){p.p[j]=bones[j].localPosition;p.q[j]=bones[j].localRotation;}
    poses.Add(p);
   }
   clip=WriteClip(spec,bones,g,poses);float elbowError=0,wristError=0,upperRange=0,shoulderError=0;
   for(int f=0;f<=last*2;f++){
    float t=f/120f;reference.SampleAnimation(g,t);Quaternion sq=bones[shoulder].localRotation,uq=bones[upper].localRotation;
    clip.SampleAnimation(g,t);shoulderError=Mathf.Max(shoulderError,Mathf.Max(Quaternion.Angle(sq,bones[shoulder].localRotation),Quaternion.Angle(uq,bones[upper].localRotation)));
    upperRange=Mathf.Max(upperRange,Quaternion.Angle(poses[0].q[upper],bones[upper].localRotation));
    elbowError=Mathf.Max(elbowError,Quaternion.Angle(elbow,ts["LowerArm.R"].localRotation));wristError=Mathf.Max(wristError,Quaternion.Angle(wrist,ts["Palm.R"].localRotation));
   }
   var first=poses[0];var end=poses[last];float loop=0;for(int j=0;j<bones.Length;j++)loop=Mathf.Max(loop,Quaternion.Angle(first.q[j],end.q[j]));
   var report="MeshyRun_Player: original right shoulder/upper-arm motion restored. Elbow/wrist local angles remain idle.\nUpper-arm motion range="+upperRange+" degrees; original shoulder/upper error="+shoulderError+"; elbow error="+elbowError+"; wrist error="+wristError+"; loop rotation error="+loop+"; duration="+clip.length+"s.\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyRunShoulderValidation.txt",report);Render(g,skin,clip,"MeshyRunShoulder");
   if(upperRange<1||shoulderError>.1f||elbowError>.1f||wristError>.1f||loop>.1f)throw new Exception(report);return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);AssetDatabase.DeleteAsset(referencePath);}
 }
 public static string RepairRunIdleArm(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.loop);var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  var idle=AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller).animationClips.First(c=>c.name.EndsWith("Idle_swordRight"));
  string backup="Assets/3DBattle/Authoring~/Backups/RunIdleArm_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   var lower=ts["LowerArm.R"];var palm=ts["Palm.R"];var thumb=ts["Thumb.L"];
   idle.SampleAnimation(g,0);Quaternion elbow=lower.localRotation,wrist=palm.localRotation;
   var idleArm=Capture(bones,0);string[] armNames={"Shoulder.R","UpperArm.R","LowerArm.R","Palm.R","MiddleHand.R","Fingers.R","Thumb.L","Thumb2.L"};
   Vector3 thumbOffset=palm.InverseTransformPoint(thumb.position);Quaternion thumbRelative=Quaternion.Inverse(palm.rotation)*thumb.rotation;
   int last=Mathf.RoundToInt(clip.length*FPS);var poses=new List<Pose>();
   for(int f=0;f<=last;f++){
    clip.SampleAnimation(g,f/FPS);
    foreach(string name in armNames){int j=Array.FindIndex(bones,b=>b.name==name);bones[j].localPosition=idleArm.p[j];bones[j].localRotation=idleArm.q[j];}
    thumb.SetPositionAndRotation(palm.TransformPoint(thumbOffset),palm.rotation*thumbRelative);
    poses.Add(Capture(bones,f/FPS));
   }
   clip=WriteClip(spec,bones,g,poses);float elbowError=0,wristError=0;
   for(int f=0;f<=last*2;f++){clip.SampleAnimation(g,f/120f);elbowError=Mathf.Max(elbowError,Quaternion.Angle(lower.localRotation,elbow));wristError=Mathf.Max(wristError,Quaternion.Angle(palm.localRotation,wrist));}
   clip.SampleAnimation(g,0);var first=Capture(bones,0);clip.SampleAnimation(g,clip.length);var end=Capture(bones,clip.length);float loopPosition=0,loopRotation=0;
   for(int i=0;i<bones.Length;i++){loopPosition=Mathf.Max(loopPosition,Vector3.Distance(first.p[i],end.p[i]));loopRotation=Mathf.Max(loopRotation,Quaternion.Angle(first.q[i],end.q[i]));}
   var report="MeshyRun_Player: right arm fixed to "+idle.name+" frame 0; torso, legs and left arm running motion retained.\nElbow error="+elbowError+" degrees; wrist error="+wristError+" degrees; loop position error="+loopPosition+"; loop rotation error="+loopRotation+" degrees; duration="+clip.length+"s.\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyRunIdleArmValidation.txt",report);Render(g,skin,clip,"MeshyRunIdleArm");
   if(elbowError>.1f||wristError>.1f||loopPosition>.0001f||loopRotation>.1f)throw new Exception(report);return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 public static string RepairRunSword(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.loop);var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  string backup="Assets/3DBattle/Authoring~/Backups/RunSword_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   var hand=ts["Palm.R"];var sword=ts["Sword"];clip.SampleAnimation(g,0);
   Quaternion forward=Quaternion.FromToRotation(sword.forward,Vector3.forward)*hand.rotation;
   int last=Mathf.RoundToInt(clip.length*FPS);var poses=new List<Pose>();
   for(int f=0;f<=last;f++){clip.SampleAnimation(g,f/FPS);RotateHand(hand,ts["Thumb.L"],forward);poses.Add(Capture(bones,f/FPS));}
   clip=WriteClip(spec,bones,g,poses);float maxAngle=0;
   for(int f=0;f<=last*2;f++){clip.SampleAnimation(g,f/120f);maxAngle=Mathf.Max(maxAngle,Vector3.Angle(sword.forward,Vector3.forward));}
   clip.SampleAnimation(g,0);var first=Capture(bones,0);clip.SampleAnimation(g,clip.length);var end=Capture(bones,clip.length);float loopPosition=0,loopRotation=0;
   for(int i=0;i<bones.Length;i++){loopPosition=Mathf.Max(loopPosition,Vector3.Distance(first.p[i],end.p[i]));loopRotation=Mathf.Max(loopRotation,Quaternion.Angle(first.q[i],end.q[i]));}
   var report="MeshyRun_Player: frames 0.."+last+" at 60 FPS; length="+clip.length+"s; blade faces forward.\nMaximum blade direction error="+maxAngle+" degrees; loop position error="+loopPosition+"; loop rotation error="+loopRotation+" degrees; loop enabled="+AnimationUtility.GetAnimationClipSettings(clip).loopTime+".\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyRunSwordValidation.txt",report);Render(g,skin,clip,"MeshyRunSword");
   if(maxAngle>1||loopPosition>.0001f||loopRotation>.1f||!AnimationUtility.GetAnimationClipSettings(clip).loopTime)throw new Exception(report);return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 public static string RepairRollSword(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.roll);var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  string backup="Assets/3DBattle/Authoring~/Backups/RollSword_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   var right=ts["Palm.R"];var left=ts["Palm.L"];var sword=ts["Sword"];var mesh=sword.GetComponent<MeshFilter>().sharedMesh;
   Vector3 tipLocal=new Vector3(mesh.bounds.center.x,mesh.bounds.center.y,mesh.bounds.max.z);Vector3 finalDirection=new Vector3(0,1,1).normalized,turnAxis=Vector3.Cross(Vector3.left,new Vector3(0,1,1).normalized).normalized;
   int last=Mathf.RoundToInt(clip.length*FPS);var poses=new List<Pose>();Pose release=null;Quaternion turnStart=Quaternion.identity;float maxReach=0;
   string[] leftNames={"UpperArm.L","LowerArm.L","Palm.L","Thumb.R"};
   for(int f=0;f<=last;f++){
    clip.SampleAnimation(g,f/FPS);var original=Capture(bones,f/FPS);
    if(f>=8&&f<=80){
     Vector3 leftCenter=(left.position+ts["MiddleHand.L"].position)*.5f;
     Vector3 direction=(leftCenter-right.position).normalized;
     direction=Vector3.Slerp(direction,Vector3.left,Mathf.SmoothStep(0,1,Mathf.InverseLerp(70,80,f))).normalized;
     RotateHand(right,ts["Thumb.L"],Quaternion.FromToRotation(sword.forward,direction)*right.rotation);
     Vector3 leftOffset=leftCenter-left.position;
     Vector3 separation=sword.TransformPoint(tipLocal)-right.position-leftOffset;
     var ru=ts["UpperArm.R"];var rl=ts["LowerArm.R"];var lu=ts["UpperArm.L"];var ll=ts["LowerArm.L"];
     float rr=Vector3.Distance(ru.position,rl.position)+Vector3.Distance(rl.position,right.position)-.005f,lr=Vector3.Distance(lu.position,ll.position)+Vector3.Distance(ll.position,left.position)-.005f;
     Vector3 goal=right.position;for(int j=0;j<48;j++){goal=lu.position-separation+Vector3.ClampMagnitude(goal+separation-lu.position,lr);goal=ru.position+Vector3.ClampMagnitude(goal-ru.position,rr);}
     maxReach=Mathf.Max(maxReach,Vector3.Distance(goal+separation,lu.position)-lr);
     MoveArmHand(ru,rl,right,ts["Thumb.L"],goal);MoveArmHand(lu,ll,left,ts["Thumb.R"],goal+separation);
     var corrected=Capture(bones,f/FPS);
     if(f<14){float w=Mathf.SmoothStep(0,1,Mathf.InverseLerp(8,14,f));for(int j=0;j<bones.Length;j++){corrected.p[j]=Vector3.Lerp(original.p[j],corrected.p[j],w);corrected.q[j]=Quaternion.Slerp(original.q[j],corrected.q[j],w);}Apply(bones,corrected);}
     if(f==80){release=Capture(bones,f/FPS);turnStart=right.rotation;}
    }else if(f>80){
     float armRelease=Mathf.SmoothStep(0,1,Mathf.InverseLerp(80,94,f));
     foreach(string name in new[]{"UpperArm.R","LowerArm.R","Palm.R","Thumb.L"}){int j=Array.FindIndex(bones,b=>b.name==name);bones[j].localPosition=Vector3.Lerp(release.p[j],original.p[j],armRelease);bones[j].localRotation=Quaternion.Slerp(release.q[j],original.q[j],armRelease);}
     float w=Mathf.SmoothStep(0,1,Mathf.InverseLerp(80,98,f));RotateHand(right,ts["Thumb.L"],Quaternion.AngleAxis(90*w,turnAxis)*turnStart);
     float releaseWeight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(80,94,f));
     foreach(string name in leftNames){int j=Array.FindIndex(bones,b=>b.name==name);bones[j].localPosition=Vector3.Lerp(release.p[j],original.p[j],releaseWeight);bones[j].localRotation=Quaternion.Slerp(release.q[j],original.q[j],releaseWeight);}
    }
    poses.Add(Capture(bones,f/FPS));
   }
   if(maxReach>.025f)throw new Exception("Arm cannot reach sword tip: "+maxReach);
   // Lift only if the adjusted hands introduce new floor penetration.
   var lifts=new float[poses.Count];for(int i=0;i<poses.Count;i++){Apply(bones,poses[i]);lifts[i]=Mathf.Max(0,.012f-MinHeight(g,skin));}
   int root=Array.FindIndex(bones,b=>b.name=="Bone");float maxLift=0;
   for(int i=0;i<poses.Count;i++){float lift=0;for(int d=-2;d<=2;d++)lift=Mathf.Max(lift,lifts[Mathf.Clamp(i+d,0,poses.Count-1)]);maxLift=Mathf.Max(maxLift,lift);poses[i].p[root]+=bones[root].parent.InverseTransformVector(Vector3.up*lift);}
   clip=WriteClip(spec,bones,g,poses);float maxContact=0,minFloor=100,maxFinalAngle=0;
   for(int i=0;i<=last*2;i++){float t=i/120f;clip.SampleAnimation(g,t);if(t>=14f/FPS&&t<=80f/FPS)maxContact=Mathf.Max(maxContact,Vector3.Distance(sword.TransformPoint(tipLocal),(left.position+ts["MiddleHand.L"].position)*.5f));if(t>=98f/FPS)maxFinalAngle=Mathf.Max(maxFinalAngle,Vector3.Angle(sword.forward,finalDirection));minFloor=Mathf.Min(minFloor,MinHeight(g,skin));}
   var report="MeshyGroundRoll_Player: frames 14..80 left palm touches blade tip; right hand keeps handle. Frames 81..98 rotate upward 90 degrees, then face forward-up through "+last+".\nMax contact error="+maxContact+"m; final direction error="+maxFinalAngle+" degrees; reach excess="+maxReach+"m; min body Y="+minFloor+"m; added floor lift="+maxLift+"m.\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyRollSwordValidation.txt",report);Render(g,skin,clip,"MeshyRollSword",new float[]{14,40,60,80,90,118});
   if(maxContact>.035f||maxFinalAngle>1||minFloor<-.002f)throw new Exception(report);return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 static void MoveArmHand(Transform upper,Transform lower,Transform hand,Transform thumb,Vector3 goal){
  Quaternion q=hand.rotation;Vector3 offset=Quaternion.Inverse(q)*(thumb.position-hand.position);Quaternion tq=Quaternion.Inverse(q)*thumb.rotation;
  SolveArm(upper,lower,hand,goal);hand.SetPositionAndRotation(goal,q);thumb.SetPositionAndRotation(goal+q*offset,q*tq);
 }
 public static string RepairDoubleCombo(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.source=="Double_Combo_Attack");var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  string backup="Assets/3DBattle/Authoring~/Backups/DoubleCombo_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   var hand=ts["Palm.R"];var sword=ts["Sword"];var thumb=ts["Thumb.L"];
   clip.SampleAnimation(g,25f/FPS);var endPose=Capture(bones,25f/FPS);Quaternion endRotation=hand.rotation;Vector3 endDirection=sword.forward;
   Vector3 axis=Vector3.Cross(endDirection,Vector3.up).normalized;
   Quaternion raisedRotation=Quaternion.AngleAxis(180,axis)*endRotation;
   Vector3 raisedDirection=-endDirection;
   int[] keys={0,1,5,10,18};Vector3[] directions={new Vector3(-.85f,-.20f,.5f),new Vector3(-.85f,-.20f,.5f),new Vector3(-.5f,-.85f,.15f),new Vector3(.6f,-.8f,.1f),raisedDirection};
   var rotations=directions.Select(d=>Quaternion.FromToRotation(raisedDirection,d.normalized)*raisedRotation).ToArray();var poses=new List<Pose>();
   for(int f=0;f<=25;f++){
    clip.SampleAnimation(g,f/FPS);
    if(f<25){
     Quaternion rotation;
     if(f>=18)rotation=Quaternion.AngleAxis(180*(1-Mathf.SmoothStep(0,1,(f-18)/7f)),axis)*endRotation;
     else{int k=0;while(k<keys.Length-2&&f>keys[k+1])k++;rotation=Quaternion.Slerp(rotations[k],rotations[k+1],Mathf.SmoothStep(0,1,Mathf.InverseLerp(keys[k],keys[k+1],f)));}
     RotateHand(hand,thumb,rotation);
    }
    poses.Add(Capture(bones,f/FPS));
   }
   clip=WriteClip(spec,bones,g,poses);clip.SampleAnimation(g,clip.length);
   float finalPosition=0,finalRotation=0;for(int i=0;i<bones.Length;i++){finalPosition=Mathf.Max(finalPosition,Vector3.Distance(bones[i].localPosition,endPose.p[i]));finalRotation=Mathf.Max(finalRotation,Quaternion.Angle(bones[i].localRotation,endPose.q[i]));}
   clip.SampleAnimation(g,18f/FPS);float halfTurn=Vector3.Angle(sword.forward,endDirection);float minFloor=100;
   for(int i=0;i<=50;i++){clip.SampleAnimation(g,i/120f);minFloor=Mathf.Min(minFloor,MinHeight(g,skin));}
   var report="MeshyDoubleComboAttack_Player: 0..25 at 60 FPS; length="+clip.length+"s. Rising cut through frame 18; half-turn 18..25.\nBlade half-turn="+halfTurn+" degrees; original frame 25 pose max position error="+finalPosition+"; rotation error="+finalRotation+" degrees; minimum body Y="+minFloor+"m.\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyDoubleComboValidation.txt",report);Render(g,skin,clip,"MeshyDoubleComboCut",new float[]{1,5,10,18,22,25});
   if(Mathf.Abs(clip.length-25f/FPS)>.001f||finalPosition>.0001f||finalRotation>.1f||halfTurn<179||minFloor<0)throw new Exception(report);
   return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 public static string RepairCrouchSword(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.source=="Female_Crouch_Pick_Fr");var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  string backup="Assets/3DBattle/Authoring~/Backups/CrouchSword_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   var hand=ts["Palm.R"];var sword=ts["Sword"];var thumb=ts["Thumb.L"];
   clip.SampleAnimation(g,0);Quaternion front=Quaternion.FromToRotation(sword.forward,Vector3.forward)*hand.rotation;
   clip.SampleAnimation(g,103f/FPS);Quaternion ending=hand.rotation;Vector3 endDirection=sword.forward;
   clip.SampleAnimation(g,59f/FPS);float boundaryAngle=Quaternion.Angle(front,hand.rotation);
   int last=Mathf.RoundToInt(clip.length*FPS);var poses=new List<Pose>();
   for(int f=0;f<=last;f++){
    clip.SampleAnimation(g,f/FPS);
    if(f<=58)RotateHand(hand,thumb,front);
    else if(f<65)RotateHand(hand,thumb,Quaternion.Slerp(front,hand.rotation,Mathf.SmoothStep(0,1,Mathf.InverseLerp(58,65,f))));
    else if(f>=103)RotateHand(hand,thumb,ending);
    poses.Add(Capture(bones,f/FPS));
   }
   clip=WriteClip(spec,bones,g,poses);
   float frontError=0,endError=0,minFloor=100;
   for(int i=0;i<=last*2;i++){
    float t=i/120f;clip.SampleAnimation(g,t);
    if(t<=58f/FPS)frontError=Mathf.Max(frontError,Vector3.Angle(sword.forward,Vector3.forward));
    if(t>=103f/FPS)endError=Mathf.Max(endError,Vector3.Angle(sword.forward,endDirection));
    minFloor=Mathf.Min(minFloor,MinHeight(g,skin));
   }
   var report="MeshyCrouchPick_Player: 60 FPS; frames 0..58 forward, 103.."+last+" hold original frame 103 orientation. Frames 59..64 blend to original motion; 65..102 unchanged.\nFront error="+frontError+" degrees; ending error="+endError+" degrees; minimum body Y="+minFloor+"m; original frame 58->59 hand angle="+boundaryAngle+" degrees.\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyCrouchSwordValidation.txt",report);Render(g,skin,clip,"MeshyCrouchSword");
   if(frontError>1||endError>1||minFloor<0)throw new Exception(report);
   return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 public static string RepairAttackGrip(){
  if(EditorApplication.isPlaying)throw new Exception("Exit Play mode first");
  var spec=Specs().Single(s=>s.source=="Attack");var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(spec));
  string backup="Assets/3DBattle/Authoring~/Backups/AttackGrip_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".anim";File.Copy(ClipPath(spec),backup);
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{
   var skin=g.GetComponentInChildren<SkinnedMeshRenderer>();var bones=g.GetComponentsInChildren<Transform>().Where(t=>skin.bones.Contains(t)).ToArray();var ts=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
   var right=ts["Palm.R"];var left=ts["Palm.L"];var sword=ts["Sword"];
   clip.SampleAnimation(g,4f/FPS);Quaternion rightReference=right.rotation,leftReference=left.rotation;Vector3 bladeReference=new Vector3(0,.20f,1).normalized;
   Quaternion forwardGrip=Quaternion.FromToRotation(sword.forward,bladeReference)*rightReference;
   Quaternion skyGrip=Quaternion.FromToRotation(sword.forward,Vector3.up)*rightReference;
   var poses=new List<Pose>();float duration=clip.length;int count=Mathf.RoundToInt(duration*FPS);float maxStretch=0;
   for(int f=0;f<=count;f++){
    clip.SampleAnimation(g,f/FPS);
    // Keep the blade skyward through frame 18; start the forward cut afterwards.
    RotateHand(right,ts["Thumb.L"],Quaternion.Slerp(skyGrip,forwardGrip,Mathf.SmoothStep(0,1,Mathf.InverseLerp(18,30,f))));
    Quaternion desiredLeft=right.rotation*Quaternion.Inverse(rightReference)*leftReference;
    RotateHand(left,ts["Thumb.R"],desiredLeft);
    Vector3 rightCenter=(right.position+ts["MiddleHand.R"].position)*.5f;
    Vector3 leftOffset=(ts["MiddleHand.L"].position-left.position)*.5f;
    Vector3 goal=rightCenter-sword.forward*.13f-leftOffset;
    // Project the shared grip into both arms' reachable volumes, avoiding stretched arms.
    var ru=ts["UpperArm.R"];var rl=ts["LowerArm.R"];var lu=ts["UpperArm.L"];var ll=ts["LowerArm.L"];
    float rr=Vector3.Distance(ru.position,rl.position)+Vector3.Distance(rl.position,right.position)-.005f;
    float lr=Vector3.Distance(lu.position,ll.position)+Vector3.Distance(ll.position,left.position)-.005f;
    Vector3 separation=goal-right.position,rightGoal=right.position;
    for(int iteration=0;iteration<24;iteration++){
     rightGoal=lu.position-separation+Vector3.ClampMagnitude(rightGoal+separation-lu.position,lr);
     rightGoal=ru.position+Vector3.ClampMagnitude(rightGoal-ru.position,rr);
    }
    Quaternion rq=right.rotation;var rt=ts["Thumb.L"];Vector3 rto=Quaternion.Inverse(rq)*(rt.position-right.position);Quaternion rtq=Quaternion.Inverse(rq)*rt.rotation;
    SolveArm(ru,rl,right,rightGoal);right.SetPositionAndRotation(rightGoal,rq);rt.SetPositionAndRotation(rightGoal+rq*rto,rq*rtq);goal=rightGoal+separation;
    var thumb=ts["Thumb.R"];Vector3 thumbOffset=Quaternion.Inverse(left.rotation)*(thumb.position-left.position);Quaternion thumbRelative=Quaternion.Inverse(left.rotation)*thumb.rotation;
    var upper=ts["UpperArm.L"];var lower=ts["LowerArm.L"];
    float reach=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,left.position);
    maxStretch=Mathf.Max(maxStretch,Vector3.Distance(upper.position,goal)-reach);
    SolveArm(upper,lower,left,goal);left.SetPositionAndRotation(goal,desiredLeft);
    thumb.SetPositionAndRotation(goal+desiredLeft*thumbOffset,desiredLeft*thumbRelative);
    poses.Add(Capture(bones,f/FPS));
   }
   if(maxStretch>.08f)throw new Exception("Left hand beyond reach: "+maxStretch);
   clip=WriteClip(spec,bones,g,poses);
   float minGap=100,maxAngle=0,minFloor=100,maxGripError=0,maxSkyAngle=0;
   for(int i=0;i<=count*2;i++){
    float time=i/120f;clip.SampleAnimation(g,time);
    Vector3 rc=(right.position+ts["MiddleHand.R"].position)*.5f,lc=(left.position+ts["MiddleHand.L"].position)*.5f;
    minGap=Mathf.Min(minGap,Vector3.Dot(rc-lc,sword.forward));maxGripError=Mathf.Max(maxGripError,(rc-lc-sword.forward*.13f).magnitude);
    if(time<=18f/FPS)maxSkyAngle=Mathf.Max(maxSkyAngle,Vector3.Angle(sword.forward,Vector3.up));
    if(time>=30f/FPS)maxAngle=Mathf.Max(maxAngle,Vector3.Angle(sword.forward,bladeReference));
    minFloor=Mathf.Min(minFloor,MinHeight(g,skin));
   }
   var report="MeshyAttack_Player: frames 0.."+count+" at 60 FPS; frames 0..18 skyward; frames 19..30 turn forward, then hold through ending.\nMax sky deviation="+maxSkyAngle+" degrees; max forward deviation="+maxAngle+" degrees; min right-hand lead="+minGap+"m; max grip error="+maxGripError+"m; maximum arm reach excess="+maxStretch+"m; minimum body/armour Y="+minFloor+"m.\nBackup="+backup;
   File.WriteAllText("Assets/3DBattle/Authoring~/MeshyAttackGripValidation.txt",report);Render(g,skin,clip,"MeshyAttackGrip");
   if(maxSkyAngle>1||maxAngle>1||minGap<.10f||maxGripError>.025f||minFloor<0)throw new Exception(report);
   return report;
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 public static string ReviewAttackGrip(){
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));g.hideFlags=HideFlags.HideAndDontSave;g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  try{Render(g,g.GetComponentInChildren<SkinnedMeshRenderer>(),AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"MeshyAttack_Player.anim"),"MeshyAttackGrip");return "Review frames: 0, 9, 18, 24, 30, 126";}finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 static void RotateHand(Transform palm,Transform thumb,Quaternion rotation){
  var delta=rotation*Quaternion.Inverse(palm.rotation);var p=palm.position+delta*(thumb.position-palm.position);var q=delta*thumb.rotation;palm.rotation=rotation;thumb.SetPositionAndRotation(p,q);
 }
 static void SolveArm(Transform upper,Transform lower,Transform end,Vector3 goal){
  Vector3 origin=upper.position;float a=Vector3.Distance(origin,lower.position),b=Vector3.Distance(lower.position,end.position);Vector3 direction=(goal-origin).normalized;
  var pole=Vector3.ProjectOnPlane(lower.position-origin,direction).normalized;if(pole.sqrMagnitude<.1f)pole=Vector3.Cross(direction,Vector3.up).normalized;
  float d=Mathf.Clamp(Vector3.Distance(goal,origin),Mathf.Abs(a-b)+.0001f,a+b-.0001f),along=(a*a-b*b+d*d)/(2*d);
  Vector3 elbow=origin+direction*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
  upper.rotation=Quaternion.FromToRotation(lower.position-origin,elbow-origin)*upper.rotation;
  lower.rotation=Quaternion.FromToRotation(end.position-lower.position,goal-lower.position)*lower.rotation;
 }
 static void SolveLeg(Transform upper,Transform lower,Transform end,Vector3 goal){
  Vector3 hip=upper.position,knee=lower.position,ankle=end.position;float a=Vector3.Distance(hip,knee),b=Vector3.Distance(knee,ankle);var direction=(goal-hip).normalized;float d=Mathf.Clamp(Vector3.Distance(goal,hip),Mathf.Abs(a-b)+.001f,a+b-.001f);
  var pole=Vector3.ProjectOnPlane(Vector3.forward,direction).normalized;if(pole.sqrMagnitude<.1f)pole=Vector3.right;
  float along=(a*a-b*b+d*d)/(2*d);float h=Mathf.Sqrt(Mathf.Max(0,a*a-along*along));Vector3 joint=hip+direction*along+pole*h;
  upper.rotation=Quaternion.FromToRotation(lower.position-hip,joint-hip)*upper.rotation;lower.rotation=Quaternion.FromToRotation(end.position-lower.position,goal-lower.position)*lower.rotation;
 }
 static AnimationClip WriteClip(Spec spec,Transform[] bones,GameObject dst,List<Pose> poses){
  var clip=new AnimationClip{name=spec.name,frameRate=FPS};string[] props={"m_LocalPosition.x","m_LocalPosition.y","m_LocalPosition.z","m_LocalRotation.x","m_LocalRotation.y","m_LocalRotation.z","m_LocalRotation.w"};
  for(int j=0;j<bones.Length;j++){
   var curves=Enumerable.Range(0,7).Select(_=>new AnimationCurve()).ToArray();Quaternion prev=Quaternion.identity;
   for(int i=0;i<poses.Count;i++){var p=poses[i].p[j];var q=poses[i].q[j];if(i>0&&Quaternion.Dot(prev,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);prev=q;float[] v={p.x,p.y,p.z,q.x,q.y,q.z,q.w};for(int k=0;k<7;k++)curves[k].AddKey(poses[i].time,v[k]);}
   for(int k=0;k<7;k++){for(int i=0;i<curves[k].length;i++){AnimationUtility.SetKeyLeftTangentMode(curves[k],i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curves[k],i,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[j],dst.transform),typeof(Transform),props[k]),curves[k]);}
  }
  clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=spec.loop;AnimationUtility.SetAnimationClipSettings(clip,settings);
  var path=ClipPath(spec);var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(existing==null)AssetDatabase.CreateAsset(clip,path);else{EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);clip=existing;EditorUtility.SetDirty(clip);AssetDatabase.SaveAssetIfDirty(clip);}return clip;
 }
 static Vector3[] SkinWorld(SkinnedMeshRenderer r){
  var mesh=r.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;var matrices=r.bones.Select((b,i)=>b.localToWorldMatrix*mesh.bindposes[i]).ToArray();
  for(int i=0;i<vertices.Length;i++){var v=vertices[i];var w=weights[i];vertices[i]=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;}return vertices;
 }
 static float MinHeight(GameObject root,SkinnedMeshRenderer skin){
  float min=SkinWorld(skin).Min(v=>v.y);
  foreach(var f in root.GetComponentsInChildren<MeshFilter>(true)){
   if(f.name=="Sword"||f.sharedMesh==null||!f.gameObject.activeInHierarchy)continue;
   foreach(var v in f.sharedMesh.vertices)min=Mathf.Min(min,f.transform.TransformPoint(v).y);
  }return min;
 }
 static void Render(GameObject g,SkinnedMeshRenderer skin,AnimationClip clip,string name,float[] reviewFrames=null){
  foreach(var t in g.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
  var camGo=new GameObject("Motion review camera");var lightGo=new GameObject("Motion review light");var proxy=new GameObject("Skinned snapshot");proxy.layer=31;
  var mesh=UnityEngine.Object.Instantiate(skin.sharedMesh);proxy.AddComponent<MeshFilter>().sharedMesh=mesh;proxy.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
  var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.layer=31;floor.transform.localScale=Vector3.one*.6f;
  var floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMat.color=new Color(.22f,.24f,.25f);floor.GetComponent<Renderer>().sharedMaterial=floorMat;
  var rt=new RenderTexture(360,420,24);var tile=new Texture2D(360,420,TextureFormat.RGB24,false);var sheet=new Texture2D(1080,840,TextureFormat.RGB24,false);var old=RenderTexture.active;bool was=skin.enabled;skin.enabled=false;
  try{
   var camera=camGo.AddComponent<Camera>();camera.cullingMask=1<<31;camera.orthographic=true;camera.orthographicSize=1.45f;camera.nearClipPlane=.01f;camera.farClipPlane=30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.13f,.14f);camera.targetTexture=rt;camera.transform.position=new Vector3(3,2.3f,5);camera.transform.LookAt(new Vector3(0,.9f,0));
   var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(40,-30,0);
   for(int i=0;i<6;i++){clip.SampleAnimation(g,reviewFrames!=null?reviewFrames[i]/FPS:name=="MeshyAttackGrip"?new float[]{0,9,18,24,30,126}[i]/FPS:clip.length*i/5);mesh.vertices=SkinWorld(skin);mesh.RecalculateNormals();mesh.RecalculateBounds();camera.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,360,420),0,0);tile.Apply();sheet.SetPixels((i%3)*360,(1-i/3)*420,360,420,tile.GetPixels());}
   sheet.Apply();File.WriteAllBytes("Assets/3DBattle/Authoring~/"+name+"_review.png",sheet.EncodeToPNG());
  }finally{skin.enabled=was;RenderTexture.active=old;foreach(var o in new UnityEngine.Object[]{sheet,tile,rt,proxy,mesh,floor,floorMat,camGo,lightGo})UnityEngine.Object.DestroyImmediate(o);}
 }
}
