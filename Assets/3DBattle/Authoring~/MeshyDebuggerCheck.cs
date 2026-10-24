using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using OneWay.Battle3D;
public static class MeshyDebuggerCheck {
 public static string Run(){
 var src=UnityEngine.Object.FindFirstObjectByType<MeshyAnimationDebugger>();
 var go=UnityEngine.Object.Instantiate(src.gameObject);var d=go.GetComponent<MeshyAnimationDebugger>();d.logClipChanges=false;
 try{
 var poses=go.GetComponentsInChildren<Transform>();
 d.enabled=false;var initial=poses.Select(t=>t.localToWorldMatrix).ToArray();d.enabled=true;
 var results=new List<string>();
 for(int i=0;i<d.clips.Length;i++){
 d.PlayClip(i);d.Seek(0);var first=poses.Select(t=>t.localToWorldMatrix).ToArray();d.Seek(d.CurrentClip.length*.5f);
 float diff=0;for(int j=0;j<poses.Length;j++)for(int k=0;k<16;k++)diff=Mathf.Max(diff,Mathf.Abs(first[j][k]-poses[j].localToWorldMatrix[k]));
 results.Add(d.CurrentClip.name+": midpoint matrix delta="+diff);
 }
 d.PlayClip(0);d.Next();if(d.CurrentIndex!=1)throw new Exception("Next failed");d.Previous();if(d.CurrentIndex!=0)throw new Exception("Previous failed");
 d.Seek(d.CurrentClip.length);d.endHoldSeconds=0;d.TogglePause();
 typeof(MeshyAnimationDebugger).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(d,null);
 if(d.CurrentIndex!=1)throw new Exception("Automatic transition failed");
 d.enabled=false;
 for(int j=0;j<poses.Length;j++)for(int k=0;k<16;k++)if(Mathf.Abs(initial[j][k]-poses[j].localToWorldMatrix[k])>.01f)throw new Exception("Pose restore failed");
 System.IO.File.WriteAllLines("Assets/3DBattle/Authoring~/MeshyDebuggerValidation.txt",results);
 return "PASS: 20 clip sampling, next/previous, automatic advance, disable restores pose.\n"+string.Join("\n",results);
 }finally{UnityEngine.Object.DestroyImmediate(go);}
 }
}
