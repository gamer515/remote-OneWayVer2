using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;
public static class BuildNewKnightParryPreview
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play mode first");var scene=SceneManager.GetActiveScene();if(scene.name!="3DBattleDebugScene")throw new Exception("Wrong scene");
  const string name="New Knight Parry Preview";if(GameObject.Find(name)!=null)throw new Exception("Preview already exists");
  var template=GameObject.Find("Running Knight Preview");var root=UnityEngine.Object.Instantiate(template);root.name=name;Undo.RegisterCreatedObjectUndo(root,"Create shield parry preview");root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
  var a=root.GetComponentInChildren<Animator>();a.enabled=false;a.runtimeAnimatorController.animationClips[0].SampleAnimation(a.gameObject,0);
  string path="Assets/3DBattle/NewPlayer/Meshy_AI_KnightCharacter_rigge_Sword_Parry_Backward_.fbx";
  var source=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
  var clip=new AnimationClip{name="NewKnight_ShieldParry_Preview",frameRate=60};
  float hold=.35f,blend=.25f,duration=source.length+hold+blend;int frames=Mathf.CeilToInt(duration*60);
  foreach(var binding in AnimationUtility.GetCurveBindings(source))
  {
   if(a.transform.Find(binding.path)==null)throw new Exception("Missing animation path: "+binding.path);
   var curve=AnimationUtility.GetEditorCurve(source,binding);float first=curve.Evaluate(0),last=curve.Evaluate(source.length);
   var keys=new List<Keyframe>();for(int f=0;f<=frames;f++){float t=duration*f/frames;float value=t<=source.length?curve.Evaluate(t):t<=source.length+hold?last:Mathf.Lerp(last,first,Mathf.SmoothStep(0,1,(t-source.length-hold)/blend));keys.Add(new Keyframe(t,value));}
   var output=new AnimationCurve(keys.ToArray());for(int k=0;k<keys.Count;k++){AnimationUtility.SetKeyLeftTangentMode(output,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(output,k,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,binding,output);
  }
  clip.EnsureQuaternionContinuity();var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
  const string folder="Assets/3DBattle/NewPlayer/Converted/";AssetDatabase.CreateAsset(clip,folder+clip.name+".anim");var controller=AnimatorController.CreateAnimatorControllerAtPath(folder+clip.name+".controller");var state=controller.layers[0].stateMachine.AddState("Parry");state.motion=clip;controller.layers[0].stateMachine.defaultState=state;
  a.runtimeAnimatorController=controller;a.applyRootMotion=false;a.cullingMode=AnimatorCullingMode.AlwaysAnimate;clip.SampleAnimation(a.gameObject,0);
  var hand=a.GetComponentsInChildren<Transform>().First(t=>t.name=="LeftHand");
  var player=UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();var original=player.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name=="Shield4");
  Vector3 size=original.transform.lossyScale;
  var shield=UnityEngine.Object.Instantiate(original.gameObject,hand);shield.name="Shield4";shield.SetActive(true);
  shield.transform.localScale=new Vector3(size.x/hand.lossyScale.x,size.y/hand.lossyScale.y,size.z/hand.lossyScale.z);
  // Preserve the gameplay shield's bind-space mounting orientation across the two rigs.
  var oldSkin=player.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="Knight");var skin=a.GetComponentInChildren<SkinnedMeshRenderer>();
  int oldIndex=Array.IndexOf(oldSkin.bones,original.transform.parent),newIndex=Array.IndexOf(skin.bones,hand);
  var oldBind=oldSkin.transform.localToWorldMatrix*oldSkin.sharedMesh.bindposes[oldIndex].inverse;
  var newBind=skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[newIndex].inverse;
  Quaternion oldRest=Quaternion.Inverse(player.transform.rotation)*oldBind.rotation*original.transform.localRotation;
  shield.transform.localRotation=Quaternion.Inverse(newBind.rotation)*oldRest;
  shield.transform.localPosition=Vector3.zero;
  var renderer=shield.GetComponent<MeshRenderer>();
  Vector3 referenceOffset=Quaternion.Inverse(player.transform.rotation)*(original.bounds.center-original.transform.parent.position);
  shield.transform.position+=hand.position+referenceOffset-renderer.bounds.center;
  a.enabled=true;
  var previous=GameObject.Find("New Knight Hit Preview");root.transform.SetPositionAndRotation(previous.transform.position+previous.transform.right*2.5f,template.transform.rotation);
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Selection.activeGameObject=root;SceneView.lastActiveSceneView?.FrameSelected();
  float scaleError=Vector3.Distance(shield.transform.lossyScale,original.transform.lossyScale);
  return new{name,clip=clip.name,sourceSeconds=source.length,previewSeconds=duration,parent=shield.transform.parent.name,worldScaleError=scaleError,rightFist=skin.sharedMesh.name.EndsWith("_RightFist")};
 }
 public static object Preview()
 {
  var original=GameObject.Find("New Knight Parry Preview");var root=UnityEngine.Object.Instantiate(original);root.transform.SetPositionAndRotation(new Vector3(1000,0,1000),Quaternion.identity);
  var a=root.GetComponentInChildren<Animator>();a.enabled=false;var c=a.runtimeAnimatorController.animationClips[0];
  var go=new GameObject("Temporary Parry Preview Camera");var cam=go.AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.2f,.22f,.25f);cam.fieldOfView=36;
  var rt=new RenderTexture(480,600,24);cam.targetTexture=rt;var tex=new Texture2D(1440,600,TextureFormat.RGB24,false);var old=RenderTexture.active;
  for(int i=0;i<3;i++){c.SampleAnimation(a.gameObject,i*.25f);cam.transform.position=root.transform.position+new Vector3(-2.5f,1.8f,4.5f);cam.transform.LookAt(root.transform.position+Vector3.up);cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,480,600),i*480,0);}
  tex.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/NewKnightParryPreview.png",tex.EncodeToPNG());RenderTexture.active=old;cam.targetTexture=null;
  UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);return "saved";
 }
}
