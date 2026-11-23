using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class ReviewB2SmokeSlashPreview
{
 public static object Run()
 {
  return Render(false);
 }
 public static object FloatingCloud()
 {
  return Render(true);
 }
 static object Render(bool closeup)
 {
  var scene=EditorSceneManager.NewPreviewScene();
  var prefab=BattleCharacterAssetPaths.Load<GameObject>("Assets/3DBattle/Boss2/PhasePreview/B2_SmokeSlash_Preview.prefab");
  var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
  var driver=root.GetComponent<B2SmokeSlashPreview>();
  var cg=new GameObject("Review camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cg,scene);
  var camera=cg.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.12f,.16f);camera.orthographic=true;camera.orthographicSize=14;camera.nearClipPlane=.01f;camera.farClipPlane=200;camera.cullingMask=1<<30;
  camera.GetUniversalAdditionalCameraData().requiresDepthTexture=true;camera.GetUniversalAdditionalCameraData().requiresColorTexture=true;
  var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor,scene);floor.layer=30;floor.transform.localScale=Vector3.one*20;
  var fm=new Material(Shader.Find("Universal Render Pipeline/Unlit"));fm.color=new Color(.16f,.18f,.22f);floor.GetComponent<Renderer>().sharedMaterial=fm;
  foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
  int w=520,h=360;var atlas=new Texture2D(w*3,h*2,TextureFormat.RGB24,false);var tile=new Texture2D(w,h,TextureFormat.RGB24,false);var rt=new RenderTexture(w,h,24);camera.targetTexture=rt;var old=RenderTexture.active;var report=new List<object>();
  try
  {
   float[] times=closeup?new[]{3.6f,4.1f,4.6f,5.1f,5.6f,6.2f}:new[]{.95f,1.75f,2f,3.6f,4.8f,6.2f};
   var fixedCloud=driver.cloudRoot.position;
   var focus=closeup?new Vector3(0,4,0):new Vector3(0,4,10);camera.orthographicSize=closeup?6:14;camera.transform.position=focus+(closeup?new Vector3(16,4,6):new Vector3(33,8,10));camera.transform.LookAt(focus);
   for(int i=0;i<times.Length;i++)
   {
    driver.SamplePreview(times[i]);
    camera.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,w,h),0,0);tile.Apply();atlas.SetPixels((i%3)*w,(1-i/3)*h,w,h,tile.GetPixels());
    if(Vector3.Distance(driver.cloudRoot.position,fixedCloud)>.001f)throw new Exception("Cloud moved");
    float expected=Mathf.Clamp(times[i]-driver.slashSpawnTime-driver.launchDelay,0,driver.flightSeconds)*driver.travelSpeed;
    if(Mathf.Abs(driver.slashRoot.localPosition.z-expected)>.001f)throw new Exception("Flight position mismatch");
    report.Add(new{time=times[i],cloudParticles=driver.cloudLayers.Sum(p=>p.particleCount),slashParticles=driver.slashLayers.Sum(p=>p.particleCount),slashVisible=driver.slashRoot.gameObject.activeSelf,flightDistance=driver.slashRoot.localPosition.z});
   }
   atlas.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/B2SmokeSlashReview.png",atlas.EncodeToPNG());
   driver.SamplePreview(driver.slashSpawnTime+.05f);
   if(driver.slashRoot.localPosition.z!=0||!driver.slashRoot.gameObject.activeSelf)throw new Exception("Launch delay failed");
   driver.SamplePreview(0);
   if(driver.slashRoot.gameObject.activeSelf||root.GetComponentsInChildren<ParticleSystem>(true).Any(p=>p.particleCount!=0))throw new Exception("Preview reset failed");
   if(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length!=0||root.GetComponentsInChildren<Animator>(true).Length!=0||root.GetComponentsInChildren<Collider>(true).Length!=0)throw new Exception("Unwanted character or collider");
   return new{report,resetPassed=true,launchDelayPassed=true,cloudFixed=true,noBossOrCollider=true,cloudLayers=driver.cloudLayers.Length,slashLayers=driver.slashLayers.Length};
  }
  finally
  {
   RenderTexture.active=old;EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Object.DestroyImmediate(atlas);UnityEngine.Object.DestroyImmediate(tile);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(fm);
  }
 }
}
