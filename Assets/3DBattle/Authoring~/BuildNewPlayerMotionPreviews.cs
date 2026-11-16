using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class BuildNewPlayerMotionPreviews
{
 const string Folder="Assets/3DBattle/NewPlayer/Converted";
 public static object FinishFit()
 {
  foreach(string name in new[]{"New Knight Defeat Preview","New Knight Roll Preview"})
  {
   var root=GameObject.Find(name);var a=root.GetComponentInChildren<Animator>();var s=root.GetComponentInChildren<SkinnedMeshRenderer>();var clip=a.runtimeAnimatorController.animationClips[0];
   var hip=s.bones.First(b=>b.name=="Hips");int hi=Array.IndexOf(s.bones,hip);var bind=s.transform.localToWorldMatrix*s.sharedMesh.bindposes[hi].inverse;var p=hip.parent.InverseTransformPoint(bind.GetColumn(3));
   string path=AnimationUtility.CalculateTransformPath(hip,a.transform);
   AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalPosition.x"),AnimationCurve.Constant(0,clip.length,p.x));
   AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalPosition.z"),AnimationCurve.Constant(0,clip.length,p.z));
   clip.SampleAnimation(a.gameObject,0);
   int head=Array.FindIndex(s.bones,b=>b.name=="Head");var mesh=s.sharedMesh;var vs=mesh.vertices;var ws=mesh.boneWeights;var matrices=s.bones.Select((b,i)=>b.localToWorldMatrix*mesh.bindposes[i]).ToArray();Bounds hb=new Bounds();bool first=true;
   for(int i=0;i<vs.Length;i++){var w=ws[i];if(w.boneIndex0!=head||w.weight0<.5f)continue;var v=matrices[w.boneIndex0].MultiplyPoint3x4(vs[i])*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(vs[i])*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(vs[i])*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(vs[i])*w.weight3;if(first){hb=new Bounds(v,Vector3.zero);first=false;}else hb.Encapsulate(v);}
   var helmet=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Helmet3");helmet.transform.position+=hb.center-helmet.bounds.center;
   EditorUtility.SetDirty(clip);
  }
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return "fit saved";
 }
 public static object Native()
 {
  if(Application.isPlaying)throw new Exception("Stop play first");
  var template=UnityEngine.Object.Instantiate(GameObject.Find("Running Knight Preview"));template.hideFlags=HideFlags.HideAndDontSave;template.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  var templateSkin=template.GetComponentInChildren<SkinnedMeshRenderer>();
  var reports=new List<object>();
  for(int kind=0;kind<2;kind++)
  {
   bool roll=kind==1;var root=GameObject.Find(roll?"New Knight Roll Preview":"New Knight Defeat Preview");var pos=root.transform.position;var rot=root.transform.rotation;
   var oldAnimator=root.GetComponentInChildren<Animator>();var controller=oldAnimator.runtimeAnimatorController;var output=controller.animationClips[0];
   UnityEngine.Object.DestroyImmediate(oldAnimator.gameObject);root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   string path="Assets/3DBattle/NewPlayer/Meshy_AI_KnightCharacter_rigge_"+(roll?"Run_Jump_and_Roll":"Female_Crouch_Pick_Fr")+".fbx";
   var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),root.transform);model.name="Knight Model";model.transform.localScale=Vector3.one*(2f/1.4f);
   var animator=model.GetComponent<Animator>();if(animator==null)animator=model.AddComponent<Animator>();animator.enabled=false;
   var skin=model.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=UnityEngine.Object.Instantiate(skin.sharedMesh);var verts=mesh.vertices;var tris=mesh.triangles;var groups=new[]{new List<int>(),new List<int>(),new List<int>()};
   for(int i=0;i<tris.Length;i+=3){var cs=new[]{verts[tris[i]],verts[tris[i+1]],verts[tris[i+2]]};var c=(cs[0]+cs[1]+cs[2])/3;int mat=Mathf.Abs(c.x)>.605f?2:c.y<.19f?1:0;if(mat==0&&cs.Min(v=>Mathf.Abs(v.x))>.58f&&cs.Max(v=>Mathf.Abs(v.x))>.61f)mat=2;if(mat==1&&cs.Max(v=>v.y)>.205f)mat=0;groups[mat].AddRange(new[]{tris[i],tris[i+1],tris[i+2]});}
   mesh.subMeshCount=3;for(int i=0;i<3;i++)mesh.SetTriangles(groups[i],i);AssetDatabase.CreateAsset(mesh,Folder+"/"+(roll?"Roll":"Defeat")+"Body.asset");skin.sharedMesh=mesh;skin.sharedMaterials=templateSkin.sharedMaterials;
   foreach(string gearName in new[]{"Helmet3","ShoulderPads"})
   {
    var gear=template.GetComponentsInChildren<MeshRenderer>().First(r=>r.name==gearName);var oldBone=gear.transform.parent;
    int oldIndex=Array.IndexOf(templateSkin.bones,oldBone);string newName=gearName=="Helmet3"?"Head":"UpperChest";
    var bone=skin.bones.First(b=>b.name==newName);int index=Array.IndexOf(skin.bones,bone);
    var oldBind=templateSkin.transform.localToWorldMatrix*templateSkin.sharedMesh.bindposes[oldIndex].inverse;
    var newBind=skin.transform.localToWorldMatrix*mesh.bindposes[index].inverse;
    var offset=newBind.inverse*oldBind*Matrix4x4.TRS(gear.transform.localPosition,gear.transform.localRotation,gear.transform.localScale);
    var copy=UnityEngine.Object.Instantiate(gear.gameObject,bone);copy.name=gearName;copy.transform.localPosition=offset.GetColumn(3);copy.transform.localRotation=offset.rotation;copy.transform.localScale=offset.lossyScale;
   }
   var sourceClip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
   var bones=model.GetComponentsInChildren<Transform>().Where(t=>t!=model.transform&&!t.GetComponent<Renderer>()&&t.name!="Helmet3"&&t.name!="ShoulderPads").ToArray();
   float begin=roll?56/30f:0;float motionDuration=(roll?sourceClip.length:2f)-begin;float duration=motionDuration+(roll?.5f:1f);int frames=Mathf.CeilToInt(duration*60);var poses=new List<Pose>();
   var hip=skin.bones.First(b=>b.name=="Hips");sourceClip.SampleAnimation(model,begin);Vector3 first=hip.position;
   for(int f=0;f<=frames;f++)
   {
    float t=duration*f/frames;sourceClip.SampleAnimation(model,begin+Mathf.Min(t,motionDuration));var h=hip.position;h.x=first.x;h.z=first.z;hip.position=h;
    if(roll&&t>motionDuration){float w=Mathf.SmoothStep(0,1,(t-motionDuration)/.5f);for(int j=0;j<bones.Length;j++){bones[j].localPosition=Vector3.Lerp(bones[j].localPosition,poses[0].p[j],w);bones[j].localRotation=Quaternion.Slerp(bones[j].localRotation,poses[0].q[j],w);}}
    hip.position+=Vector3.up*Mathf.Max(0,.015f-MinY(model,skin));poses.Add(Capture(bones));
   }
   output.ClearCurves();
   for(int b=0;b<bones.Length;b++)for(int ch=0;ch<7;ch++)
   {
    var keys=new Keyframe[poses.Count];var prev=poses[0].q[b];for(int f=0;f<poses.Count;f++){var q=poses[f].q[b];if(Quaternion.Dot(prev,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);poses[f].q[b]=q;prev=q;keys[f]=new Keyframe(duration*f/frames,ch<3?poses[f].p[b][ch]:q[ch-3]);}
    var c=new AnimationCurve(keys);for(int k=0;k<keys.Length;k++){AnimationUtility.SetKeyLeftTangentMode(c,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(c,k,AnimationUtility.TangentMode.Linear);}
    AnimationUtility.SetEditorCurve(output,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[b],model.transform),typeof(Transform),ch<3?"m_LocalPosition."+"xyz"[ch]:"m_LocalRotation."+"xyzw"[ch-3]),c);
   }
   output.EnsureQuaternionContinuity();EditorUtility.SetDirty(output);animator.avatar=null;animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=true;output.SampleAnimation(model,0);
   root.transform.SetPositionAndRotation(pos,rot);reports.Add(new{name=root.name,bindings=AnimationUtility.GetCurveBindings(output).Length});
  }
  UnityEngine.Object.DestroyImmediate(template);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return reports;
 }
 public static object Preview()
 {
  var atlas=new Texture2D(1200,800,TextureFormat.RGB24,false);var rt=new RenderTexture(400,400,24);
  var go=new GameObject("Temporary Motion Preview Camera");var camera=go.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.2f,.22f,.25f);camera.fieldOfView=36;camera.targetTexture=rt;
  var old=RenderTexture.active;float min=float.PositiveInfinity;
  for(int row=0;row<2;row++)
  {
   var root=UnityEngine.Object.Instantiate(GameObject.Find(row==0?"New Knight Defeat Preview":"New Knight Roll Preview"));root.transform.SetPositionAndRotation(new Vector3(1000,0,1000),Quaternion.identity);
   var a=root.GetComponentInChildren<Animator>();a.enabled=false;var clip=a.runtimeAnimatorController.animationClips[0];
   for(int col=0;col<3;col++)
   {
    float t=row==0?col:col*.65f;clip.SampleAnimation(a.gameObject,t);
    min=Mathf.Min(min,MinY(a.gameObject,root.GetComponentInChildren<SkinnedMeshRenderer>()));
    camera.transform.position=root.transform.position+new Vector3(2.5f,1.9f,4.5f);camera.transform.LookAt(root.transform.position+Vector3.up*.85f);
    camera.Render();RenderTexture.active=rt;atlas.ReadPixels(new Rect(0,0,400,400),col*400,(1-row)*400);
   }
   UnityEngine.Object.DestroyImmediate(root);
  }
  atlas.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/NewKnightMotionPreviews.png",atlas.EncodeToPNG());RenderTexture.active=old;camera.targetTexture=null;
  UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(atlas);return new{minimumY=min};
 }
 class Pose {public Vector3[] p;public Quaternion[] q;}
 static Pose Capture(Transform[] ts)=>new Pose{p=ts.Select(t=>t.localPosition).ToArray(),q=ts.Select(t=>t.localRotation).ToArray()};
 static void Apply(Transform[] ts,Pose p){for(int i=0;i<ts.Length;i++){ts[i].localPosition=p.p[i];ts[i].localRotation=p.q[i];}}
 static float MinY(GameObject model,SkinnedMeshRenderer skin)
 {
  var m=skin.sharedMesh;var vs=m.vertices;var ws=m.boneWeights;var mats=skin.bones.Select((b,i)=>b.localToWorldMatrix*m.bindposes[i]).ToArray();float min=float.PositiveInfinity;
  for(int i=0;i<vs.Length;i++){var w=ws[i];var p=mats[w.boneIndex0].MultiplyPoint3x4(vs[i])*w.weight0+mats[w.boneIndex1].MultiplyPoint3x4(vs[i])*w.weight1+mats[w.boneIndex2].MultiplyPoint3x4(vs[i])*w.weight2+mats[w.boneIndex3].MultiplyPoint3x4(vs[i])*w.weight3;min=Mathf.Min(min,p.y);}
  foreach(var f in model.GetComponentsInChildren<MeshFilter>())foreach(var v in f.sharedMesh.vertices)min=Mathf.Min(min,f.transform.TransformPoint(v).y);
  return min;
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play first");var scene=SceneManager.GetActiveScene();if(scene.name!="3DBattleDebugScene")throw new Exception("Wrong scene");
  var template=GameObject.Find("Running Knight Preview");if(template==null)throw new Exception("Missing equipped running model");
  if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/3DBattle/NewPlayer","Converted");
  var reports=new List<object>();
  for(int kind=0;kind<2;kind++)
  {
   bool roll=kind==1;string name=roll?"New Knight Roll Preview":"New Knight Defeat Preview";
   if(GameObject.Find(name)!=null)throw new Exception("Preview already exists: "+name);
   var root=UnityEngine.Object.Instantiate(template);root.name=name;Undo.RegisterCreatedObjectUndo(root,"Create motion preview");
   root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   var animator=root.GetComponentInChildren<Animator>();var model=animator.gameObject;animator.enabled=false;
   var skin=model.GetComponentInChildren<SkinnedMeshRenderer>();var bones=skin.bones.OrderBy(t=>AnimationUtility.CalculateTransformPath(t,model.transform).Split('/').Length).ToArray();
   var idle=Capture(bones);
   string sourcePath="Assets/3DBattle/NewPlayer/Meshy_AI_KnightCharacter_rigge_"+(roll?"Run_Jump_and_Roll":"Female_Crouch_Pick_Fr")+".fbx";
   var src=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));src.hideFlags=HideFlags.HideAndDontSave;
   var sourceSkin=src.GetComponentInChildren<SkinnedMeshRenderer>();var sourceClip=AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
   var srcBones=sourceSkin.bones.ToDictionary(b=>b.name);
   var srcBind=sourceSkin.bones.Select((b,i)=>new{b.name,m=sourceSkin.transform.localToWorldMatrix*sourceSkin.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.m);
   var dstBind=skin.bones.Select((b,i)=>new{b.name,m=skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.m);
   var map=new Dictionary<string,string>{{"Hips","Hips"},{"Spine02","Chest"},{"Spine","UpperChest"},{"neck","Neck"},{"Head","Head"}};
   foreach(string side in new[]{"Left","Right"})foreach(var pair in new[]{new[]{"Shoulder","Shoulder"},new[]{"Arm","UpperArm"},new[]{"ForeArm","LowerArm"},new[]{"Hand","Hand"},new[]{"UpLeg","UpperLeg"},new[]{"Leg","LowerLeg"},new[]{"Foot","Foot"},new[]{"ToeBase","Toes"}})map[side+pair[0]]=side+pair[1];
   float ratio=Vector3.Distance(dstBind["Head"].GetColumn(3),dstBind["Hips"].GetColumn(3))/Vector3.Distance(srcBind["Head"].GetColumn(3),srcBind["Hips"].GetColumn(3));
   Quaternion align=Quaternion.FromToRotation(srcBind["RightUpperArm"].GetColumn(3)-srcBind["LeftUpperArm"].GetColumn(3),dstBind["RightArm"].GetColumn(3)-dstBind["LeftArm"].GetColumn(3));
   float begin=roll?56/30f:0;float end=roll?sourceClip.length:2f;float motionDuration=end-begin;float duration=motionDuration+(roll?.5f:1f);
   int frames=Mathf.CeilToInt(duration*60);var poses=new List<Pose>();
   sourceClip.SampleAnimation(src,begin);var firstHip=srcBones["Hips"].position;
   try
   {
    for(int f=0;f<=frames;f++)
    {
     float time=duration*f/frames;Apply(bones,idle);sourceClip.SampleAnimation(src,begin+Mathf.Min(time,motionDuration));
     var travel=srcBones["Hips"].position-firstHip;travel.y=0;
     foreach(var b in bones)if(map.TryGetValue(b.name,out string sourceName))
     {
      var sb=srcBind[sourceName];var db=dstBind[b.name];
      b.SetPositionAndRotation((Vector3)db.GetColumn(3)+align*(srcBones[sourceName].position-(Vector3)sb.GetColumn(3)-travel)*ratio,align*srcBones[sourceName].rotation*Quaternion.Inverse(sb.rotation)*Quaternion.Inverse(align)*db.rotation);
     }
     if(roll&&time>motionDuration)
     {
      // Return to the first crouched pose for a repeatable inspection cycle.
      float blend=Mathf.SmoothStep(0,1,(time-motionDuration)/.5f);
      for(int j=0;j<bones.Length;j++){bones[j].localPosition=Vector3.Lerp(bones[j].localPosition,poses[0].p[j],blend);bones[j].localRotation=Quaternion.Slerp(bones[j].localRotation,poses[0].q[j],blend);}
     }
     float ground=MinY(model,skin);skin.bones.First(b=>b.name=="Hips").position+=Vector3.up*Mathf.Max(0,.015f-ground);
     poses.Add(Capture(bones));
    }
    var clip=new AnimationClip{name=roll?"NewKnight_GroundRoll_Preview":"NewKnight_Defeat_Preview",frameRate=60};
    for(int b=0;b<bones.Length;b++)
    {
     string path=AnimationUtility.CalculateTransformPath(bones[b],model.transform);
     for(int channel=0;channel<7;channel++)
     {
      var keys=new Keyframe[poses.Count];Quaternion prev=poses[0].q[b];
      for(int f=0;f<poses.Count;f++){var q=poses[f].q[b];if(Quaternion.Dot(prev,q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);poses[f].q[b]=q;prev=q;keys[f]=new Keyframe(duration*f/frames,channel<3?poses[f].p[b][channel]:q[channel-3]);}
      var curve=new AnimationCurve(keys);for(int k=0;k<keys.Length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
      AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),channel<3?"m_LocalPosition."+"xyz"[channel]:"m_LocalRotation."+"xyzw"[channel-3]),curve);
     }
    }
    clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=roll;AnimationUtility.SetAnimationClipSettings(clip,settings);
    AssetDatabase.CreateAsset(clip,Folder+"/"+clip.name+".anim");
    var controller=AnimatorController.CreateAnimatorControllerAtPath(Folder+"/"+clip.name+".controller");var state=controller.layers[0].stateMachine.AddState(roll?"Roll":"Defeat");state.motion=clip;controller.layers[0].stateMachine.defaultState=state;
    animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.enabled=true;
    clip.SampleAnimation(model,0);
    root.transform.SetPositionAndRotation(template.transform.position+template.transform.right*(kind+1)*2.5f,template.transform.rotation);
    reports.Add(new{name,clip=clip.name,duration=clip.length,loop=roll,mappedBones=map.Count,missingBindings=AnimationUtility.GetCurveBindings(clip).Count(b=>model.transform.Find(b.path)==null)});
   }
   finally{UnityEngine.Object.DestroyImmediate(src);}
  }
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);return reports;
 }
}
