using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;
public static class BuildRunningKnight
{
 const string Folder="Assets/3DBattle/3DBattleDebug/RunningKnight";
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play first");
  var scene=SceneManager.GetActiveScene();if(scene.name!="3DBattleDebugScene")throw new Exception("Wrong scene");
  if(GameObject.Find("Running Knight Preview")!=null)throw new Exception("Preview already exists");
  if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/3DBattle/3DBattleDebug","RunningKnight");
  var player=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
  var originalBody=player.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r=>r.name=="Knight");
  string path="Assets/3DBattle/Meshy_AI_KnightCharacter_rigge_Running.fbx";
  var root=new GameObject("Running Knight Preview");Undo.RegisterCreatedObjectUndo(root,"Create running knight");
  var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),root.transform);
  PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
  model.name="Knight Model";
  var skin=model.GetComponentInChildren<SkinnedMeshRenderer>();
  var mesh=UnityEngine.Object.Instantiate(skin.sharedMesh);mesh.name="RunningKnight_ArmorBootsSkin";
  var v=mesh.vertices;var weights=mesh.boneWeights;
  var groups=new[]{new List<int>(),new List<int>(),new List<int>()};
  Func<int,int> classify=index=>
  {
   var w=weights[index];int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] ws={w.weight0,w.weight1,w.weight2,w.weight3};
   float hand=0,foot=0,leg=0;
   for(int j=0;j<4;j++){var name=skin.bones[ids[j]].name;if(name.Contains("Hand"))hand+=ws[j];if(name.Contains("Foot")||name.Contains("Toe"))foot+=ws[j];if(name=="LeftLeg"||name=="RightLeg")leg+=ws[j];}
   if(hand>.45f)return 2;
   if(foot>.35f||(leg>.5f&&v[index].y<mesh.bounds.min.y+mesh.bounds.size.y*.20f))return 1;
   return 0;
  };
  int[] triangles=mesh.triangles;
  for(int i=0;i<triangles.Length;i+=3){int a=classify(triangles[i]),b=classify(triangles[i+1]),c=classify(triangles[i+2]);int group=a==b||a==c?a:b==c?b:0;groups[group].AddRange(new[]{triangles[i],triangles[i+1],triangles[i+2]});}
  mesh.subMeshCount=3;for(int i=0;i<3;i++)mesh.SetTriangles(groups[i],i);
  AssetDatabase.CreateAsset(mesh,Folder+"/RunningKnightBody.asset");skin.sharedMesh=mesh;
  skin.sharedMaterials=new[]{originalBody.sharedMaterials.First(m=>m.name=="KnightArmor"),originalBody.sharedMaterials.First(m=>m.name=="Boots"),originalBody.sharedMaterials.First(m=>m.name=="Skin")};
  float scale=2f/1.4f;model.transform.localScale=Vector3.one*scale;
  var baked=new Mesh();skin.BakeMesh(baked);var posed=baked.vertices.Select(x=>skin.transform.TransformPoint(x)).ToArray();
  var head=skin.bones.First(b=>b.name=="Head");
  var headIndices=Enumerable.Range(0,weights.Length).Where(i=>skin.bones[weights[i].boneIndex0].name=="Head").ToArray();
  var hb=new Bounds(posed[headIndices[0]],Vector3.zero);foreach(int i in headIndices)hb.Encapsulate(posed[i]);
  var oldHelmet=player.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name=="Helmet3");
  var oldPads=player.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name=="ShoulderPads");
  Action<MeshRenderer,Transform,Vector3,Vector3,string> equip=(source,parent,center,size,name)=>
  {
   var obj=UnityEngine.Object.Instantiate(source.gameObject);obj.name=name;
   obj.transform.SetParent(root.transform,false);
   obj.transform.rotation=Quaternion.Euler(-90,0,0);obj.transform.localScale=Vector3.one;
   var r=obj.GetComponent<MeshRenderer>();var measured=r.bounds.size;
   obj.transform.localScale=new Vector3(size.x/measured.x,size.z/measured.z,size.y/measured.y);
   obj.transform.position+=center-r.bounds.center;
   obj.transform.SetParent(parent,true);
  };
  equip(oldHelmet,head,hb.center+Vector3.up*.01f,new Vector3(hb.size.x*1.14f,hb.size.y*1.08f,hb.size.z*1.16f),"Helmet3");
  var left=skin.bones.First(b=>b.name=="LeftArm");var right=skin.bones.First(b=>b.name=="RightArm");
  var chest=skin.bones.First(b=>b.name=="Spine");
  float width=Vector3.Distance(left.position,right.position)*1.55f;
  equip(oldPads,chest,(left.position+right.position)*.5f+Vector3.up*.025f,new Vector3(width,.18f,.36f),"ShoulderPads");
  UnityEngine.Object.DestroyImmediate(baked);
  var sourceClip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
  var clip=UnityEngine.Object.Instantiate(sourceClip);clip.name="RunningKnight_Run_Loop";
  var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
  AssetDatabase.CreateAsset(clip,Folder+"/RunningKnight_Run_Loop.anim");
  var controller=AnimatorController.CreateAnimatorControllerAtPath(Folder+"/RunningKnight.controller");var state=controller.layers[0].stateMachine.AddState("Run");state.motion=clip;controller.layers[0].stateMachine.defaultState=state;
  var animator=model.GetComponent<Animator>();if(animator==null)animator=model.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
  root.transform.position=player.transform.position+player.transform.right*3+player.transform.forward*2;
  root.transform.rotation=player.transform.rotation;
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  Selection.activeGameObject=root;SceneView.lastActiveSceneView?.FrameSelected();
  return new{name=root.name,triangles=groups.Select(g=>g.Count/3).ToArray(),clip=clip.name,loop=settings.loopTime,position=root.transform.position.ToString()};
 }
 public static object Preview()
 {
  var root=GameObject.Find("Running Knight Preview");var animator=root.GetComponentInChildren<Animator>();
  var clip=animator.runtimeAnimatorController.animationClips.First();clip.SampleAnimation(animator.gameObject,.15f);
  var bounds=root.GetComponentsInChildren<Renderer>().First().bounds;foreach(var r in root.GetComponentsInChildren<Renderer>())bounds.Encapsulate(r.bounds);
  var go=new GameObject("Temporary Preview Camera");var cam=go.AddComponent<Camera>();
  cam.transform.position=bounds.center+root.transform.TransformDirection(new Vector3(2.8f,1.1f,4.5f));cam.transform.LookAt(bounds.center);
  cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.19f,.21f,.24f);cam.fieldOfView=32;
  var rt=new RenderTexture(768,768,24);cam.targetTexture=rt;cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;
  var tex=new Texture2D(768,768,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,768,768),0,0);tex.Apply();
  System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/RunningKnightFitted.png",tex.EncodeToPNG());RenderTexture.active=old;cam.targetTexture=null;
  UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);
  return "saved";
 }
 public static object FitHelmet()
 {
  var root=GameObject.Find("Running Knight Preview");var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();
  var mesh=skin.sharedMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;var bind=mesh.bindposes;
  var matrices=skin.bones.Select((b,i)=>b.localToWorldMatrix*bind[i]).ToArray();
  int headIndex=Array.FindIndex(skin.bones,b=>b.name=="Head");
  Bounds bounds=new Bounds();bool initialized=false;
  for(int i=0;i<vertices.Length;i++)
  {
   var w=weights[i];if(w.boneIndex0!=headIndex||w.weight0<.5f)continue;
   Vector3 point=matrices[w.boneIndex0].MultiplyPoint3x4(vertices[i])*w.weight0+matrices[w.boneIndex1].MultiplyPoint3x4(vertices[i])*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(vertices[i])*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(vertices[i])*w.weight3;
   if(!initialized){bounds=new Bounds(point,Vector3.zero);initialized=true;}else bounds.Encapsulate(point);
  }
  var helmet=root.GetComponentsInChildren<MeshRenderer>().First(r=>r.name=="Helmet3");
  float factor=bounds.size.y*1.12f/helmet.bounds.size.y;
  helmet.transform.localScale*=factor;
  helmet.transform.position+=bounds.center+Vector3.up*.015f-helmet.bounds.center;
  EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
  return new{headSize=bounds.size.ToString(),helmetSize=helmet.bounds.size.ToString()};
 }
}
