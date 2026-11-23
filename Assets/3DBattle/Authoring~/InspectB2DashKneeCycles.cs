using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class InspectB2DashKneeCycles
{
 public static object Run()
 {
  const string folder="Assets/3DBattle/Boss2/BossAnim/Approach/";
  var g=UnityEngine.Object.Instantiate(BattleCharacterAssetPaths.Load<GameObject>(folder+"B2_ApproachDash_Preview.prefab"));
  g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);g.transform.localScale=Vector3.one;g.GetComponent<Animator>().enabled=false;
  try
  {
   var clip=BattleCharacterAssetPaths.Load<AnimationClip>(folder+"B2_ApproachDash_InPlace.anim");
   var b=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
   var samples=new List<object>();
   for(int f=0;f<=89;f++)
   {
    clip.SampleAnimation(g,f/30f);
    var h=b["Hips"];
    samples.Add(new{frame=f,hipY=h.position.y,leftBend=Bend(b,"Left"),rightBend=Bend(b,"Right"),leftKnee=(b["LeftLowerLeg"].position-h.position).ToString("F4"),rightKnee=(b["RightLowerLeg"].position-h.position).ToString("F4"),leftFoot=b["LeftFoot"].position.ToString("F4"),rightFoot=b["RightFoot"].position.ToString("F4")});
   }
   System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/B2DashKneeCycles.json",Newtonsoft.Json.JsonConvert.SerializeObject(samples,Newtonsoft.Json.Formatting.Indented));
   return samples.Where((s,i)=>i%3==0||i==89).ToArray();
  }
  finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 static float Bend(Dictionary<string,Transform>b,string s)=>180-Vector3.Angle(b[s+"UpperLeg"].position-b[s+"LowerLeg"].position,b[s+"Foot"].position-b[s+"LowerLeg"].position);
}
