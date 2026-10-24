using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
public static class MeshyMotionInspect {
 public static string Run(){
 var log=new List<string>();
 foreach(var name in new[]{"Run_Jump_and_Roll","Shield_Push_Left"}){
 string path="Assets/3DBattle/MeshyPlayer/Meshy_AI_KnightCharacter_"+name+".fbx";
 var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));g.hideFlags=HideFlags.HideAndDontSave;
 try{var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));var bones=g.GetComponentsInChildren<Transform>().GroupBy(t=>t.name).ToDictionary(x=>x.Key,x=>x.First());
 var frames=name=="Run_Jump_and_Roll"?new[]{1,11,21,31,41,51,61,71,81,91,101,104}:new[]{1,8,15,22,29,36,43,50,57,64,70,71};
 foreach(int f in frames){clip.SampleAnimation(g,Mathf.Min((f-1)/30f,clip.length));log.Add(name+" frame="+f+" hips="+bones["Hips"].position.ToString("F3")+" head="+bones["Head"].position.ToString("F3")+" LH="+bones["LeftHand"].position.ToString("F3")+" RH="+bones["RightHand"].position.ToString("F3"));}
 Render(g,clip,frames,name);
 }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 File.WriteAllLines("Assets/3DBattle/Authoring~/MeshyMotionSourceInspection.txt",log);return string.Join("\n",log);
 }
 public static void Render(GameObject g,AnimationClip clip,int[] frames,string name){
 foreach(var t in g.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
 var camera=new GameObject("Review camera");var light=new GameObject("Review light");var rt=new RenderTexture(300,360,24);var tile=new Texture2D(300,360,TextureFormat.RGB24,false);var sheet=new Texture2D(1200,1080,TextureFormat.RGB24,false);var old=RenderTexture.active;var mesh=new Mesh();var proxy=new GameObject("Review skin");proxy.layer=31;
 try{var c=camera.AddComponent<Camera>();c.cullingMask=1<<31;c.orthographic=true;c.orthographicSize=1.15f;c.nearClipPlane=.01f;c.farClipPlane=100;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.16f,.16f,.16f);c.targetTexture=rt;var l=light.AddComponent<Light>();l.type=LightType.Directional;l.intensity=1;l.transform.rotation=Quaternion.Euler(40,-30,0);
 var sk=g.GetComponentsInChildren<SkinnedMeshRenderer>().First(r=>r.name=="Knight");proxy.AddComponent<MeshFilter>().sharedMesh=mesh;proxy.AddComponent<MeshRenderer>().sharedMaterials=sk.sharedMaterials;bool was=sk.enabled;sk.enabled=false;
 for(int i=0;i<frames.Length;i++){clip.SampleAnimation(g,Mathf.Min(Mathf.Max(0,(frames[i]-1)/30f),clip.length));sk.BakeMesh(mesh);proxy.transform.SetPositionAndRotation(sk.transform.position,sk.transform.rotation);proxy.transform.localScale=Vector3.one;var center=proxy.transform.TransformPoint(mesh.bounds.center);c.transform.position=center+new Vector3(3,1,4).normalized*10;c.transform.LookAt(center);c.orthographicSize=Mathf.Max(1.1f,mesh.bounds.size.magnitude*.55f);c.Render();RenderTexture.active=rt;tile.ReadPixels(new Rect(0,0,300,360),0,0);tile.Apply();sheet.SetPixels((i%4)*300,(2-i/4)*360,300,360,tile.GetPixels());}
 sk.enabled=was;sheet.Apply();File.WriteAllBytes("Assets/3DBattle/Authoring~/"+name+"_source.png",sheet.EncodeToPNG());
 }finally{RenderTexture.active=old;foreach(var o in new UnityEngine.Object[]{proxy,mesh,sheet,tile,rt,camera,light})UnityEngine.Object.DestroyImmediate(o);}
 }
}
