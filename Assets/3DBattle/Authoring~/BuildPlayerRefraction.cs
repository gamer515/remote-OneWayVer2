using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using OneWay.Battle3D;
public static class BuildPlayerRefraction
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  string dir="Assets/3DBattle/Effects";
  var shader=Shader.Find("OneWay/Player Refractive Slash");
  if(shader==null || ShaderUtil.ShaderHasError(shader))throw new Exception("Refraction shader failed");
  var mesh=new Mesh{name="Player Refractive Crescent"};
  int n=100;var v=new Vector3[(n+1)*2];var uv=new Vector2[v.Length];var tri=new int[n*6];
  for(int i=0;i<=n;i++)
  {
   float u=i/(float)n,angle=Mathf.Lerp(-110,110,u)*Mathf.Deg2Rad;
   float width=2.3f*Mathf.Pow(Mathf.Sin(u*Mathf.PI),.65f);
   for(int j=0;j<2;j++){float r=7f-(j==0?width:0);v[i*2+j]=new Vector3(Mathf.Cos(angle)*r,Mathf.Sin(angle)*r,0);uv[i*2+j]=new Vector2(u,j);}
   if(i<n){int k=i*6,b=i*2;tri[k]=b;tri[k+1]=b+1;tri[k+2]=b+2;tri[k+3]=b+1;tri[k+4]=b+3;tri[k+5]=b+2;}
  }
  mesh.vertices=v;mesh.uv=uv;mesh.triangles=tri;mesh.RecalculateNormals();mesh.RecalculateBounds();
  AssetDatabase.CreateAsset(mesh,dir+"/PlayerRefractiveCrescent.asset");
  var mat=new Material(shader){name="Player Refractive Slash"};AssetDatabase.CreateAsset(mat,dir+"/PlayerRefractiveSlash.mat");
  var go=new GameObject("Player Refractive Slash");
  GameObject prefab;
  try {go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;go.AddComponent<BattleRefractiveSlash>();prefab=PrefabUtility.SaveAsPrefabAsset(go,dir+"/PlayerRefractiveSlash.prefab");}
  finally{UnityEngine.Object.DestroyImmediate(go);}
  var effects=UnityEngine.Object.FindObjectsByType<BattleSwordSlashEffect>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(e=>e.gameObject.scene.path=="Assets/Scenes/3DBattleScene.unity").ToArray();
  foreach(var e in effects){Undo.RecordObject(e,"Use refractive slash");e.slashPrefab=prefab;EditorUtility.SetDirty(e);}
  foreach(var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None))
  {if(cam.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity")continue;var data=cam.GetUniversalAdditionalCameraData();Undo.RecordObject(data,"Enable slash refraction texture");data.requiresColorOption=CameraOverrideOption.On;EditorUtility.SetDirty(data);}
  AssetDatabase.SaveAssets();var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/3DBattleScene.unity");UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
  return new {connected=effects.Length,shaderErrors=ShaderUtil.ShaderHasError(shader)};
 }
}
