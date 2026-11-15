using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class BuildB2SmokeRunOutPreview
{
 const string Folder="Assets/3DBattle/Boss2/PhasePreview/";
 const string Name="B2 - Smoke Run Out Preview";
 public static object Run()
 {
  if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  if(scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("3DBattleScene required");
  if(GameObject.Find(Name))throw new Exception("Preview already exists");
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<B2CombatController>();
  var originalController=boss.animator.runtimeAnimatorController;
  var sourceCloud=GameObject.Find("B2 - Smoke Flying Slash Preview").GetComponent<B2SmokeSlashPreview>();
  var sourceModel=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"B2_RetreatVanish_Preview.prefab").GetComponent<B2RetreatVanishPreview>().model;
  var root=new GameObject(Name);
  var driver=root.AddComponent<B2SmokeRunOutPreview>();
  var moving=new GameObject("Run Out Motion");moving.transform.SetParent(root.transform,false);
  var model=UnityEngine.Object.Instantiate(sourceModel,moving.transform);model.name="B2 Running Preview Model";
  foreach(var component in model.GetComponentsInChildren<MonoBehaviour>(true))UnityEngine.Object.DestroyImmediate(component);
  foreach(var component in model.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(component);
  foreach(var animator in model.GetComponentsInChildren<Animator>(true)){animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.fireEvents=false;animator.enabled=false;}
  model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;model.transform.localScale=boss.animator.transform.lossyScale;
  foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))skin.updateWhenOffscreen=true;
  driver.movingRoot=moving.transform;driver.model=model;driver.runClip=boss.runClip;driver.runSpeed=boss.runSpeed;
  driver.bodyRenderers=model.GetComponentsInChildren<Renderer>(true);
  var smoke=new GameObject("Floating Smoke Preview");smoke.transform.SetParent(root.transform,false);
  var cloud=smoke.AddComponent<B2SmokeSlashPreview>();cloud.playOnStart=false;cloud.enabled=false;
  cloud.cloudRoot=UnityEngine.Object.Instantiate(sourceCloud.cloudRoot.gameObject,smoke.transform).transform;
  cloud.cloudRoot.name="Yellow Gold Cloud - 16 Layers";cloud.cloudRoot.localPosition=Vector3.zero;cloud.cloudRoot.localRotation=Quaternion.identity;
  cloud.cloudLayers=cloud.cloudRoot.GetComponentsInChildren<ParticleSystem>(true);
  cloud.smokeStart=sourceCloud.smokeStart;cloud.smokeEnd=sourceCloud.smokeEnd;cloud.holdCloudShape=true;
  cloud.cloudHoldTime=sourceCloud.cloudHoldTime;cloud.cloudFloatDistance=sourceCloud.cloudFloatDistance;cloud.cloudFloatSpeed=sourceCloud.cloudFloatSpeed;
  driver.cloud=cloud;driver.runStart=cloud.cloudHoldTime;
  foreach(var p in cloud.cloudLayers){p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var main=p.main;main.playOnAwake=false;main.stopAction=ParticleSystemStopAction.None;}
  foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.tag="EditorOnly";
  driver.StopPreview();
  PrefabUtility.SaveAsPrefabAssetAndConnect(root,Folder+"B2_SmokeRunOut_Preview.prefab",InteractionMode.AutomatedAction);
  root.transform.SetPositionAndRotation(new Vector3(-18,0,-60),Quaternion.Euler(0,90,0));
  PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
  Undo.RegisterCreatedObjectUndo(root,"Create B2 smoke run out preview");
  EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
  Selection.activeGameObject=root;
  if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(root.transform.TransformPoint(new Vector3(0,3.5f,5)),root.transform.rotation*Quaternion.LookRotation(new Vector3(-18,-5,-22)),17);
  return new{root.name,driver.runStart,driver.runSeconds,driver.runSpeed,driver.Duration,cloudLayers=cloud.cloudLayers.Length,runClip=AssetDatabase.GetAssetPath(driver.runClip),bossControllerUnchanged=boss.animator.runtimeAnimatorController==originalController,saved=!scene.isDirty};
 }
 public static object Inspect()
 {
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<B2CombatController>();
  var retreat=GameObject.Find("B2 - Retreat Smoke Vanish Preview").GetComponent<B2RetreatVanishPreview>();
  var missing=AnimationUtility.GetCurveBindings(boss.runClip).Where(b=>!string.IsNullOrEmpty(b.path)&&retreat.model.transform.Find(b.path)==null).Select(b=>b.path).Distinct().ToArray();
  return new{run=AssetDatabase.GetAssetPath(boss.runClip),boss.runClip.length,boss.runClip.frameRate,boss.runSpeed,missing,model=retreat.model.name,scale=retreat.model.transform.localScale.ToString(),rotation=retreat.model.transform.localEulerAngles.ToString(),components=retreat.model.GetComponentsInChildren<MonoBehaviour>(true).Select(x=>x.GetType().Name).ToArray()};
 }
}
