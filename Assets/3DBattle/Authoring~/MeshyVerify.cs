using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
public static class MeshyVerify {
 public static string Run(){
 const string path="Assets/3DBattle/Meshy_AI_boss_character_model_biped/Meshy_AI_boss_character_model_biped_Meshy_Merged_Animations.fbx";
 var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));g.hideFlags=HideFlags.HideAndDontSave;g.transform.position=Vector3.zero;
 var r=g.GetComponentInChildren<SkinnedMeshRenderer>();r.updateWhenOffscreen=true;var m=new Mesh();var logs=new List<string>();
 try { foreach(var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__"))){
 foreach(float t in new[]{0f,clip.length*.5f,clip.length}){clip.SampleAnimation(g,t);r.BakeMesh(m,true); logs.Add(clip.name+" @"+t+" baked "+m.bounds.ToString("F5")+" world "+r.bounds.ToString("F5"));}
 }
 System.IO.File.WriteAllLines("Assets/3DBattle/Authoring~/MeshyUnityValidation.txt",logs);
 return string.Join("\n",logs.Take(6));
 }finally{UnityEngine.Object.DestroyImmediate(m);UnityEngine.Object.DestroyImmediate(g);}
 }
}
