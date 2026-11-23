using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class ReviewB2SmokeRunOutPreview
{
 public static object Run()
 {
  var scene=EditorSceneManager.NewPreviewScene();
  var prefab=BattleCharacterAssetPaths.Load<GameObject>("Assets/3DBattle/Boss2/PhasePreview/B2_SmokeRunOut_Preview.prefab");
  var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
  var driver=root.GetComponent<B2SmokeRunOutPreview>();
  var cg=new GameObject("Review camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cg,scene);
  var camera=cg.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.15f,.19f);camera.orthographic=true;camera.orthographicSize=10;camera.nearClipPlane=.01f;camera.farClipPlane=150;camera.cullingMask=1<<30;
  camera.GetUniversalAdditionalCameraData().requiresDepthTexture=true;camera.GetUniversalAdditionalCameraData().requiresColorTexture=true;
  var lightGo=new GameObject("Review light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo,scene);var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;lightGo.transform.rotation=Quaternion.Euler(40,30,0);
  var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor,scene);floor.layer=30;floor.transform.localScale=Vector3.one*10;
  var fm=new Material(Shader.Find("Universal Render Pipeline/Unlit"));fm.color=new Color(.2f,.23f,.27f);floor.GetComponent<Renderer>().sharedMaterial=fm;
  foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
  var skins=driver.model.GetComponentsInChildren<SkinnedMeshRenderer>(true);var meshes=new List<Mesh>();var outputs=new List<MeshRenderer>();
  foreach(var skin in skins)
  {
   var go=new GameObject("Baked review skin");go.layer=30;go.transform.SetParent(skin.transform,false);go.transform.localScale=Vector3.one/skin.transform.lossyScale.x;
   var mesh=new Mesh();meshes.Add(mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterials=skin.sharedMaterials;outputs.Add(mr);
  }
  int w=420,h=360;var atlas=new Texture2D(w*6,h*2,TextureFormat.RGB24,false);var tile=new Texture2D(w,h,TextureFormat.RGB24,false);var rt=new RenderTexture(w,h,24);camera.targetTexture=rt;var old=RenderTexture.active;var report=new List<object>();
  try
  {
   float[] times={.95f,3.3f,3.8f,4.35f,5.15f,6.2f};
   var origin=driver.cloud.cloudRoot.position;
   var bones=driver.model.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
   for(int row=0;row<2;row++)for(int col=0;col<times.Length;col++)
   {
    driver.SamplePreview(times[col]);
    bool visible=driver.bodyRenderers.Any(r=>r.enabled);
    if(times[col]<driver.runStart&&visible)throw new Exception("Boss visible before smoke spread");
    if(Vector3.Distance(driver.cloud.cloudRoot.position,origin)>.001f)throw new Exception("Smoke followed the boss");
    for(int i=0;i<skins.Length;i++){skins[i].BakeMesh(meshes[i]);outputs[i].sharedMaterials=skins[i].sharedMaterials;outputs[i].enabled=skins[i].enabled;skins[i].enabled=false;}
    var focus=new Vector3(0,3.5f,5);camera.transform.position=focus+(row==0?new Vector3(18,5,22):new Vector3(-24,3,0));camera.transform.LookAt(focus);
    camera.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,w,h),0,0);tile.Apply();atlas.SetPixels(col*w,(1-row)*h,w,h,tile.GetPixels());
    if(row==0)report.Add(new{time=times[col],visible,position=driver.movingRoot.localPosition.ToString(),particles=driver.cloud.cloudLayers.Sum(p=>p.particleCount),forwardZ=Vector3.ProjectOnPlane(bones["headfront"].position-bones["Head"].position,Vector3.up).normalized.z});
   }
   atlas.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/B2SmokeRunOutReview.png",atlas.EncodeToPNG());
   driver.SamplePreview(4);var pose=bones["LeftUpperLeg"].localRotation;
   driver.SamplePreview(4.2f);if(Quaternion.Angle(pose,bones["LeftUpperLeg"].localRotation)<1)throw new Exception("Run pose did not animate");
   driver.StopPreview();
   if(driver.bodyRenderers.Any(r=>r.enabled)||driver.cloud.cloudLayers.Any(p=>p.particleCount!=0)||Vector3.Distance(driver.movingRoot.localPosition,driver.startPosition)>.001f)throw new Exception("Preview reset failed");
   var missing=AnimationUtility.GetCurveBindings(driver.runClip).Where(b=>!string.IsNullOrEmpty(b.path)&&driver.model.transform.Find(b.path)==null).Select(b=>b.path).Distinct().ToArray();
   if(missing.Length>0||root.GetComponentsInChildren<Collider>(true).Length>0)throw new Exception("Invalid rig or gameplay collider");
   return new{report,runAnimates=true,resetPassed=true,cloudStayedFixed=true,missingBindings=missing,colliders=0,components=root.GetComponentsInChildren<MonoBehaviour>(true).Select(c=>c.GetType().Name).ToArray()};
  }
  finally
  {
   RenderTexture.active=old;EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Object.DestroyImmediate(atlas);UnityEngine.Object.DestroyImmediate(tile);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(fm);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
  }
 }
}
