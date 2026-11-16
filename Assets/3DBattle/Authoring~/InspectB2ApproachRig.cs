using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class InspectB2ApproachRig
{
 public static object Run()
 {
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss");
  var a=boss.GetComponentsInChildren<Animator>(true).Single(x=>x.name.Contains("Heavy_Hammer"));
  var copy=UnityEngine.Object.Instantiate(a.gameObject);
  copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);copy.transform.localScale=Vector3.one;
  try
  {
   foreach(var b in copy.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
   copy.GetComponent<Animator>().enabled=false;
   var skin=copy.GetComponentsInChildren<SkinnedMeshRenderer>().First();
   var bones=skin.bones.Select((t,i)=>new{name=t.name,parent=t.parent.name,pos=(copy.transform.worldToLocalMatrix*skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[i].inverse).GetColumn(3),rot=(copy.transform.worldToLocalMatrix*skin.transform.localToWorldMatrix*skin.sharedMesh.bindposes[i].inverse).rotation.eulerAngles}).ToArray();
   var sword=copy.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="B2 Holy Sword");
   var hand=copy.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="RightHand");
   var source=AssetDatabase.FindAssets("t:Model",new[]{"Assets/3DBattle/Boss2/BossAnim"}).Select(AssetDatabase.GUIDToAssetPath).Single(p=>p.Contains("01a1bf98"));
   var clip=AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
   return Newtonsoft.Json.JsonConvert.SerializeObject(new{source,clip=clip.name,length=clip.length,fps=clip.frameRate,bossScale=boss.transform.lossyScale.ToString(),bones=bones.Select(b=>new{b.name,b.parent,pos=b.pos.ToString("F4"),rot=b.rot.ToString("F2")}),swordMountRotation=(Quaternion.Inverse(hand.rotation)*sword.rotation).eulerAngles.ToString(),swordScale=sword.lossyScale.ToString("F3"),swordBounds=sword.GetComponent<MeshFilter>().sharedMesh.bounds.ToString("F5")});
  }
  finally{UnityEngine.Object.DestroyImmediate(copy);}
 }
}
