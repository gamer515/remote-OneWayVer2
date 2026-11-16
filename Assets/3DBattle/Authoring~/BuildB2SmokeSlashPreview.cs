using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class BuildB2SmokeSlashPreview
{
 const string Folder="Assets/3DBattle/Boss2/PhasePreview/";
 public static object Run()
 {
  if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
  var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
  if(scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("3DBattleScene required");
  const string name="B2 - Smoke Flying Slash Preview";
  if(GameObject.Find(name))throw new Exception("Preview already exists");
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<BattleBoss>();
  var fx=boss.animator.GetComponent<B2SwordSlash>();
  var source=GameObject.Find("B2 - Retreat Smoke Vanish Preview").GetComponent<B2RetreatVanishPreview>();
  var root=new GameObject(name);
  var preview=root.AddComponent<B2SmokeSlashPreview>();
  var cloud=UnityEngine.Object.Instantiate(source.smokeLayers[0].transform.parent.gameObject,root.transform);
  cloud.name="Stationary Yellow Gold Cloud - 16 Layers";
  cloud.transform.localPosition=Vector3.zero;cloud.transform.localRotation=Quaternion.identity;cloud.transform.localScale=Vector3.one;
  preview.cloudRoot=cloud.transform;preview.cloudLayers=cloud.GetComponentsInChildren<ParticleSystem>(true);
  var slash=(GameObject)PrefabUtility.InstantiatePrefab(fx.slashPrefab,root.transform);
  PrefabUtility.UnpackPrefabInstance(slash,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
  slash.name="Flying Golden Slash";
  var layout=slash.GetComponentInChildren<B2SlashTrailLayout>(true);
  if(layout)layout.ApplyLengths(fx.topTrailLength,fx.upperMiddleTrailLength,fx.centerTrailLength,fx.lowerMiddleTrailLength,fx.bottomTrailLength);
  preview.slashRoot=slash.transform;
  preview.slashLayers=slash.GetComponentsInChildren<ParticleSystem>(false);
  preview.smokeStart=source.smokeStart;preview.smokeEnd=source.smokeEnd;
  preview.travelSpeed=fx.travelSpeed;preview.launchDelay=fx.launchDelay;preview.flightSeconds=fx.lifetime;
  preview.slashOrigin=new Vector3(0,fx.BossHeight*.5f,0);preview.slashScale=fx.BossHeight*fx.heightMultiplier;
  foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.tag="EditorOnly";
  foreach(var c in root.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(c);
  int seed=701;
  foreach(var p in root.GetComponentsInChildren<ParticleSystem>(true))
  {
   p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);p.useAutoRandomSeed=false;p.randomSeed=(uint)seed++;
   var main=p.main;main.playOnAwake=false;main.stopAction=ParticleSystemStopAction.None;
  }
  preview.StopPreview();
  PrefabUtility.SaveAsPrefabAssetAndConnect(root,Folder+"B2_SmokeSlash_Preview.prefab",InteractionMode.AutomatedAction);
  root.transform.SetPositionAndRotation(new Vector3(-18,0,-36),Quaternion.Euler(0,90,0));
  PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
  Undo.RegisterCreatedObjectUndo(root,"Create smoke flying slash preview");
  EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
  Selection.activeGameObject=root;
  if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(root.transform.TransformPoint(new Vector3(0,4,10)),root.transform.rotation*Quaternion.LookRotation(new Vector3(-33,-8,-10)),23);
  return new {root.name,clouds=preview.cloudLayers.Length,slashLayers=preview.slashLayers.Length,preview.Duration,preview.travelSpeed,preview.launchDelay,preview.slashScale,sceneSaved=!scene.isDirty};
 }
 public static object Focus()
 {
  var root=GameObject.Find("B2 - Smoke Flying Slash Preview");
  var preview=root.GetComponent<B2SmokeSlashPreview>();
  preview.StopPreview();Selection.activeGameObject=root;
  if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(root.transform.TransformPoint(new Vector3(0,4,10)),root.transform.rotation*Quaternion.LookRotation(new Vector3(-33,-8,-10)),23);
  return new {selected=root.name,scene=root.scene.path,dirty=root.scene.isDirty,prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root),preview.Duration};
 }
 public static object HoldCloud()
 {
  if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
  var sceneRoot=GameObject.Find("B2 - Smoke Flying Slash Preview");
  if(!sceneRoot||sceneRoot.scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Expected preview scene is not open");
  string path=Folder+"B2_SmokeSlash_Preview.prefab";
  var root=PrefabUtility.LoadPrefabContents(path);
  object report;
  try
  {
   var p=root.GetComponent<B2SmokeSlashPreview>();
   p.holdCloudShape=false;p.SamplePreview(3.6f);
   var reference=p.cloudLayers.Select(ps=>{var particles=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(particles);return particles;}).ToArray();
   p.holdCloudShape=true;p.cloudHoldTime=3.6f;p.cloudFloatDistance=.5f;p.cloudFloatSpeed=.7f;
   float maxDrift=0;int movingParticles=0;
   foreach(float time in new[]{3.6f,4.5f,5.8f,6.2f})
   {
    p.SamplePreview(time);
    for(int i=0;i<p.cloudLayers.Length;i++)
    {
     var ps=p.cloudLayers[i];var particles=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(particles);
     if(particles.Length!=reference[i].Length)throw new Exception("Cloud particle count changed");
     for(int j=0;j<particles.Length;j++)
     {
      float drift=ps.transform.TransformVector(particles[j].position-reference[i][j].position).magnitude;
      maxDrift=Mathf.Max(maxDrift,drift);if(drift>.001f)movingParticles++;
      if(drift>p.cloudFloatDistance*1.3f)throw new Exception("Cloud drift exceeded bounded area");
      if(Mathf.Abs(particles[j].remainingLifetime-reference[i][j].remainingLifetime)>.0001f||Vector3.Distance(particles[j].GetCurrentSize3D(ps),reference[i][j].GetCurrentSize3D(ps))>.0001f)throw new Exception("Cloud size or age changed after hold");
     }
    }
   }
   if(movingParticles==0)throw new Exception("Cloud is still frozen");
   p.SamplePreview(3.6f);
   for(int i=0;i<p.cloudLayers.Length;i++)
   {
    var particles=new ParticleSystem.Particle[p.cloudLayers[i].particleCount];p.cloudLayers[i].GetParticles(particles);
    for(int j=0;j<particles.Length;j++)if(Vector3.Distance(particles[j].position,reference[i][j].position)>.0001f)throw new Exception("Scrubbing accumulated drift");
   }
   p.SamplePreview(2f);float start=p.slashRoot.localPosition.z;
   p.SamplePreview(2.25f);float end=p.slashRoot.localPosition.z;
   if(end<=start)throw new Exception("Slash did not keep moving");
   p.StopPreview();
   if(p.cloudLayers.Any(ps=>ps.particleCount!=0))throw new Exception("Cloud reset failed");
   EditorUtility.SetDirty(p);PrefabUtility.SaveAsPrefabAsset(root,path);
   report=new {holdAt=3.6f,verifiedTimes=new[]{3.6f,4.5f,5.8f,6.2f},particles=reference.Sum(a=>a.Length),maxDrift,movingParticles,sizeAndAgeMaintained=true,noAccumulatedDrift=true,slashContinues=true,resetPassed=true};
  }
  finally{PrefabUtility.UnloadPrefabContents(root);}
  var preview=sceneRoot.GetComponent<B2SmokeSlashPreview>();
  Undo.RecordObject(preview,"Float smoke while holding its size");preview.holdCloudShape=true;preview.cloudHoldTime=3.6f;preview.cloudFloatDistance=.5f;preview.cloudFloatSpeed=.7f;preview.StopPreview();
  PrefabUtility.RecordPrefabInstancePropertyModifications(preview);EditorUtility.SetDirty(preview);
  EditorSceneManager.MarkSceneDirty(sceneRoot.scene);EditorSceneManager.SaveScene(sceneRoot.scene);AssetDatabase.SaveAssets();
  Selection.activeGameObject=sceneRoot;
  return report;
 }
 public static object Inspect()
 {
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<BattleBoss>();
  var fx=boss.animator.GetComponent<B2SwordSlash>();
  var preview=GameObject.Find("B2 - Retreat Smoke Vanish Preview").GetComponent<B2RetreatVanishPreview>();
  return new{scene=boss.gameObject.scene.path,fx.BossHeight,fx.heightMultiplier,fx.travelSpeed,fx.launchDelay,fx.lifetime,fx.topTrailLength,fx.upperMiddleTrailLength,fx.centerTrailLength,fx.lowerMiddleTrailLength,fx.bottomTrailLength,prefab=AssetDatabase.GetAssetPath(fx.slashPrefab),clouds=preview.smokeLayers.Length,preview.smokeStart,preview.smokeEnd,previewDuration=preview.Duration};
 }
}
