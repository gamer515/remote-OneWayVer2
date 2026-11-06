using System;using System.Linq;using UnityEngine;using UnityEditor;using Newtonsoft.Json.Linq;
public static class ApplySmoothedB2Slash{
 public static string Run(){
 if(Application.isPlaying)throw new Exception("Stop play first");
 string dir="Assets/3DBattle/Boss2/";var original=AssetDatabase.LoadAssetAtPath<Mesh>(dir+"B2WhiteSlashMesh.asset");
 var data=JObject.Parse(System.IO.File.ReadAllText("Assets/3DBattle/Authoring~/SlashSmoothed.json"));var vertices=data["vertices"].Select(v=>new Vector3((float)v[0],(float)v[1],(float)v[2])).ToArray();var triangles=data["triangles"].Select(t=>(int)t).ToArray();
 var mesh=new Mesh{name="B2WhiteSlashMesh_Smooth"};mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.vertices=vertices;mesh.triangles=triangles;mesh.uv=new Vector2[vertices.Length];mesh.RecalculateNormals();mesh.RecalculateBounds();
 if(Vector3.Distance(mesh.bounds.size,original.bounds.size)>.0001f||Vector3.Distance(mesh.bounds.center,original.bounds.center)>.0001f)throw new Exception("Dimensions changed");
 AssetDatabase.CreateAsset(mesh,dir+"B2WhiteSlashMesh_Smooth.asset");
 var root=PrefabUtility.LoadPrefabContents(dir+"B2WhiteSlash.prefab");
 try{var filter=root.GetComponentInChildren<MeshFilter>();filter.sharedMesh=mesh;PrefabUtility.SaveAsPrefabAsset(root,dir+"B2WhiteSlash.prefab");}finally{PrefabUtility.UnloadPrefabContents(root);}
 AssetDatabase.SaveAssets();var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(dir+"B2WhiteSlash.prefab");if(prefab.GetComponentInChildren<MeshFilter>().sharedMesh!=mesh)throw new Exception("Prefab not connected");
 return "PASS: prefab connected to smoothed mesh; dimensions preserved; original retained; triangles="+triangles.Length/3;
 }
}
