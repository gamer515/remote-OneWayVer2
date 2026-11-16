using System;using System.Linq;using System.IO;using UnityEngine;using UnityEditor;using UnityEditor.SceneManagement;using OneWay.Battle3D;
public static class PlaceCathedralAltar{
static Bounds BoundsOf(GameObject g){var rs=g.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
static GameObject Model(string token,Transform parent,string name,float widthOrHeight,bool height){
 var path=Directory.GetFiles("Assets/3DBattle/Boss2/Cathedral","*"+token+"*.fbx",SearchOption.AllDirectories).Single().Replace('\\','/');var holder=new GameObject(name);holder.transform.SetParent(parent,false);
 var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));model.transform.SetParent(holder.transform,false);
 var b=BoundsOf(holder);holder.transform.localScale*=widthOrHeight/(height?b.size.y:b.size.x);holder.transform.rotation=Quaternion.Euler(0,90,0);
 string matPath="Assets/3DBattle/Boss2/Cathedral/StageAssets/"+name+".mat";var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Path.ChangeExtension(path,".png")));mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Metallic",.1f);mat.SetFloat("_Smoothness",.35f);AssetDatabase.CreateAsset(mat,matPath);
 foreach(var r in holder.GetComponentsInChildren<Renderer>()){r.sharedMaterial=mat;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
 return holder;
}
public static string Run(){if(Application.isPlaying)throw new Exception("Edit mode required");var stage=UnityEngine.Object.FindObjectsByType<BattleStage>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(s=>s.name=="B2");if(stage.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Wrong scene");if(stage.transform.Find("Cathedral Altar and Steps")!=null)throw new Exception("Already placed");
 var root=new GameObject("Cathedral Altar and Steps");root.transform.SetParent(stage.transform,false);Undo.RegisterCreatedObjectUndo(root,"Place cathedral altar and stairs");
 var stairs=Model("stone_dais",root.transform,"Cathedral Steps",24,false);var b=BoundsOf(stairs);stairs.transform.position+=new Vector3(-49-b.center.x,-b.min.y,-b.center.z);b=BoundsOf(stairs);
 var altar=Model("altar_shrine",root.transform,"Cathedral Altar",20,true);var a=BoundsOf(altar);altar.transform.position+=new Vector3(-53-a.center.x,b.max.y-a.min.y,-a.center.z);
 // Find the actual top beneath the altar, rather than assuming the bounding-box top is level.
 float top=float.NegativeInfinity;
 foreach(var filter in stairs.GetComponentsInChildren<MeshFilter>()){var c=filter.gameObject.AddComponent<MeshCollider>();c.sharedMesh=filter.sharedMesh;try{RaycastHit hit;if(c.Raycast(new Ray(new Vector3(-53,100,0),Vector3.down),out hit,200))top=Mathf.Max(top,hit.point.y);}finally{UnityEngine.Object.DestroyImmediate(c);}}
 if(float.IsNegativeInfinity(top))throw new Exception("No platform under altar");a=BoundsOf(altar);altar.transform.position+=Vector3.up*(top-a.min.y);a=BoundsOf(altar);
 var boundary=stage.boundary.walls.Single(w=>w.transform.position.x<-20).bounds;
 if(b.max.x>=boundary.min.x||a.max.x>=boundary.min.x)throw new Exception("Decorations overlap arena boundary");
 if(Mathf.Abs(a.min.y-top)>.001f)throw new Exception("Altar is not on platform");
 AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);EditorSceneManager.SaveScene(stage.gameObject.scene);Selection.activeGameObject=root;
 return "PASS: stairs="+b+" altar="+a+" platformY="+top+" boundary outerX="+boundary.min.x+"; both entirely beyond boundary; saved.";
}}
