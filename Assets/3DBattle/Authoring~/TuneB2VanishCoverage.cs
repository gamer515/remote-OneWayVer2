using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class TuneB2VanishCoverage
{
 public static object Run()
 {
  var root=PrefabUtility.LoadPrefabContents("Assets/3DBattle/Boss2/PhasePreview/B2_RetreatVanish_Preview.prefab");
  try
  {
   var p=root.GetComponent<B2RetreatVanishPreview>();
   for(int i=0;i<p.smokeLayers.Length;i++)
   {
    var s=p.smokeLayers[i];s.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
    var main=s.main;main.startSizeX=i%2==0?13:1;main.startSizeY=i%2==0?10:1;main.startSizeZ=i%2==0?1:1.3f;
    var q=Quaternion.Euler(0,i*45+12,i*47-35);
    s.transform.localPosition=new Vector3(0,3.5f+(i%3-1)*.65f,0)+Quaternion.Euler(0,i*45+12,0)*Vector3.forward*1.8f;
    s.transform.localRotation=q*(i%2==0?Quaternion.identity:Quaternion.Euler(0,0,90));
   }
   p.StopPreview();PrefabUtility.SaveAsPrefabAsset(root,"Assets/3DBattle/Boss2/PhasePreview/B2_RetreatVanish_Preview.prefab");
   var existing=GameObject.Find("B2 - Retreat Smoke Vanish Preview");var current=existing.GetComponent<B2RetreatVanishPreview>();current.StopPreview();
   for(int i=0;i<p.smokeLayers.Length;i++)
   {
    var s=current.smokeLayers[i];var source=p.smokeLayers[i];var main=s.main;main.startSizeX=source.main.startSizeX;main.startSizeY=source.main.startSizeY;main.startSizeZ=source.main.startSizeZ;
    s.transform.localPosition=source.transform.localPosition;s.transform.localRotation=source.transform.localRotation;
   }
   EditorSceneManager.MarkSceneDirty(existing.scene);EditorSceneManager.SaveScene(existing.scene);return "Coverage widened";
  }
  finally{PrefabUtility.UnloadPrefabContents(root);}
 }
}
