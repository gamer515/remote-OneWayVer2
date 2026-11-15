using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class FinishB2RetreatPreview
{
 public static object Run()
 {
  var root=GameObject.Find("B2 - Retreat Smoke Vanish Preview");var p=root.GetComponent<B2RetreatVanishPreview>();p.StopPreview();
  root.transform.SetPositionAndRotation(new Vector3(-18,0,0),Quaternion.Euler(0,90,0));
  var source=root.transform.position;var reports=new System.Collections.Generic.List<object>();
  foreach(float t in new[]{0f,1.1f,1.45f,1.65f,2.7f,3.65f,0f})
  {
   p.SamplePreview(t);reports.Add(new{time=t,visible=p.bodyRenderers.Count(r=>r.enabled),particles=p.smokeLayers.Sum(s=>s.particleCount),distance=Vector3.Distance(p.movingRoot.position,source),temporaryMaterials=p.bodyRenderers.SelectMany(r=>r.sharedMaterials).Count(m=>m&&(m.hideFlags&HideFlags.DontSave)!=0)});
  }
  p.StopPreview();
  if(p.bodyRenderers.Any(r=>!r.enabled)||p.smokeLayers.Any(s=>s.particleCount!=0)||p.movingRoot.localPosition.sqrMagnitude>.0001)throw new Exception("Reset failed");
  if(p.bodyRenderers.SelectMany(r=>r.sharedMaterials).Any(m=>!AssetDatabase.Contains(m)))throw new Exception("Transient material left on preview");
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<BattleBoss>();
  Selection.activeGameObject=root;
  if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(root.transform.position+new Vector3(-1.5f,3.4f,0),Quaternion.Euler(12,225,0),8);
  EditorSceneManager.MarkSceneDirty(root.scene);bool saved=EditorSceneManager.SaveScene(root.scene);AssetDatabase.SaveAssets();
  return new{saved,root=root.name,position=root.transform.position.ToString(),reports,smokeCount=p.smokeLayers.Length,uniqueSizes=p.smokeLayers.Select(s=>s.transform.localScale.x).Distinct().Count(),bossController=boss.animator.runtimeAnimatorController.name,previewCombatScripts=root.GetComponentsInChildren<MonoBehaviour>(true).Select(c=>c.GetType().Name).ToArray(),colliders=root.GetComponentsInChildren<Collider>(true).Length};
 }
}
