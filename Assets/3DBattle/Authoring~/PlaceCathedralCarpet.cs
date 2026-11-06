using System;using System.Linq;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using OneWay.Battle3D;
public static class PlaceCathedralCarpet{
static Bounds BoundsOf(Transform t){var rs=t.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
public static string Run(){
 if(Application.isPlaying)throw new Exception("Edit mode required");var b2=UnityEngine.Object.FindObjectsByType<BattleStage>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(s=>s.name=="B2");if(b2.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Wrong scene");
 if(b2.transform.Find("Cathedral Center Carpet")!=null)throw new Exception("Already exists");
 var entrance=BoundsOf(b2.transform.Find("Cathedral Stage/Wall Entrance"));var stairs=BoundsOf(b2.transform.Find("Cathedral Altar and Steps/Cathedral Steps"));var floor=BoundsOf(b2.transform.Find("Arena"));
 float start=stairs.max.x+.08f,end=entrance.center.x-.08f,width=10,y=floor.max.y+.025f,z=stairs.center.z;
 if(end<=start)throw new Exception("Invalid carpet endpoints");
 string dir="Assets/3DBattle/Boss2/Cathedral/StageAssets/";var texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/3DBattle/Boss2/Cathedral/T_Fabric_valvet_01_BC.png");if(texture==null)throw new Exception("Texture missing");
 var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Cathedral Velvet Carpet"};mat.SetTexture("_BaseMap",texture);mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Metallic",0);mat.SetFloat("_Smoothness",.12f);mat.SetFloat("_Cull",0);AssetDatabase.CreateAsset(mat,dir+"Cathedral Velvet Carpet.mat");
 var mesh=new Mesh{name="Cathedral Center Carpet"};float half=(end-start)*.5f;
 mesh.vertices=new[]{new Vector3(-half,0,-width*.5f),new Vector3(-half,0,width*.5f),new Vector3(half,0,-width*.5f),new Vector3(half,0,width*.5f)};mesh.triangles=new[]{0,1,2,2,1,3};mesh.uv=new[]{Vector2.zero,new Vector2(0,width/2),new Vector2((end-start)/2,0),new Vector2((end-start)/2,width/2)};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,dir+"Cathedral Center Carpet.asset");
 var go=new GameObject("Cathedral Center Carpet");go.transform.SetParent(b2.transform,false);go.transform.position=new Vector3((start+end)*.5f,y,z);go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=true;Undo.RegisterCreatedObjectUndo(go,"Place central velvet carpet");
 AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(b2.gameObject.scene);EditorSceneManager.SaveScene(b2.gameObject.scene);Selection.activeGameObject=go;
 return "Saved carpet: x="+start+" to "+end+", width="+width+", floor clearance=0.025; no collider; texture wrap="+texture.wrapMode;
}}
