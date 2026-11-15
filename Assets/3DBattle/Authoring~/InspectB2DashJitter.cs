using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class InspectB2DashJitter
{
 public static object Run()
 {
  const string folder="Assets/3DBattle/Boss2/BossAnim/Approach/";
  var g=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"B2_ApproachDash_Preview.prefab"));
  g.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);g.transform.localScale=Vector3.one;g.GetComponent<Animator>().enabled=false;
  try
  {
   var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(folder+"B2_ApproachDash.anim");
   string[] names={"Hips","LeftUpperLeg","LeftLowerLeg","LeftFoot","LeftToes","RightUpperLeg","RightLowerLeg","RightFoot","RightToes"};
   var bones=g.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
   var samples=new List<object>();var positions=new Dictionary<string,List<Vector3>>();var rotations=new Dictionary<string,List<Quaternion>>();
   foreach(string n in names){positions[n]=new List<Vector3>();rotations[n]=new List<Quaternion>();}
   for(int f=0;f<=89;f++)
   {
    clip.SampleAnimation(g,f/30f);
    foreach(string n in names){positions[n].Add(bones[n].position);rotations[n].Add(bones[n].localRotation);}
    samples.Add(new{frame=f,hip=Vec(bones["Hips"].position),leftFoot=Vec(bones["LeftFoot"].position),rightFoot=Vec(bones["RightFoot"].position),leftToe=Vec(bones["LeftToes"].position),rightToe=Vec(bones["RightToes"].position),rotation=names.ToDictionary(n=>n,n=>Vec(bones[n].localEulerAngles))});
   }
   var report=new List<object>();
   foreach(string n in names)
   {
    var angles=Enumerable.Range(1,89).Select(f=>new{f,angle=Quaternion.Angle(rotations[n][f-1],rotations[n][f])}).ToArray();
    var deviations=Enumerable.Range(1,88).Select(f=>new{f,angle=Quaternion.Angle(rotations[n][f],Quaternion.Slerp(rotations[n][f-1],rotations[n][f+1],.5f))}).ToArray();
    report.Add(new{bone=n,largestSteps=angles.OrderByDescending(x=>x.angle).Take(5).ToArray(),settleMeanStep=angles.Where(x=>x.f>=32&&x.f<=62).Average(x=>x.angle),settleMeanJitter=deviations.Where(x=>x.f>=32&&x.f<=62).Average(x=>x.angle)});
   }
   System.IO.File.WriteAllText("Assets/3DBattle/Authoring~/B2DashJitterBefore.json",Newtonsoft.Json.JsonConvert.SerializeObject(samples,Newtonsoft.Json.Formatting.Indented));
   return report;
  }
  finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 static float[] Vec(Vector3 v)=>new[]{v.x,v.y,v.z};
}
