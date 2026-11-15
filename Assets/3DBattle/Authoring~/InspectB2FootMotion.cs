using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
public static class InspectB2FootMotion
{
 public static object Run()
 {
  var a=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(x=>x.name.Contains("Heavy_Hammer")&&x.gameObject.activeInHierarchy);
  var model=UnityEngine.Object.Instantiate(a.gameObject);var result=new List<object>();
  try
  {
   model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);model.transform.localScale=Vector3.one;model.GetComponent<Animator>().enabled=false;
   var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
   string[] names={"B2_PowerSlash_Crisp","B2_DoubleCombo_Crisp","B2_WeaponCombo_Crisp","B2_Skill03_FromPlayer"};
   foreach(var name in names)
   {
    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss2/BossAnim/Crisp/"+name+".anim");var frames=new List<object>();
    int end=Mathf.RoundToInt(clip.length*30);
    for(int f=0;f<=end;f++)
    {
     clip.SampleAnimation(model,f/30f);Vector3 l=bones["LeftFoot"].position,r=bones["RightFoot"].position,h=bones["Hips"].position;
     frames.Add(new{frame=f,left=new[]{l.x,l.y,l.z},right=new[]{r.x,r.y,r.z},hips=new[]{h.x,h.y,h.z}});
    }
    result.Add(new{name,end,frames});
   }
   var json=Newtonsoft.Json.JsonConvert.SerializeObject(result);System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/B2FootMotion.json",json);
   return new{file="Assets/3DBattle/Authoring~/B2FootMotion.json",bones=bones.Keys.Where(n=>n.Contains("Foot")||n.Contains("Toes")).ToArray(),clips=names};
  }
  finally{UnityEngine.Object.DestroyImmediate(model);}
 }
}
