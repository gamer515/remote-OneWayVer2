using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class MeshyAllCheck {
 public static string Run(){
 var src=GameObject.Find("Meshy_AI_boss_character_model_biped_Meshy_Merged_Animations (1)");
 var g=UnityEngine.Object.Instantiate(src);g.hideFlags=HideFlags.HideAndDontSave;
 try{
 var d=g.GetComponent<MeshyAnimationDebugger>();d.logClipChanges=false;var ts=g.GetComponentsInChildren<Transform>();var lines=new List<string>();int moving=0;
 for(int i=0;i<d.clips.Length;i++){
 d.PlayClip(i);d.Seek(0);var q=ts.Select(t=>t.localRotation).ToArray();var p=ts.Select(t=>t.localPosition).ToArray();var changed=new HashSet<int>();
 for(int f=1;f<=8;f++){d.Seek(d.CurrentClip.length*f/8f);for(int j=0;j<ts.Length;j++){if(!float.IsFinite(ts[j].position.x)||!float.IsFinite(ts[j].position.y)||!float.IsFinite(ts[j].position.z))throw new Exception("Non-finite pose "+d.CurrentClip.name);if(Quaternion.Angle(q[j],ts[j].localRotation)>.1f||Vector3.Distance(p[j],ts[j].localPosition)>.00001f)changed.Add(j);}}
 int missing=AnimationUtility.GetCurveBindings(d.CurrentClip).Where(b=>b.path!=""&&g.transform.Find(b.path)==null).Select(b=>b.path).Distinct().Count();
 if(changed.Count>0)moving++;
 lines.Add($"{i+1}: {d.CurrentClip.name}; moving transforms={changed.Count}; missing paths={missing}");
 }
 d.PlayClip(0);d.Seek(d.CurrentClip.length);d.endHoldSeconds=0;d.TogglePause();typeof(MeshyAnimationDebugger).GetMethod("LateUpdate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(d,null);if(d.CurrentIndex!=1)throw new Exception("Advance failed");
 string result=$"Moving clips: {moving}/{d.clips.Length}; auto-advance PASS\n"+string.Join("\n",lines);System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/MeshyAllValidation.txt",result);return result;
 }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
}
