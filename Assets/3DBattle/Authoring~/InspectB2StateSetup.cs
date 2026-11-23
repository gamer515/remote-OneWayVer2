using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using OneWay.Battle3D;
public static class InspectB2StateSetup
{
 public static object Run()
 {
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<BattleBoss>();
  var animator=boss.animator;
  var battle=boss.GetComponentInParent<Battle3DSceneController>();
  var paths=AssetDatabase.FindAssets("t:Model",new[]{"Assets/3DBattle/Boss2/BossAnim"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>new[]{"Idle_02","Axe_Stance","run_fast_3_inplace","01a1a012","01a1bf9f"}.Any(p.Contains)).ToArray();
  var sources=paths.Select(p=>{
   var model=BattleCharacterAssetPaths.Load<GameObject>(p);var clip=BattleCharacterAssetPaths.LoadAll(p).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
   return new{path=p,clip=clip.name,clip.length,clip.frameRate,bones=model.GetComponentsInChildren<SkinnedMeshRenderer>().First().bones.Select(t=>t.name).ToArray()};
  }).ToArray();
  var controller=animator.runtimeAnimatorController as AnimatorController;
  return new{boss=EditorJsonUtility.ToJson(boss),components=boss.GetComponents<MonoBehaviour>().Select(c=>new{type=c.GetType().Name,c.enabled,json=EditorJsonUtility.ToJson(c)}).ToArray(),animator=EditorJsonUtility.ToJson(animator),states=controller.layers[0].stateMachine.states.Select(s=>new{s.state.name,clip=AssetDatabase.GetAssetPath(s.state.motion),events=s.state.motion is AnimationClip a?AnimationUtility.GetAnimationEvents(a).Select(e=>new{e.time,e.functionName}).ToArray():null}).ToArray(),sources,stage=EditorJsonUtility.ToJson(boss.GetComponentInParent<BattleStage>()),intro=battle?EditorJsonUtility.ToJson(battle.GetComponent<BattleIntroWalkDebug>()):"",colliders=boss.GetComponentsInChildren<Collider>(true).Select(c=>new{c.name,type=c.GetType().Name,c.enabled,c.isTrigger,size=c.bounds.size.ToString(),json=EditorJsonUtility.ToJson(c)}).ToArray()};
 }
}
