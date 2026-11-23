using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class InspectB2VanishSources
{
 public static object Details()
 {
  var p=BattleCharacterAssetPaths.Load<GameObject>("Assets/3DBattle/Boss2/GoldenSlashVFX/B2GoldenSlash.prefab");
  var row=p.GetComponent<B2SlashTrailLayout>().center;
  return row.GetComponentsInChildren<ParticleSystem>(true).Select(s=>new{name=s.name,start3D=s.main.startSize3D,x=s.main.startSizeX.constant,y=s.main.startSizeY.constant,z=s.main.startSizeZ.constant,rotation=s.main.startRotation.constant,shape=s.shape.shapeType.ToString(),shapeOn=s.shape.enabled,meshBounds=s.GetComponent<ParticleSystemRenderer>().mesh.bounds.ToString(),alignment=s.GetComponent<ParticleSystemRenderer>().alignment.ToString(),color=s.main.startColor.color.ToString(),sizeCurve=s.sizeOverLifetime.size.curve?.keys.Select(k=>new{k.time,k.value}).ToArray(),material=EditorJsonUtility.ToJson(s.GetComponent<ParticleSystemRenderer>().sharedMaterial)}).ToArray();
 }
 public static object Run()
 {
  var p=BattleCharacterAssetPaths.Load<GameObject>("Assets/3DBattle/Boss2/GoldenSlashVFX/B2GoldenSlash.prefab");
  var b=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<BattleBoss>();
  return new{scene=b.gameObject.scene.path,scale=b.animator.transform.lossyScale.ToString(),components=b.animator.GetComponentsInChildren<MonoBehaviour>(true).Select(x=>x.GetType().Name).ToArray(),particles=p.GetComponentsInChildren<ParticleSystem>(true).Select(s=>new{name=s.name,path=AnimationUtility.CalculateTransformPath(s.transform,p.transform),position=s.transform.localPosition.ToString(),rotation=s.transform.localEulerAngles.ToString(),scale=s.transform.localScale.ToString(),main=new{s.main.duration,s.main.loop,s.main.startLifetime.constant,speed=s.main.startSpeed.constant,size=s.main.startSize.constant,space=s.main.simulationSpace.ToString()},emission=new{s.emission.enabled,rate=s.emission.rateOverTime.constant,bursts=s.emission.burstCount},renderer=new{mode=s.GetComponent<ParticleSystemRenderer>().renderMode.ToString(),mesh=s.GetComponent<ParticleSystemRenderer>().mesh?.name,material=AssetDatabase.GetAssetPath(s.GetComponent<ParticleSystemRenderer>().sharedMaterial)}}).ToArray(),renderers=p.GetComponentsInChildren<Renderer>(true).Select(r=>new{name=r.name,type=r.GetType().Name,materials=r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray()}).ToArray()};
 }
}
