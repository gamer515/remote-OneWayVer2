using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class PlaceArenaRocks {
 public static string Run(){
 if(Application.isPlaying)throw new Exception("Edit mode required");
 var background=GameObject.Find("BackgroundCylinder");if(background==null)throw new Exception("Missing background");
 var root=new GameObject("Arena Outer Rocks");Undo.RegisterCreatedObjectUndo(root,"Place outer arena rocks");root.transform.SetParent(background.transform.parent,false);root.transform.position=Vector3.zero;root.transform.rotation=Quaternion.identity;root.transform.localScale=Vector3.one;
 string[] names={"Rock Type1 02","Rock Type6 01","Rock Type2 03","Rock Type4 01","Rock Type4 03","Rock Type5 03","Rock Type5 02","Rock Type6 02","Rock Type4 04"};
 int count=0;float nearest=999,farthest=0;
 try {for(int group=0;group<9;group++){
 float angle=(group*40+13)*Mathf.Deg2Rad;var radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var tangent=new Vector3(-radial.z,0,radial.x);float radius=66+(group%3)*3;
 for(int j=0;j<3;j++){
 string name=names[(group+j*3)%9];var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/BrokenVector/LowPolyRockPack/Prefabs/"+name+".prefab");if(asset==null)throw new Exception(name);
 var rock=(GameObject)PrefabUtility.InstantiatePrefab(asset,root.transform);Undo.RegisterCreatedObjectUndo(rock,"Place rock");rock.name=name+" - Group "+(group+1);
 rock.transform.rotation=Quaternion.Euler(0,group*67+j*113,0);
 var renderers=rock.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
 float width=j==0?13+(group%3)*2:j==1?7:5;rock.transform.localScale*=width/Mathf.Max(bounds.size.x,bounds.size.z);
 bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
 Vector3 center=radial*(radius+(j==0?0:5))+tangent*(j==0?0:j==1?-10:9);
 rock.transform.position+=new Vector3(center.x-bounds.center.x,-.15f-bounds.min.y,center.z-bounds.center.z);
 bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
 if(bounds.min.x<33 && bounds.max.x> -33 && bounds.min.z<33 && bounds.max.z> -33)throw new Exception("Rock overlaps battle area");
 foreach(float x in new[]{bounds.min.x,bounds.max.x})foreach(float z in new[]{bounds.min.z,bounds.max.z}){float d=new Vector2(x,z).magnitude;farthest=Mathf.Max(farthest,d);if(d>96)throw new Exception("Rock outside background");}
 nearest=Mathf.Min(nearest,new Vector2(bounds.center.x,bounds.center.z).magnitude);PrefabUtility.RecordPrefabInstancePropertyModifications(rock.transform);count++;
 }}EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);Selection.activeGameObject=root;return "Placed "+count+" rocks in 9 clusters; all nine prefabs used three times; center radius min="+nearest+"; outer bound max="+farthest+"; combat square clear.";}
 catch{UnityEngine.Object.DestroyImmediate(root);throw;}
 }
}
