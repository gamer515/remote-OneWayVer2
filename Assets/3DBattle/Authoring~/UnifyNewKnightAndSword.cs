using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class UnifyNewKnightAndSword
{
 const string Folder="Assets/3DBattle/NewPlayer/Converted/";
 public static object Verify()
 {
  var player=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();var reference=UnityEngine.Object.Instantiate(player.gameObject);reference.hideFlags=HideFlags.HideAndDontSave;reference.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  var ra=reference.GetComponent<BattlePlayerController>().animator;var rs=reference.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name=="Sword").transform;
  var reports=new List<object>();string signature=null;
  foreach(string name in new[]{"Running Knight Preview","New Knight Defeat Preview","New Knight Roll Preview"})
  {
   var root=UnityEngine.Object.Instantiate(GameObject.Find(name));root.hideFlags=HideFlags.HideAndDontSave;root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   var a=root.GetComponentInChildren<Animator>();var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();var clip=a.runtimeAnimatorController.animationClips[0];
   string sig=string.Join("|",skin.bones.Select(b=>AnimationUtility.CalculateTransformPath(b,a.transform)).OrderBy(s=>s));if(signature!=null&&sig!=signature)throw new Exception("Rig mismatch");signature=sig;
   var sword=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Sword").transform;
   var rc=ra.runtimeAnimatorController.animationClips.First(c=>c.name==(name.StartsWith("Running")?"MeshyRun_Player":name.Contains("Defeat")?"MeshyCrouchPick_Player":"MeshyGroundRoll_Player"));
   rc.SampleAnimation(ra.gameObject,0);Vector3 grip=rs.InverseTransformPoint(rs.parent.position);float maxAngle=0,maxGrip=0;
   for(int f=0;f<=20;f++)
   {
    float t=(name.Contains("Roll")?clip.length-.5f:clip.length)*f/20;
    float rt=name.StartsWith("Running")?t/clip.length*rc.length:name.Contains("Defeat")?Mathf.Min(t/2,1)*rc.length:Mathf.Clamp01(t/(clip.length-.5f))*(rc.length-.4f)+.4f;
    clip.SampleAnimation(a.gameObject,t);rc.SampleAnimation(ra.gameObject,rt);
    maxAngle=Mathf.Max(maxAngle,Quaternion.Angle(sword.rotation,rs.rotation));maxGrip=Mathf.Max(maxGrip,Vector3.Distance(sword.TransformPoint(grip),sword.parent.position));
   }
   reports.Add(new{name,bones=skin.bones.Length,maxSwordAngleError=maxAngle,maxGripError=maxGrip});UnityEngine.Object.DestroyImmediate(root);
  }
  UnityEngine.Object.DestroyImmediate(reference);return reports;
 }
 static void Write(AnimationClip c,Transform root,Transform t,List<Vector3> ps,List<Quaternion> qs,float duration)
 {
  string path=AnimationUtility.CalculateTransformPath(t,root);
  for(int i=1;i<qs.Count;i++)if(Quaternion.Dot(qs[i-1],qs[i])<0){var q=qs[i];qs[i]=new Quaternion(-q.x,-q.y,-q.z,-q.w);}
  for(int j=0;j<7;j++){var curve=new AnimationCurve(Enumerable.Range(0,ps.Count).Select(i=>new Keyframe(duration*i/(ps.Count-1),j<3?ps[i][j]:qs[i][j-3])).ToArray());for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(c,EditorCurveBinding.FloatCurve(path,typeof(Transform),j<3?"m_LocalPosition."+"xyz"[j]:"m_LocalRotation."+"xyzw"[j-3]),curve);}
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play mode");
  var run=GameObject.Find("Running Knight Preview");var reference=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
  if(run.scene.name!="3DBattleDebugScene")throw new Exception("Wrong scene");
  var runAnimator=run.GetComponentInChildren<Animator>();var source=UnityEngine.Object.Instantiate(runAnimator.gameObject);source.hideFlags=HideFlags.HideAndDontSave;source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);source.transform.localScale=runAnimator.transform.localScale;
  var sourceSkin=source.GetComponentInChildren<SkinnedMeshRenderer>();var sourceClip=runAnimator.runtimeAnimatorController.animationClips[0];
  var pos=run.transform.position;var rot=run.transform.rotation;run.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  var target=UnityEngine.Object.Instantiate(GameObject.Find("New Knight Defeat Preview").GetComponentInChildren<Animator>().gameObject,run.transform);target.name="Knight Model Unified";
  var dst=target.GetComponentInChildren<SkinnedMeshRenderer>();var bones=dst.bones.OrderBy(b=>AnimationUtility.CalculateTransformPath(b,target.transform).Split('/').Length).ToArray();
  var sb=sourceSkin.bones.Select((b,i)=>new{b.name,m=sourceSkin.transform.localToWorldMatrix*sourceSkin.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.m);
  var db=dst.bones.Select((b,i)=>new{b.name,m=dst.transform.localToWorldMatrix*dst.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.m);
  var map=new Dictionary<string,string>{{"Hips","Hips"},{"Chest","Spine02"},{"UpperChest","Spine"},{"Neck","neck"},{"Head","Head"}};
  foreach(string side in new[]{"Left","Right"})foreach(var pair in new[]{new[]{"Shoulder","Shoulder"},new[]{"UpperArm","Arm"},new[]{"LowerArm","ForeArm"},new[]{"Hand","Hand"},new[]{"UpperLeg","UpLeg"},new[]{"LowerLeg","Leg"},new[]{"Foot","Foot"},new[]{"Toes","ToeBase"}})map[side+pair[0]]=side+pair[1];
  var sourceBones=sourceSkin.bones.ToDictionary(b=>b.name);
  // Transfer the latest user equipment offsets through bind space before removing the old rig.
  foreach(string gearName in new[]{"Helmet3","ShoulderPads"})
  {
   var from=runAnimator.GetComponentsInChildren<MeshRenderer>().First(r=>r.name==gearName);var to=target.GetComponentsInChildren<MeshRenderer>().First(r=>r.name==gearName);
   string newBone=gearName=="Helmet3"?"Head":"UpperChest";string oldBone=from.transform.parent.name;
   var oldSkin=runAnimator.GetComponentInChildren<SkinnedMeshRenderer>();int oldIndex=Array.IndexOf(oldSkin.bones,from.transform.parent);
   var oldBind=oldSkin.transform.localToWorldMatrix*oldSkin.sharedMesh.bindposes[oldIndex].inverse;
   var matrix=db[newBone].inverse*oldBind*Matrix4x4.TRS(from.transform.localPosition,from.transform.localRotation,from.transform.localScale);
   to.transform.localPosition=matrix.GetColumn(3);to.transform.localRotation=matrix.rotation;to.transform.localScale=matrix.lossyScale;
  }
  var output=new AnimationClip{name="NewKnight_Run_ChestUpperChest",frameRate=60};int frames=Mathf.CeilToInt(sourceClip.length*60);
  var ps=bones.Select(b=>new List<Vector3>()).ToArray();var qs=bones.Select(b=>new List<Quaternion>()).ToArray();
  for(int f=0;f<=frames;f++)
  {
   sourceClip.SampleAnimation(source,sourceClip.length*f/frames);
   foreach(var b in bones)b.SetPositionAndRotation(db[b.name].GetColumn(3),db[b.name].rotation);
   foreach(var b in bones){string n=map[b.name];b.rotation=sourceBones[n].rotation*Quaternion.Inverse(sb[n].rotation)*db[b.name].rotation;if(b.name=="Hips"){var h=b.position;h.y+=sourceBones[n].position.y-sb[n].m13;b.position=h;}}
   for(int i=0;i<bones.Length;i++){ps[i].Add(bones[i].localPosition);qs[i].Add(bones[i].localRotation);}
  }
  for(int i=0;i<bones.Length;i++){ps[i][frames]=ps[i][0];qs[i][frames]=qs[i][0];Write(output,target.transform,bones[i],ps[i],qs[i],sourceClip.length);}
  output.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(output);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(output,settings);AssetDatabase.CreateAsset(output,Folder+output.name+".anim");
  var controller=AnimatorController.CreateAnimatorControllerAtPath(Folder+output.name+".controller");var state=controller.layers[0].stateMachine.AddState("Run");state.motion=output;controller.layers[0].stateMachine.defaultState=state;
  var animator=target.GetComponent<Animator>();animator.runtimeAnimatorController=controller;animator.avatar=null;animator.applyRootMotion=false;
  UnityEngine.Object.DestroyImmediate(runAnimator.gameObject);UnityEngine.Object.DestroyImmediate(source);run.transform.SetPositionAndRotation(pos,rot);output.SampleAnimation(target,0);
  var referenceClone=UnityEngine.Object.Instantiate(reference.gameObject);referenceClone.hideFlags=HideFlags.HideAndDontSave;referenceClone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  foreach(var c in referenceClone.GetComponentsInChildren<MonoBehaviour>(true))c.enabled=false;
  var refPlayer=referenceClone.GetComponent<BattlePlayerController>();var refAnimator=refPlayer.animator;refAnimator.enabled=false;
  var refSword=referenceClone.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name=="Sword").transform;var refPalm=refSword.parent;
  var report=new List<object>();
  foreach(string name in new[]{"Running Knight Preview","New Knight Defeat Preview","New Knight Roll Preview"})
  {
   var root=GameObject.Find(name);var a=root.GetComponentInChildren<Animator>();var clip=a.runtimeAnimatorController.animationClips[0];var hand=a.GetComponentsInChildren<Transform>().First(t=>t.name=="RightHand");
   string refName=name.StartsWith("Running")?"MeshyRun_Player":name.Contains("Defeat")?"MeshyCrouchPick_Player":"MeshyGroundRoll_Player";
   var refClip=refAnimator.runtimeAnimatorController.animationClips.First(c=>c.name==refName);
   refClip.SampleAnimation(refAnimator.gameObject,0);Vector3 grip=refSword.InverseTransformPoint(refPalm.position);
   var sword=UnityEngine.Object.Instantiate(refSword.gameObject,hand).transform;sword.name="Sword";sword.localScale=Vector3.one*(refSword.lossyScale.x/hand.lossyScale.x);
   int count=Mathf.CeilToInt(clip.length*60);var positions=new List<Vector3>();var rotations=new List<Quaternion>();
   for(int f=0;f<=count;f++)
   {
    float t=clip.length*f/count;clip.SampleAnimation(a.gameObject,t);
    float referenceTime;
    if(name.Contains("Defeat"))referenceTime=Mathf.Min(t/2f,1f)*refClip.length;
    else if(name.Contains("Roll"))referenceTime=Mathf.Clamp01(t/(clip.length-.5f))*(refClip.length-.4f)+.4f;
    else referenceTime=t/clip.length*refClip.length;
    refClip.SampleAnimation(refAnimator.gameObject,referenceTime);
    sword.rotation=root.transform.rotation*refSword.rotation;
    sword.position=hand.position-sword.TransformVector(grip);
    positions.Add(sword.localPosition);rotations.Add(sword.localRotation);
   }
   if(name.StartsWith("Running")){positions[count]=positions[0];rotations[count]=rotations[0];}
   if(name.Contains("Roll")){int begin=Mathf.RoundToInt((clip.length-.5f)/clip.length*count);for(int f=begin;f<=count;f++){float w=Mathf.SmoothStep(0,1,(f-begin)/(float)(count-begin));positions[f]=Vector3.Lerp(positions[f],positions[0],w);rotations[f]=Quaternion.Slerp(rotations[f],rotations[0],w);}}
   Write(clip,a.transform,sword,positions,rotations,clip.length);clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);clip.SampleAnimation(a.gameObject,0);
   report.Add(new{name,reference=refName,rootPath=AnimationUtility.CalculateTransformPath(hand,a.transform),hasChest=a.transform.GetComponentsInChildren<Transform>().Any(t=>t.name=="Chest"),missing=AnimationUtility.GetCurveBindings(clip).Count(b=>a.transform.Find(b.path)==null)});
  }
  UnityEngine.Object.DestroyImmediate(referenceClone);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(run.scene);EditorSceneManager.SaveScene(run.scene);return report;
 }
}
