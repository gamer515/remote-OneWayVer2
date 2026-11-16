using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class InspectB2Skill03Sword
{
 public static object Run()
 {
  var combo=UnityEngine.Object.FindObjectsByType<NewPlayerAttackCombo>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(c=>c.gameObject.activeInHierarchy);
  var a=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(x=>x.name.Contains("Heavy_Hammer")&&x.gameObject.activeInHierarchy);
  var p=UnityEngine.Object.Instantiate(combo.GetComponentInChildren<Animator>().gameObject);var b=UnityEngine.Object.Instantiate(a.gameObject);
  try
  {
   foreach(var g in new[]{p,b}){g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);g.transform.localScale=Vector3.one;foreach(var an in g.GetComponentsInChildren<Animator>())an.enabled=false;}
   var pc=combo.motions.attacks[4].clip;var bc=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss2/BossAnim/Crisp/B2_Skill03_FromPlayer.anim");
   var ps=p.GetComponentsInChildren<Transform>().Single(t=>t.name=="Sword");var bs=b.GetComponentsInChildren<Transform>().Single(t=>t.name=="B2 Holy Sword");
   int prefix=Mathf.RoundToInt((bc.length-pc.length)*30f);var frames=new List<object>();
   foreach(int f in new[]{0,1,3,6,10,16,20,26,36})
   {
    pc.SampleAnimation(p,f/30f);bc.SampleAnimation(b,(f+prefix)/30f);
    var pv=ps.forward;var bv=bs.forward;
    frames.Add(new{frame=f,playerAngle=Mathf.Asin(pv.y)*Mathf.Rad2Deg,bossAngle=Mathf.Asin(bv.y)*Mathf.Rad2Deg,playerDirection=new[]{pv.x,pv.y,pv.z},bossDirection=new[]{bv.x,bv.y,bv.z}});
   }
   return new{prefix,frames};
  }
  finally{UnityEngine.Object.DestroyImmediate(p);UnityEngine.Object.DestroyImmediate(b);}
 }
}
