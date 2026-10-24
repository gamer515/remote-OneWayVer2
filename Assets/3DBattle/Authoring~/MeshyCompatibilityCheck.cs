using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
public static class MeshyCompatibilityCheck {
 public static string Run(){
 string a="Assets/3DBattle/Meshy_AI_boss_character_model_biped_attack/Meshy_AI_boss_character_model_biped_Animation_Basic_Jump_withSkin.fbx";
 string b="Assets/3DBattle/Meshy_AI_boss_character_model_biped_rest/Meshy_AI_boss_character_model_biped_Animation_Axe_Breathe_and_Look_Around_withSkin.fbx";
 var prefabs=new[]{a,b}.Select(p=>AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray();
 var clips=new[]{a,b}.Select(p=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"))).ToArray();
 var gs=prefabs.Select(p=>UnityEngine.Object.Instantiate(p)).ToArray();var ms=new[]{new Mesh(),new Mesh()};
 try{
 foreach(var g in gs){g.hideFlags=HideFlags.HideAndDontSave;g.transform.position=Vector3.zero;}
 var rs=gs.Select(g=>g.GetComponentInChildren<SkinnedMeshRenderer>()).ToArray();foreach(var r in rs)r.updateWhenOffscreen=true;
 var paths=gs.Select(g=>g.GetComponentsInChildren<Transform>().Select(t=>AnimationUtility.CalculateTransformPath(t,g.transform)).OrderBy(s=>s).ToArray()).ToArray();
 var logs=new List<string>{"Identical hierarchy: "+paths[0].SequenceEqual(paths[1]),"Bone counts: "+rs[0].bones.Length+" / "+rs[1].bones.Length,"Vertices: "+rs[0].sharedMesh.vertexCount+" / "+rs[1].sharedMesh.vertexCount};
 float bindDiff=0;var bindings=rs.Select(r=>r.bones.Select((t,i)=>new{t.name,m=r.sharedMesh.bindposes[i]}).ToDictionary(x=>x.name,x=>x.m)).ToArray();
 foreach(var kv in bindings[0]){if(!bindings[1].ContainsKey(kv.Key))throw new Exception("Missing bone "+kv.Key);for(int k=0;k<16;k++)bindDiff=Mathf.Max(bindDiff,Mathf.Abs(kv.Value[k]-bindings[1][kv.Key][k]));}logs.Add("Max bind matrix difference: "+bindDiff);
 foreach(var clip in clips){
 foreach(var g in gs){var missing=AnimationUtility.GetCurveBindings(clip).Where(x=>x.path!=""&&g.transform.Find(x.path)==null).Select(x=>x.path).Distinct().ToArray();if(missing.Length>0)throw new Exception("Unbound paths: "+string.Join(",",missing));}
 float maxDiff=0,movement=0;Vector3[] initial=null;
 for(int f=0;f<=8;f++){
 var verts=new Vector3[2][];
 for(int i=0;i<2;i++){clip.SampleAnimation(gs[i],clip.length*f/8f);rs[i].BakeMesh(ms[i],true);verts[i]=ms[i].vertices.Select(v=>rs[i].transform.TransformPoint(v)).ToArray();}
 if(initial==null)initial=verts[0];
 if(verts[0].Length!=verts[1].Length)throw new Exception("Different topology");
 for(int j=0;j<verts[0].Length;j++){maxDiff=Mathf.Max(maxDiff,Vector3.Distance(verts[0][j],verts[1][j]));movement=Mathf.Max(movement,Vector3.Distance(initial[j],verts[0][j]));}
 }
 logs.Add(clip.name+": cross-model max vertex difference="+maxDiff+", motion distance="+movement);
 }
 return string.Join("\n",logs);
 }finally{foreach(var g in gs)UnityEngine.Object.DestroyImmediate(g);foreach(var m in ms)UnityEngine.Object.DestroyImmediate(m);}
 }
}
