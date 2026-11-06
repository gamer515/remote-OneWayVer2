using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class RefineRunningKnightColors
{
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Stop play mode first");
  var root=GameObject.Find("Running Knight Preview");var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();
  var gear=root.GetComponentsInChildren<MeshRenderer>().Select(r=>new {name=r.name,position=r.transform.localPosition,rotation=r.transform.localRotation,scale=r.transform.localScale}).ToArray();
  var mesh=skin.sharedMesh;var vertices=mesh.vertices;var tris=mesh.triangles;
  var groups=new[]{new List<int>(),new List<int>(),new List<int>()};
  int wristCorrections=0,ankleCorrections=0;
  // Rest-pose anatomy, independent of animation skin-weight spill across the wrist/ankle.
  for(int i=0;i<tris.Length;i+=3)
  {
   var center=(vertices[tris[i]]+vertices[tris[i+1]]+vertices[tris[i+2]])/3f;
   int material=Mathf.Abs(center.x)>.605f?2:center.y<.19f?1:0;
   var corners=new[]{vertices[tris[i]],vertices[tris[i+1]],vertices[tris[i+2]]};
   // The wrist cuff ends at x ~ .60. This hand triangle shares two cuff-ring
   // vertices, so its centroid falls on the wrong side of the old cutoff.
   if(material==0 && corners.Min(p=>Mathf.Abs(p.x))>.58f && corners.Max(p=>Mathf.Abs(p.x))>.61f)
   {material=2;wristCorrections++;}
   // The ankle ring is slanted. A shin triangle reaches down into the ring,
   // but its upper vertex belongs to the leg, not to the boot.
   if(material==1 && corners.Max(p=>p.y)>.205f)
   {material=0;ankleCorrections++;}
   groups[material].AddRange(new[]{tris[i],tris[i+1],tris[i+2]});
  }
  Undo.RecordObject(mesh,"Refine wrist and boot material boundaries");
  for(int i=0;i<3;i++)mesh.SetTriangles(groups[i],i);
  EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssets();
  // Snapshot the user's adjustments as a reusable preset without touching the live transforms.
  string preset="Assets/3DBattle/3DBattleDebug/RunningKnight/UserAdjustedEquipment.json";
  System.IO.File.WriteAllText(preset,Newtonsoft.Json.JsonConvert.SerializeObject(gear.Select(g=>new{name=g.name,position=new[]{g.position.x,g.position.y,g.position.z},rotation=new[]{g.rotation.x,g.rotation.y,g.rotation.z,g.rotation.w},scale=new[]{g.scale.x,g.scale.y,g.scale.z}}),Newtonsoft.Json.Formatting.Indented));
  AssetDatabase.ImportAsset(preset);
  EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene);
  return new {wristCorrections,ankleCorrections,triangles=groups.Select(g=>g.Count/3).ToArray(),gearPreserved=gear.All(g=>{var t=root.GetComponentsInChildren<Transform>().First(x=>x.name==g.name);return t.localScale==g.scale&&t.localPosition==g.position&&t.localRotation==g.rotation;})};
 }
}
