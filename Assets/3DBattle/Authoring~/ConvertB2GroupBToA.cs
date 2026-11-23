using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public static class ConvertB2GroupBToA
{
 static string Rename(string name)
 {
  if(!name.StartsWith("mixamorig:"))return name;
  string n=name.Substring(10);
  if(n=="Spine1")return "Chest";
  if(n=="Spine2")return "UpperChest";
  if(n=="HeadTop_End")return "HeadEnd";
  foreach(string side in new[]{"Left","Right"})
  {
   if(n==side+"UpLeg")return side+"UpperLeg";
   if(n==side+"Leg")return side+"LowerLeg";
   if(n==side+"ToeBase")return side+"Toes";
   if(n==side+"Toe_End")return side+"ToesEnd";
   if(n==side+"Arm")return side+"UpperArm";
   if(n==side+"ForeArm")return side+"LowerArm";
   foreach(string finger in new[]{"Thumb","Middle"})
    for(int i=1;i<=4;i++)if(n==side+"Hand"+finger+i)
     return side+finger+new[]{"Proximal","Intermediate","Distal","DistalEnd"}[i-1];
  }
  return n;
 }
 static string Path(string path)=>string.Join("/",path.Split('/').Select(Rename));
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  const string folder="Assets/3DBattle/Boss2/BossAnim";
  const string output=folder+"/ConvertedA";
  var paths=AssetDatabase.FindAssets("t:Model",new[]{folder}).Select(AssetDatabase.GUIDToAssetPath).ToArray();
  var sourcePath=paths.Single(p=>p.Contains("01a1b97c"));
  var source=BattleCharacterAssetPaths.Load<GameObject>(sourcePath);
  var reference=BattleCharacterAssetPaths.Load<GameObject>(paths.Single(p=>p.Contains("Double_Combo_Attack")));
  var originalClip=BattleCharacterAssetPaths.LoadAll(sourcePath).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
  if(!AssetDatabase.IsValidFolder(output))AssetDatabase.CreateFolder(folder,"ConvertedA");
  if(BattleCharacterAssetPaths.Load<AnimationClip>(output+"/B2_RetargetClip_A.anim")!=null)throw new Exception("Conversion exists; inspect before replacing");
  var original=UnityEngine.Object.Instantiate(source);
  var converted=UnityEngine.Object.Instantiate(source);
  try
  {
   if(PrefabUtility.IsPartOfPrefabInstance(converted))PrefabUtility.UnpackPrefabInstance(converted,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
   converted.name="B2_RetargetClip_A";
   var originalNodes=original.GetComponentsInChildren<Transform>(true).Where(t=>t!=original.transform).ToArray();
   foreach(var t in converted.GetComponentsInChildren<Transform>(true))if(t!=converted.transform)t.name=Rename(t.name);
   var refPaths=reference.transform.Find("SmartRigArmature").GetComponentsInChildren<Transform>(true).Select(t=>AnimationUtility.CalculateTransformPath(t,reference.transform)).OrderBy(p=>p).ToArray();
   var newPaths=converted.transform.Find("SmartRigArmature").GetComponentsInChildren<Transform>(true).Select(t=>AnimationUtility.CalculateTransformPath(t,converted.transform)).OrderBy(p=>p).ToArray();
   if(!refPaths.SequenceEqual(newPaths))throw new Exception("Hierarchy differs from A: "+string.Join(",",newPaths.Except(refPaths)));
   var clip=new AnimationClip{name="B2_RetargetClip_A",frameRate=originalClip.frameRate};
   AnimationUtility.SetAnimationClipSettings(clip,AnimationUtility.GetAnimationClipSettings(originalClip));
   foreach(var b in AnimationUtility.GetCurveBindings(originalClip))
   {var mapped=b;mapped.path=Path(b.path);if(mapped.path!=""&&converted.transform.Find(mapped.path)==null)throw new Exception("Missing "+mapped.path);AnimationUtility.SetEditorCurve(clip,mapped,AnimationUtility.GetEditorCurve(originalClip,b));}
   foreach(var b in AnimationUtility.GetObjectReferenceCurveBindings(originalClip))
   {var mapped=b;mapped.path=Path(b.path);AnimationUtility.SetObjectReferenceCurve(clip,mapped,AnimationUtility.GetObjectReferenceCurve(originalClip,b));}
   AnimationUtility.SetAnimationEvents(clip,AnimationUtility.GetAnimationEvents(originalClip));
   float maxPositionError=0,maxRotationError=0;
   var newNodes=originalNodes.Select(t=>converted.transform.Find(Path(AnimationUtility.CalculateTransformPath(t,original.transform)))).ToArray();
   for(int f=0;f<=89;f++)
   {
    originalClip.SampleAnimation(original,f/30f);clip.SampleAnimation(converted,f/30f);
    for(int i=0;i<originalNodes.Length;i++)
    {if(newNodes[i]==null)throw new Exception("Missing mapped node");maxPositionError=Mathf.Max(maxPositionError,Vector3.Distance(originalNodes[i].position,newNodes[i].position));maxRotationError=Mathf.Max(maxRotationError,Quaternion.Angle(originalNodes[i].rotation,newNodes[i].rotation));}
   }
   if(maxPositionError>.0001f||maxRotationError>.1f)throw new Exception("Pose mismatch");
   AssetDatabase.CreateAsset(clip,output+"/B2_RetargetClip_A.anim");
   var avatar=AvatarBuilder.BuildGenericAvatar(converted,"");avatar.name="B2_A_GenericAvatar";
   if(!avatar.isValid)throw new Exception("Invalid avatar");
   AssetDatabase.CreateAsset(avatar,output+"/B2_A_GenericAvatar.asset");
   var controller=AnimatorController.CreateAnimatorControllerAtPath(output+"/B2_RetargetClip_A.controller");
   var state=controller.layers[0].stateMachine.AddState("RetargetClip_A");state.motion=clip;controller.layers[0].stateMachine.defaultState=state;
   var animator=converted.GetComponent<Animator>();if(!animator)animator=converted.AddComponent<Animator>();
   animator.avatar=avatar;animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
   clip.SampleAnimation(converted,0);
   PrefabUtility.SaveAsPrefabAsset(converted,output+"/B2_RetargetClip_A.prefab");AssetDatabase.SaveAssets();
   return new{output,hierarchyMatchesA=true,rigNodes=newPaths.Length,checkedFrames=90,maxPositionError,maxRotationError,clip.length,clip.frameRate};
  }
  finally{UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(converted);}
 }
}
