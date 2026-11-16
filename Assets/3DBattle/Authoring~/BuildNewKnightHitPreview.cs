using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
public static class BuildNewKnightHitPreview
{
 const string Name="New Knight Hit Preview";
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play first");var scene=SceneManager.GetActiveScene();if(scene.name!="3DBattleDebugScene")throw new Exception("Wrong scene");
  if(GameObject.Find(Name)!=null)throw new Exception("Preview already exists");
  var template=GameObject.Find("Running Knight Preview");var root=UnityEngine.Object.Instantiate(template);root.name=Name;Undo.RegisterCreatedObjectUndo(root,"Create hit preview");root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  var a=root.GetComponentInChildren<Animator>();a.enabled=false;a.runtimeAnimatorController.animationClips[0].SampleAnimation(a.gameObject,0);
  var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();var bones=skin.bones.OrderBy(t=>AnimationUtility.CalculateTransformPath(t,a.transform).Split('/').Length).ToArray();
  string path="Assets/3DBattle/NewPlayer/Meshy_AI_KnightCharacter_rigge_hit.fbx";
  var source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));source.hideFlags=HideFlags.HideAndDontSave;source.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);source.transform.localScale=a.transform.localScale;
  try
  {
   var ss=source.GetComponentInChildren<SkinnedMeshRenderer>();var sc=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
   var sb=ss.bones.Select((b,i)=>new{b.name,m=ss.transform.localToWorldMatrix*ss.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.m);
   var db=skin.bones.Select((b,i)=>new{b.name,m=skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[i].inverse}).ToDictionary(x=>x.name,x=>x.m);var sourceBones=ss.bones.ToDictionary(b=>b.name);
   var map=new Dictionary<string,string>{{"Hips","Hips"},{"Chest","Spine02"},{"UpperChest","Spine"},{"Neck","neck"},{"Head","Head"}};
   foreach(string side in new[]{"Left","Right"})foreach(var pair in new[]{new[]{"Shoulder","Shoulder"},new[]{"UpperArm","Arm"},new[]{"LowerArm","ForeArm"},new[]{"Hand","Hand"},new[]{"UpperLeg","UpLeg"},new[]{"LowerLeg","Leg"},new[]{"Foot","Foot"},new[]{"Toes","ToeBase"}})map[side+pair[0]]=side+pair[1];
   float duration=sc.length+.4f;int frames=Mathf.CeilToInt(duration*60);var ps=bones.Select(b=>new List<Vector3>()).ToArray();var qs=bones.Select(b=>new List<Quaternion>()).ToArray();
   var mesh=skin.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;
   float minGround=float.PositiveInfinity;
   for(int f=0;f<=frames;f++)
   {
    float t=duration*f/frames;sc.SampleAnimation(source,Mathf.Min(t,sc.length));
    foreach(var b in bones)b.SetPositionAndRotation(db[b.name].GetColumn(3),db[b.name].rotation);
    foreach(var b in bones){string n=map[b.name];b.rotation=sourceBones[n].rotation*Quaternion.Inverse(sb[n].rotation)*db[b.name].rotation;if(b.name=="Hips"){var p=b.position;p.y+=sourceBones[n].position.y-sb[n].m13;b.position=p;}}
    if(t>sc.length){float w=Mathf.SmoothStep(0,1,(t-sc.length)/.4f);for(int j=0;j<bones.Length;j++){bones[j].localPosition=Vector3.Lerp(bones[j].localPosition,ps[j][0],w);bones[j].localRotation=Quaternion.Slerp(bones[j].localRotation,qs[j][0],w);}}
    var matrices=skin.bones.Select((b,i)=>b.localToWorldMatrix*mesh.bindposes[i]).ToArray();float min=float.PositiveInfinity;
    for(int i=0;i<vertices.Length;i++){var w=weights[i];var p=matrices[w.boneIndex0].MultiplyPoint3x4(vertices[i])*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(vertices[i])*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(vertices[i])*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(vertices[i])*w.weight3;min=Mathf.Min(min,p.y);}
    foreach(var mf in root.GetComponentsInChildren<MeshFilter>().Where(m=>m.name!="Sword"))foreach(var v in mf.sharedMesh.vertices)min=Mathf.Min(min,mf.transform.TransformPoint(v).y);
    float lift=Mathf.Max(0,.015f-min);bones.First(b=>b.name=="Hips").position+=Vector3.up*lift;minGround=Mathf.Min(minGround,min+lift);
    for(int j=0;j<bones.Length;j++){ps[j].Add(bones[j].localPosition);qs[j].Add(bones[j].localRotation);}
   }
   var clip=new AnimationClip{name="NewKnight_Hit_Preview",frameRate=60};
   for(int b=0;b<bones.Length;b++)
   {
    ps[b][frames]=ps[b][0];qs[b][frames]=qs[b][0];for(int i=1;i<=frames;i++)if(Quaternion.Dot(qs[b][i-1],qs[b][i])<0){var q=qs[b][i];qs[b][i]=new Quaternion(-q.x,-q.y,-q.z,-q.w);}
    for(int ch=0;ch<7;ch++){var curve=new AnimationCurve(Enumerable.Range(0,frames+1).Select(i=>new Keyframe(duration*i/frames,ch<3?ps[b][i][ch]:qs[b][i][ch-3])).ToArray());for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[b],a.transform),typeof(Transform),ch<3?"m_LocalPosition."+"xyz"[ch]:"m_LocalRotation."+"xyzw"[ch-3]),curve);}
   }
   clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
   string folder="Assets/3DBattle/NewPlayer/Converted/";AssetDatabase.CreateAsset(clip,folder+clip.name+".anim");var ctrl=AnimatorController.CreateAnimatorControllerAtPath(folder+clip.name+".controller");var state=ctrl.layers[0].stateMachine.AddState("Hit");state.motion=clip;ctrl.layers[0].stateMachine.defaultState=state;
   a.runtimeAnimatorController=ctrl;a.applyRootMotion=false;a.enabled=true;clip.SampleAnimation(a.gameObject,0);
   var neighbour=GameObject.Find("New Knight Roll Preview");root.transform.SetPositionAndRotation(neighbour.transform.position+neighbour.transform.right*2.5f,template.transform.rotation);
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Selection.activeGameObject=root;SceneView.lastActiveSceneView?.FrameSelected();
   return new{name=Name,sourceLength=sc.length,previewLength=clip.length,bones=bones.Length,rightFist=mesh.name.EndsWith("_RightFist"),minGround,missingBindings=AnimationUtility.GetCurveBindings(clip).Count(b=>a.transform.Find(b.path)==null)};
  }
  finally{UnityEngine.Object.DestroyImmediate(source);}
 }
}
