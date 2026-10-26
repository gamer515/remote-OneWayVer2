using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class MeshySceneFix {
 public static string Apply(){
 var g=GameObject.Find("Meshy_AI_boss_character_model_biped_Meshy_Merged_Animations");
 if(g==null || g.scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Wrong scene");
 var r=g.GetComponentInChildren<SkinnedMeshRenderer>();var arm=g.transform.Find("target_character");
 Undo.RegisterFullObjectHierarchyUndo(g,"Repair Meshy bind pose");
 arm.localScale=new Vector3(10000,10000,10000);
 var targets=r.bones.Select((b,i)=>new{bone=b,world=r.transform.localToWorldMatrix*r.sharedMesh.bindposes[i].inverse}).ToArray();
 foreach(var t in g.GetComponentsInChildren<Transform>()){
 var item=targets.FirstOrDefault(x=>x.bone==t);if(item==null)continue;
 var local=t.parent.worldToLocalMatrix*item.world;t.localPosition=local.GetColumn(3);t.localRotation=local.rotation;t.localScale=local.lossyScale;
 }
 r.updateWhenOffscreen=true;
 var mesh=new Mesh();r.BakeMesh(mesh,true);
 var b=mesh.bounds;var bottom=r.transform.TransformPoint(b.center-new Vector3(0,b.extents.y,0));
 // Baked vertices are in renderer space; find the actual lowest point after its axis rotation.
 float minY=mesh.vertices.Min(v=>r.transform.TransformPoint(v).y);
 g.transform.position-=Vector3.up*minY;
 foreach(var t in g.GetComponentsInChildren<Transform>())PrefabUtility.RecordPrefabInstancePropertyModifications(t);
 PrefabUtility.RecordPrefabInstancePropertyModifications(r);
 EditorSceneManager.MarkSceneDirty(g.scene);EditorSceneManager.SaveScene(g.scene);
 UnityEngine.Object.DestroyImmediate(mesh);
 Selection.activeGameObject=g;if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.FrameSelected();
 return "Fixed scene pose; "+r.bounds;
 }
}
