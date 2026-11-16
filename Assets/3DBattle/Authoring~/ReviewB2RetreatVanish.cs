using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class ReviewB2RetreatVanish
{
 const string Folder="Assets/3DBattle/Boss2/PhasePreview/";
 public static object Run()
 {
  var scene=EditorSceneManager.NewPreviewScene();
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"B2_RetreatVanish_Preview.prefab");
  var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
  var driver=root.GetComponent<B2RetreatVanishPreview>();
  var cg=new GameObject("Review camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cg,scene);
  var camera=cg.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.15f,.19f);camera.orthographic=true;camera.orthographicSize=9.2f;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.cullingMask=1<<30;
  camera.GetUniversalAdditionalCameraData().requiresDepthTexture=true;camera.GetUniversalAdditionalCameraData().requiresColorTexture=true;
  var lightGo=new GameObject("Review light");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo,scene);var light=lightGo.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;lightGo.transform.rotation=Quaternion.Euler(40,30,0);
  var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor,scene);floor.layer=30;floor.transform.localScale=Vector3.one*5;
  var fm=new Material(Shader.Find("Universal Render Pipeline/Unlit"));fm.color=new Color(.2f,.23f,.27f);floor.GetComponent<Renderer>().sharedMaterial=fm;
  foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
  var skins=driver.model.GetComponentsInChildren<SkinnedMeshRenderer>();var meshes=new List<Mesh>();var outputs=new List<MeshRenderer>();
  foreach(var skin in skins)
  {
   var go=new GameObject("Baked review skin");go.layer=30;go.transform.SetParent(skin.transform,false);go.transform.localScale=Vector3.one/skin.transform.lossyScale.x;
   var mesh=new Mesh();meshes.Add(mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterials=skin.sharedMaterials;outputs.Add(mr);
  }
  int w=340,h=380;var atlas=new Texture2D(w*6,h*2,TextureFormat.RGB24,false);var tile=new Texture2D(w,h,TextureFormat.RGB24,false);var rt=new RenderTexture(w,h,24);camera.targetTexture=rt;var old=RenderTexture.active;var report=new List<object>();
  try
  {
   float[] times={0,.95f,2.5f,3.7f,4.15f,5.8f};
   var cloudOrigin=driver.smokeLayers[0].transform.parent.position;
   for(int row=0;row<2;row++)for(int col=0;col<times.Length;col++)
   {
    driver.SamplePreview(times[col]);
    for(int i=0;i<skins.Length;i++){skins[i].BakeMesh(meshes[i]);outputs[i].sharedMaterials=skins[i].sharedMaterials;outputs[i].enabled=skins[i].enabled;skins[i].enabled=false;}
    var focus=new Vector3(0,3.7f,-3.1f);camera.transform.position=focus+(row==0?new Vector3(13,5,16):new Vector3(-16,3,0));camera.transform.LookAt(focus);
    camera.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,w,h),0,0);tile.Apply();atlas.SetPixels(col*w,(1-row)*h,w,h,tile.GetPixels());
    if(driver.smokeLayers.Any(p=>Vector3.Distance(p.transform.parent.position,cloudOrigin)>.001f))throw new Exception("Cloud followed the boss");
    if(times[col]==.95f&&(driver.movingRoot.localPosition.sqrMagnitude>.001f||!outputs.Any(r=>r.enabled)||driver.smokeLayers.Any(p=>p.particleCount==0)))throw new Exception("Cloud was not ready before retreat");
    if(row==0)report.Add(new{time=times[col],visible=outputs.Any(r=>r.enabled),particles=driver.smokeLayers.Sum(p=>p.particleCount),position=driver.movingRoot.localPosition.ToString(),cloudFixedPosition=cloudOrigin.ToString()});
   }
   atlas.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/B2RetreatVanishReview.png",atlas.EncodeToPNG());
   driver.SamplePreview(driver.hideAt);
   if(driver.bodyRenderers.Any(r=>r.enabled)||Vector3.Distance(driver.movingRoot.position,cloudOrigin)>.1f)throw new Exception("Boss disappeared before entering cloud");
   driver.SamplePreview(0);
   if(driver.bodyRenderers.Any(r=>!r.enabled)||driver.smokeLayers.Any(p=>p.particleCount!=0)||driver.movingRoot.localPosition.sqrMagnitude>.00001f)throw new Exception("Preview reset failed");
   var missing=AnimationUtility.GetCurveBindings(driver.backwardClip).Where(b=>driver.model.transform.Find(b.path)==null).Select(b=>b.path).Distinct().ToArray();
   return new{report,resetPassed=true,cloudStayedFixed=true,enteredCloudBeforeDisappearing=true,smokeLayers=driver.smokeLayers.Length,missingBindings=missing,colliders=root.GetComponentsInChildren<Collider>(true).Length,controllers=root.GetComponentsInChildren<MonoBehaviour>(true).Select(c=>c.GetType().Name).ToArray(),sourceSmokeMaterials=driver.smokeLayers.Select(p=>AssetDatabase.GetAssetPath(p.GetComponent<ParticleSystemRenderer>().sharedMaterial)).Distinct().ToArray()};
  }
  finally
  {
   RenderTexture.active=old;EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Object.DestroyImmediate(atlas);UnityEngine.Object.DestroyImmediate(tile);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(fm);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
  }
 }
}
