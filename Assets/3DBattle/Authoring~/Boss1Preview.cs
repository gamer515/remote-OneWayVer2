using System;
using System.Linq;
using UnityEngine;
public static class Boss1Preview {
 public static string Run(){
 var src=GameObject.Find("Boss1_AllAnimations");var g=UnityEngine.Object.Instantiate(src);g.hideFlags=HideFlags.HideAndDontSave;g.transform.position=Vector3.zero;
 foreach(var t in g.GetComponentsInChildren<Transform>())t.gameObject.layer=31;
 var camGo=new GameObject("Meshy Review Camera");var lightGo=new GameObject("Meshy Review Light");var rt=new RenderTexture(700,700,24);var tex=new Texture2D(700,700,TextureFormat.RGB24,false);var old=RenderTexture.active;
 try{
 var c=camGo.AddComponent<Camera>();c.cullingMask=1<<31;c.orthographic=true;c.orthographicSize=1.15f;c.nearClipPlane=.01f;c.farClipPlane=20;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.16f,.16f,.16f);c.transform.position=new Vector3(-2,1.5f,4);c.transform.LookAt(new Vector3(0,.85f,0));c.targetTexture=rt;
 var l=lightGo.AddComponent<Light>();l.type=LightType.Directional;l.intensity=.7f;l.transform.rotation=Quaternion.Euler(35,-30,0);
 c.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,700,700),0,0);tex.Apply();System.IO.File.WriteAllBytes("Assets/3DBattle/Authoring~/Boss1Preview.png",tex.EncodeToPNG());return "Rendered";
 }finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camGo);UnityEngine.Object.DestroyImmediate(lightGo);UnityEngine.Object.DestroyImmediate(g);}
 }
}


