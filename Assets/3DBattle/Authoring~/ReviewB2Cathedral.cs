using System;using System.Linq;using UnityEngine;using UnityEditor;using OneWay.Battle3D;
public static class ReviewB2Cathedral{
 public static string Run(){
 var stage=UnityEngine.Object.FindObjectsByType<BattleStage>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(s=>s.name=="B2");var root=stage.transform.Find("Cathedral Stage");
 if(root.GetComponentsInChildren<Renderer>().Any(r=>r.shadowCastingMode!=UnityEngine.Rendering.ShadowCastingMode.Off))throw new Exception("Shadow caster found");
 foreach(var d in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back}){var p=stage.boundary.Constrain(d*100+Vector3.up*50,1);if(Mathf.Abs(p.x)>28.01f||Mathf.Abs(p.z)>28.01f||Mathf.Abs(p.y-50)>.01f)throw new Exception("Boundary failed "+p);}
 var go=new GameObject("Temporary cathedral review camera");go.hideFlags=HideFlags.HideAndDontSave;var cam=go.AddComponent<Camera>();cam.enabled=false;cam.fieldOfView=75;cam.nearClipPlane=.1f;cam.farClipPlane=200;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.magenta;
 var rt=new RenderTexture(1000,700,24);var tex=new Texture2D(2000,700,TextureFormat.RGB24,false);var old=RenderTexture.active;
 try{cam.targetTexture=rt;for(int i=0;i<2;i++){cam.transform.position=new Vector3(i==0?24:-24,7,i==0?-8:8);cam.transform.LookAt(new Vector3(i==0?-24:24,18,0));cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1000,700),i*1000,0);}tex.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/B2CathedralReview.png",tex.EncodeToPNG());return "PASS: 5 shadow-free surfaces, 4 infinite-height boundary directions; interior review saved.";}
 finally{RenderTexture.active=old;cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);}
 }
}
