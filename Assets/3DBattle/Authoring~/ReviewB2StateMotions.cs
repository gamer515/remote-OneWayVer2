using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class ReviewB2StateMotions
{
 const string Folder="Assets/3DBattle/Boss2/BossAnim/Approach/";
 public static object Run()
 {
  var prefab=BattleCharacterAssetPaths.Load<GameObject>(Folder+"B2_ApproachDash_Preview.prefab");
  var model=UnityEngine.Object.Instantiate(prefab);model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);model.transform.localScale=Vector3.one;model.GetComponent<Animator>().enabled=false;
  var clip=BattleCharacterAssetPaths.Load<AnimationClip>(Folder+"B2_ApproachDash_InPlace.anim");
  var cameraGo=new GameObject("B2 approach review camera");var camera=cameraGo.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.16f,.19f);camera.orthographic=true;camera.orthographicSize=1.55f;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<30;
  int width=330,height=360;
  var atlas=new Texture2D(width*6,height*5,TextureFormat.RGB24,false);var tile=new Texture2D(width,height,TextureFormat.RGB24,false);var rt=new RenderTexture(width,height,24);camera.targetTexture=rt;var old=RenderTexture.active;
  var baked=new List<Mesh>();var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.color=new Color(.72f,.75f,.79f);
  try
  {
   foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
   var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();
   var outputs=new List<Mesh>();
   foreach(var skin in skins)
   {
    var go=new GameObject("Review skin");go.layer=30;go.transform.SetParent(skin.transform,false);go.transform.localScale=Vector3.one/skin.transform.lossyScale.x;
    var mesh=new Mesh();baked.Add(mesh);outputs.Add(mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
   }
   var bone=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
   var right=bone["RightHand"];var sword=bone["B2 Holy Sword"];var hips=bone["Hips"];
   var frames=new List<object>();var renderFrames=new[]{0f,10f,20f,30f,55f,89f};
   for(int row=0;row<5;row++)for(int col=0;col<6;col++)
   {
    var stateNames=new[]{"B2_PhaseRecovery","B2_Death","B2_HitReaction","B2_Run","B2_Idle_02"}; clip=BattleCharacterAssetPaths.Load<AnimationClip>("Assets/3DBattle/Boss2/BossAnim/StateMachine/"+stateNames[row]+".anim"); float sampleTime=row==0 ? new[]{0f,12f,24f,80f,165f,230f}[col]/30f : col/5f*clip.length; clip.SampleAnimation(model,sampleTime);
    for(int i=0;i<skins.Length;i++)skins[i].BakeMesh(outputs[i]);
    var focus=new Vector3(0,.83f,-.20f);
    camera.transform.position=focus+(new Vector3(4.2f,2.0f,6.0f));camera.transform.LookAt(focus);
    camera.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,width,height),0,0);tile.Apply();atlas.SetPixels(col*width,(4-row)*height,width,height,tile.GetPixels());
    if(row==0)frames.Add(new{frame=renderFrames[col],headPitch=Vector3.Angle(bone["headfront"].position-bone["Head"].position,Vector3.forward),bladeForward=sword.forward.ToString("F3"),rightHand=right.position.ToString("F3"),hips=hips.position.ToString("F3")});
   }
   atlas.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/B2StateMotionReview.png",atlas.EncodeToPNG());
   return frames;
  }
  finally
  {
   RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(model);UnityEngine.Object.DestroyImmediate(cameraGo);UnityEngine.Object.DestroyImmediate(atlas);UnityEngine.Object.DestroyImmediate(tile);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(material);foreach(var m in baked)UnityEngine.Object.DestroyImmediate(m);
  }
 }
}

