using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class Boss1RuntimeCheck {
 public static string Run(){
 var source=GameObject.Find("Boss1_AllAnimations");var sd=source.GetComponent<MeshyAnimationDebugger>();if(!sd.IsPlaying||sd.clips.Length!=15)throw new Exception("Playlist not started");
 var g=UnityEngine.Object.Instantiate(source);
 try{
 var d=g.GetComponent<MeshyAnimationDebugger>();d.enabled=false;var a=g.GetComponent<Animator>();a.enabled=true;a.Rebind();a.Update(0);var ts=g.GetComponentsInChildren<Transform>();int moved=0;
 foreach(var clip in d.clips){string name=clip.name.Split('|').Last();int hash=Animator.StringToHash("Base Layer."+name);if(!a.HasState(0,hash))throw new Exception("Missing state "+name);a.Play(hash,0,0);a.Update(0);var first=ts.Select(t=>t.localRotation).ToArray();bool changes=false;for(int f=1;f<=4;f++){a.Play(hash,0,f/5f);a.Update(0);changes|=ts.Where((t,i)=>Quaternion.Angle(first[i],t.localRotation)>.1f).Any();}if(!changes)throw new Exception("Animator did not animate "+name);moved++;}
 var renderers=g.GetComponentsInChildren<Renderer>();if(renderers.Any(r=>r.sharedMaterials.Any(m=>m==null||m.mainTexture==null)))throw new Exception("Missing material texture");
 return "PASS: sequential debugger started; Animator states moving "+moved+"/15; textured renderers="+renderers.Length;
 }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
}
