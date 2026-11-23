using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class ReplaceB2VanishWithDust
{
 const string Folder="Assets/3DBattle/Boss2/PhasePreview/";
 const string Preview="B2 - Retreat Smoke Vanish Preview";
 const string Source="Assets/3DBattle/Effects/Dust/QuarryDust_Burst.prefab";
 static string Hash(string path){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));}
 public static object Focus()
 {
  var root=GameObject.Find(Preview);Selection.activeGameObject=root;
  if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(root.transform.TransformPoint(new Vector3(0,3.7f,-3)),Quaternion.Euler(12,225,0),11);
  var p=root.GetComponent<B2RetreatVanishPreview>();
  return new{sceneDirty=root.scene.isDirty,layers=p.smokeLayers.Length,smokeFollowsBoss=p.smokeLayers.Any(s=>s.transform.IsChildOf(p.movingRoot)),p.Duration};
 }
 public static object Inspect()
 {
  var dust=BattleCharacterAssetPaths.Load<GameObject>(Source);
  var p=GameObject.Find(Preview).GetComponent<B2RetreatVanishPreview>();
  return new{scene=p.gameObject.scene.path,position=p.transform.position.ToString(),p.smokeStart,p.smokeEnd,p.hideAt,scale=dust.transform.localScale.ToString(),B1References=UnityEngine.Object.FindObjectsByType<BossJumpMotionDebug>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(j=>AssetDatabase.GetAssetPath(j.jumpDustPrefab)).ToArray(),systems=dust.GetComponentsInChildren<ParticleSystem>(true).Select(s=>new{s.name,sizeMin=s.main.startSize.constantMin,sizeMax=s.main.startSize.constantMax,speedMin=s.main.startSpeed.constantMin,speedMax=s.main.startSpeed.constantMax,color=s.main.startColor.colorMax.ToString(),material=AssetDatabase.GetAssetPath(s.GetComponent<ParticleSystemRenderer>().sharedMaterial)}).ToArray()};
 }
 static void Replace(B2RetreatVanishPreview preview,GameObject dust,Material material)
 {
  preview.StopPreview();
  preview.smokeStart=.1f;preview.standSeconds=1.1f;
  preview.retreatCycles=2;preview.retreatSeconds=preview.backwardClip.length*2;preview.retreatDistance=6;
  preview.hideAt=3.9f;preview.bodyFadeSeconds=.35f;preview.smokeEnd=5.6f;preview.repeatPause=.9f;
  // These are the isolated preview's old effect objects, never the shared source prefab.
  var old=preview.smokeLayers.Where(p=>p).Select(p=>p.transform.parent).Distinct().ToArray();
  preview.smokeLayers=Array.Empty<ParticleSystem>();
  foreach(var parent in old)
  {
   if(parent==preview.movingRoot||parent==preview.transform||!parent.IsChildOf(preview.transform))throw new Exception("Unexpected effect parent");
   UnityEngine.Object.DestroyImmediate(parent.gameObject);
  }
  // The cloud stays behind the starting pose while only the model walks into it.
  var holder=new GameObject("Vanish Dust - 16 Yellow Gold Clouds");holder.transform.SetParent(preview.transform,false);
  holder.transform.localPosition=Vector3.back*preview.retreatDistance;
  var sources=dust.GetComponentsInChildren<ParticleSystem>(true).Where(s=>s.name=="Ground Spread"||s.name=="Rising Plume").ToArray();
  if(sources.Length!=2)throw new Exception("Expected B1 dust cloud layers");
  float[] sizes={1.85f,1.65f,2.18f,1.9f,2.12f,1.52f,1.78f,2.06f,2.25f,1.96f,1.72f,2.02f,2.3f,1.82f,2.15f,1.6f};
  var systems=new List<ParticleSystem>();
  for(int i=0;i<16;i++)
  {
   var ps=UnityEngine.Object.Instantiate(sources[i%2],holder.transform);ps.name="Dust "+(i+1).ToString("00")+" - Yellow Gold";ps.gameObject.SetActive(true);
   ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
   int row=i/4,column=i%4;
   ps.transform.localPosition=new Vector3(new[]{-2.25f,-.75f,.75f,2.25f}[column],.45f+row*1.6f+(column%2)*.18f,column%2==0?-.9f:.9f);
   ps.transform.localRotation=Quaternion.Euler(-90,i*47,0);ps.transform.localScale=Vector3.one*sizes[i];
   ps.useAutoRandomSeed=false;ps.randomSeed=(uint)(8251+i*137);
   var main=ps.main;main.loop=false;main.playOnAwake=false;main.duration=Mathf.Max(.1f,preview.smokeEnd-preview.smokeStart);main.startDelay=0;
   main.stopAction=ParticleSystemStopAction.None;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
   main.startLifetime=main.duration;main.startSpeed=new ParticleSystem.MinMaxCurve(.2f,.55f);main.maxParticles=50;
   main.startColor=new ParticleSystem.MinMaxGradient(new Color(1,.97f,.78f,.85f),new Color(1,1,.95f,1));
   var emission=ps.emission;emission.rateOverTime=0;emission.rateOverDistance=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)(i%2==0?22:18))});
   var color=ps.colorOverLifetime;color.enabled=true;
   var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.98f,.9f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.85f,.12f),new GradientAlphaKey(.85f,.65f),new GradientAlphaKey(0,1)});color.color=gradient;
   var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
   systems.Add(ps);
  }
  preview.smokeLayers=systems.ToArray();
  foreach(var t in holder.GetComponentsInChildren<Transform>(true))t.gameObject.tag="EditorOnly";
  preview.StopPreview();EditorUtility.SetDirty(preview);
 }
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var sceneObject=GameObject.Find(Preview);if(!sceneObject||sceneObject.scene.path!="Assets/Scenes/3DBattleScene.unity")throw new Exception("Preview not found in expected scene");
  string sourceMat="Assets/3DBattle/Effects/Dust/QuarryDust.mat";
  string before=Hash(Source),beforeMat=Hash(sourceMat);
  var material=BattleCharacterAssetPaths.Load<Material>(Folder+"B2_VanishDust_PaleGold.mat");
  var copy=new Material(BattleCharacterAssetPaths.Load<Material>(sourceMat));copy.name="B2 Vanish Dust Yellow Gold";
  copy.SetColor("_BaseColor",new Color(1,.92f,.38f,1));copy.SetColor("_Color",new Color(1,.92f,.38f,1));
  if(material){EditorUtility.CopySerialized(copy,material);UnityEngine.Object.DestroyImmediate(copy);EditorUtility.SetDirty(material);}
  else{material=copy;AssetDatabase.CreateAsset(material,Folder+"B2_VanishDust_PaleGold.mat");}
  var dust=BattleCharacterAssetPaths.Load<GameObject>(Source);
  string path=Folder+"B2_RetreatVanish_Preview.prefab";var prefab=PrefabUtility.LoadPrefabContents(path);
  try{Replace(prefab.GetComponent<B2RetreatVanishPreview>(),dust,material);PrefabUtility.SaveAsPrefabAsset(prefab,path);}
  finally{PrefabUtility.UnloadPrefabContents(prefab);}
  Replace(sceneObject.GetComponent<B2RetreatVanishPreview>(),dust,material);
  Selection.activeGameObject=sceneObject;AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(sceneObject.scene);bool saved=EditorSceneManager.SaveScene(sceneObject.scene);
  if(before!=Hash(Source)||beforeMat!=Hash(sourceMat))throw new Exception("B1 source unexpectedly changed");
  return new{saved,source=Source,material=AssetDatabase.GetAssetPath(material),layers=16,sourceB1Unchanged=true,position=sceneObject.transform.position.ToString(),cloudDistanceBehind=6,retreatStarts=1.1f,hideAt=3.9f,smokeEnds=5.6f};
 }
}
