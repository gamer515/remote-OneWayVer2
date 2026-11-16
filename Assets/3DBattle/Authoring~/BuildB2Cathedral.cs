using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using OneWay.Battle3D;
public static class BuildB2Cathedral
{
 const string Folder="Assets/3DBattle/Boss2/Cathedral";
 const string Generated=Folder+"/StageAssets";
 static Material Mat(string name,string image)
 {
  var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.name=name;
  m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/"+image));m.SetColor("_BaseColor",Color.white);m.SetFloat("_Cull",0);
  AssetDatabase.CreateAsset(m,Generated+"/"+name+".mat");return m;
 }
 static GameObject Surface(Transform root,string name,int nx,int ny,Func<float,float,Vector3> pos,Func<float,float,Vector2> uv,Material mat)
 {
  var v=new List<Vector3>();var u=new List<Vector2>();var tri=new List<int>();
  for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++){float a=(float)x/nx,b=(float)y/ny;v.Add(pos(a,b));u.Add(uv(a,b));}
  for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){int i=y*(nx+1)+x;tri.AddRange(new[]{i,i+nx+1,i+1,i+1,i+nx+1,i+nx+2});}
  var mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetUVs(0,u);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Generated+"/"+name+".asset");
  var go=new GameObject(name);go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;return go;
 }
 public static string Run()
 {
  if(Application.isPlaying)throw new Exception("Stop Play first");
  var stages=UnityEngine.Object.FindObjectsByType<BattleStage>(FindObjectsInactive.Include,FindObjectsSortMode.None);
  var b2=stages.Single(s=>s.name=="B2");var b1=stages.Single(s=>s.name=="B1");
  if(b2.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Wrong scene");
  if(b2.transform.Find("Cathedral Stage")!=null||AssetDatabase.IsValidFolder(Generated))throw new Exception("Already built");
  AssetDatabase.CreateFolder(Folder,"StageAssets");
  var root=new GameObject("Cathedral Stage");Undo.RegisterCreatedObjectUndo(root,"Build B2 cathedral");root.transform.SetParent(b2.transform,false);
  var front=Mat("Cathedral Front","고딕 성당 전면벽과 스테인드글라스-1.png");var back=Mat("Cathedral Entrance","고딕 성당 후면 입구벽과 장미창-3.png");
  var left=Mat("Cathedral Left","네 창의 고딕 성당 측벽-2.png");var right=Mat("Cathedral Right","대성당 오른쪽 벽과 네 개의 창-5.png");var roof=Mat("Cathedral Vault","고딕 사연식 금빛 리브 천장-4.png");
  // Four independent planar walls. End walls include an elliptical arch matching the vault.
  for(int side=0;side<2;side++){
   float sign=side==0?-1:1;
   Surface(root.transform,side==0?"Wall Left":"Wall Right",1,1,(u,v)=>new Vector3(Mathf.Lerp(-36,36,u),v*24,sign*36),(u,v)=>new Vector2(Mathf.Lerp(.025f,.975f,u),Mathf.Lerp(.095f,.89f,v)),side==0?left:right);
  }
  // Set side geometry explicitly to keep each full image spanning the entire wall.
  foreach(var name in new[]{"Wall Left","Wall Right"}){
   var mesh=root.transform.Find(name).GetComponent<MeshFilter>().sharedMesh;float z=name=="Wall Left"?-36:36;mesh.vertices=new[]{new Vector3(-36,0,z),new Vector3(36,0,z),new Vector3(-36,24,z),new Vector3(36,24,z)};mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
  }
  for(int end=0;end<2;end++){
   float x=end==0?-36:36;var mat=end==0?front:back;
   Surface(root.transform,end==0?"Wall Front":"Wall Entrance",64,32,(u,v)=>{
    float s=-Mathf.Cos(u*Mathf.PI);float y=v<=.5f?v*48:24+(v-.5f)*2*18*Mathf.Sqrt(Mathf.Max(0,1-s*s));return new Vector3(x,y,s*36);
   },(u,v)=>{
    float signed=-Mathf.Cos(u*Mathf.PI);float s=Mathf.Abs(signed);float top=42+530*Mathf.Pow(s,1.22f);float pixelY=v<=.5f?Mathf.Lerp(1265,570,v*2):Mathf.Lerp(570,top,(v-.5f)*2);
    return new Vector2(Mathf.Lerp(.069f,.931f,(signed+1)*.5f),1-pixelY/1293f);
   },mat);
  }
  Surface(root.transform,"Rounded Vault Ceiling",24,64,(u,v)=>{float a=v*Mathf.PI;return new Vector3(Mathf.Lerp(-36,36,u),24+18*Mathf.Sin(a),-36*Mathf.Cos(a));},(u,v)=>new Vector2(Mathf.Lerp(.035f,.965f,u),Mathf.Lerp(.19f,.82f,v)),roof);
  var floor=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Cathedral Floor"};floor.SetColor("_BaseColor",new Color(.60f,.55f,.43f));floor.SetFloat("_Smoothness",.15f);AssetDatabase.CreateAsset(floor,Generated+"/Cathedral Floor.mat");
  var arena=b2.transform.Find("Arena").GetComponent<Renderer>();Undo.RecordObject(arena,"Cathedral floor");arena.sharedMaterial=floor;
  var boundaryRoot=new GameObject("Transparent Arena Boundary");boundaryRoot.transform.SetParent(root.transform,false);
  var boxes=new List<BoxCollider>();
  foreach(var source in b1.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.name=="Wall1"||c.name=="Wall2"||c.name=="Wall3"||c.name=="Wall4").OrderBy(c=>c.name)){
   var wall=new GameObject(source.name);wall.transform.SetParent(boundaryRoot.transform,false);wall.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);wall.transform.localScale=source.transform.lossyScale;
   var box=wall.AddComponent<BoxCollider>();box.center=source.center;box.size=source.size;box.sharedMaterial=source.sharedMaterial;box.isTrigger=false;boxes.Add(box);
  }
  if(boxes.Count!=4)throw new Exception("B1 four walls missing");
  var boundary=b2.GetComponent<BattleArenaBoundary>()??Undo.AddComponent<BattleArenaBoundary>(b2.gameObject);Undo.RecordObject(boundary,"B2 boundary");boundary.walls=boxes.ToArray();Undo.RecordObject(b2,"B2 boundary");b2.boundary=boundary;
  var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
  var body=b2.boss.GetComponent<BattleArenaBody>()??Undo.AddComponent<BattleArenaBody>(b2.boss.gameObject);body.battle=battle;
  var capsule=b2.boss.GetComponent<CapsuleCollider>();if(capsule!=null){body.localCenter=capsule.center;body.radius=capsule.radius*Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x),Mathf.Abs(capsule.transform.lossyScale.z));}
  EditorUtility.SetDirty(body);EditorUtility.SetDirty(boundary);EditorUtility.SetDirty(b2);
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(b2.gameObject.scene);EditorSceneManager.SaveScene(b2.gameObject.scene);
  Selection.activeGameObject=root;return "Created 4 independent walls + rounded vault, no shadow casting, B1-sized 4 solid invisible boundaries; saved scene.";
 }
}


