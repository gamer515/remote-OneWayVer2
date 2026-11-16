using UnityEngine;using UnityEditor;using UnityEngine.Rendering.Universal;
public static class ReviewPackageGoldenSlash{
public static string Run(){var slash=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Boss2/GoldenSlashVFX/B2GoldenSlash.prefab"));slash.hideFlags=HideFlags.HideAndDontSave;slash.transform.position=new Vector3(-6,5,0);slash.transform.rotation=Quaternion.Euler(20,0,0);slash.transform.localScale=Vector3.one*13.6f;
var smoke=slash.GetComponentInChildren<ParticleSystem>(); smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); smoke.Play(true); for(int i=0;i<32;i++){slash.transform.position=new Vector3(-6,5,-8+i*.25f);smoke.Simulate(.025f,true,false,true);} var go=new GameObject("Temporary golden slash camera");go.hideFlags=HideFlags.HideAndDontSave;var cam=go.AddComponent<Camera>();cam.enabled=false;cam.allowHDR=true;cam.fieldOfView=50;cam.farClipPlane=250;cam.transform.position=new Vector3(15,10,22);cam.transform.LookAt(new Vector3(-6,5,0));var data=cam.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.volumeLayerMask=~0;
var rt=new RenderTexture(1100,800,24,RenderTextureFormat.ARGBHalf);var tex=new Texture2D(1100,800,TextureFormat.RGB24,false);var old=RenderTexture.active;
try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1100,800),0,0);tex.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/B2PackageGoldenSlashReview.png",tex.EncodeToPNG());return "Smoke particles="+smoke.particleCount;}
finally{RenderTexture.active=old;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(slash);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
}}



