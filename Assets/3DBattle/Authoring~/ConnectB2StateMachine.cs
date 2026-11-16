using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class ConnectB2StateMachine
{
 const string Folder="Assets/3DBattle/Boss2/BossAnim/StateMachine";
 static AnimationClip Clip(string name)=>AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/"+name+".anim");
 public static object Run()
 {
  if(Application.isPlaying)throw new Exception("Edit mode required");
  var boss=GameObject.Find("Battle3D/Stages/B2/Boss").GetComponent<BattleBoss>();
  var animator=boss.animator;var old=(AnimatorController)animator.runtimeAnimatorController;
  string[] attackNames={"PowerSlash","DoubleCombo","WeaponCombo","Skill03"};
  var attackClips=attackNames.ToDictionary(n=>n,n=>(AnimationClip)old.layers[0].stateMachine.states.Single(s=>s.state.name==n).state.motion);
  string path=Folder+"/B2_Combat.controller";var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
  if(!controller)controller=AnimatorController.CreateAnimatorControllerAtPath(path);
  var sm=controller.layers[0].stateMachine;
  var clips=new Dictionary<string,AnimationClip>{{"Idle",Clip("B2_Idle_02")},{"Run",Clip("B2_Run")},{"HitReaction",Clip("B2_HitReaction")},{"Dead",Clip("B2_Death")},{"PhaseRecovery",Clip("B2_PhaseRecovery")},{"ApproachDash",AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss2/BossAnim/Approach/B2_ApproachDash_InPlace.anim")}};
  foreach(var pair in attackClips)clips[pair.Key]=pair.Value;
  foreach(var pair in clips)
  {
   if(!pair.Value)throw new Exception("Missing clip "+pair.Key);
   var state=sm.states.Select(s=>s.state).FirstOrDefault(s=>s.name==pair.Key)??sm.AddState(pair.Key);
   state.motion=pair.Value;state.speed=1;state.writeDefaultValues=true;
   foreach(var transition in state.transitions)state.RemoveTransition(transition);
   if(pair.Key=="Idle")sm.defaultState=state;
  }
  var combat=boss.GetComponent<B2CombatController>();if(!combat)combat=Undo.AddComponent<B2CombatController>(boss.gameObject);
  var hit=boss.GetComponent<BossWeaponHit>();if(!hit)hit=Undo.AddComponent<BossWeaponHit>(boss.gameObject);
  var blade=animator.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="B2 Holy Sword");
  Undo.RecordObjects(new UnityEngine.Object[]{boss,animator,combat,hit},"Connect B2 combat states");
  combat.animator=animator;combat.movement=boss.GetComponent<B2AttackStepMovement>();combat.battle=boss.GetComponentInParent<Battle3DSceneController>();
  combat.idleClip=clips["Idle"];combat.runClip=clips["Run"];combat.approachClip=clips["ApproachDash"];combat.hitClip=clips["HitReaction"];combat.phaseClip=clips["PhaseRecovery"];combat.deathClip=clips["Dead"];
  var travel=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss2/BossAnim/Approach/B2_ApproachDash_Travel.anim");
  combat.approachDistance=AnimationUtility.GetEditorCurve(travel,EditorCurveBinding.FloatCurve("",typeof(Transform),"m_LocalPosition.z"));
  // Widen the blade's narrow edge to the visible slash, without extending its blade length.
  hit.weapon=blade;hit.localCenter=new Vector3(0,0,.003f);hit.localSize=new Vector3(.006f,.0012f,.013f);hit.enabled=true;
  var patterns=new List<BossWeaponAttackPattern>();
  foreach(var name in attackNames)
  {
   string p=Folder+"/B2_"+name+".asset";var attack=AssetDatabase.LoadAssetAtPath<BossWeaponAttackPattern>(p);
   if(!attack){attack=ScriptableObject.CreateInstance<BossWeaponAttackPattern>();AssetDatabase.CreateAsset(attack,p);}
   attack.name="B2_"+name;attack.displayName="B2 "+name;attack.clip=clips[name];attack.animationState="Base Layer."+name;
   attack.damage=20;attack.recoverySeconds=0;attack.independentStrikes=true;
   var events=AnimationUtility.GetAnimationEvents(attack.clip).Where(e=>e.functionName.StartsWith("EmitB2")).OrderBy(e=>e.time).ToArray();
   float[] down=name=="PowerSlash"?new[]{3f}:name=="DoubleCombo"?new[]{6f,6f}:name=="WeaponCombo"?new[]{3.5f,0f,9f}:new[]{1.5f};
   attack.hitWindows=events.Select((e,i)=>new BossWeaponAttackPattern.HitWindow(Mathf.Max(0,e.time-.15f),Mathf.Min(attack.clip.length,e.time+.16f)){downwardExtension=down[i]}).ToArray();
   if(events.Length==0||!attack.TimingIsValid())throw new Exception("Invalid attack windows: "+name);
   EditorUtility.SetDirty(attack);patterns.Add(attack);
  }
  combat.attacks=patterns.ToArray();combat.enabled=true;
  animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.avatar=null;animator.speed=1;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
  boss.idleState="Base Layer.Idle";boss.walkState="Base Layer.Run";boss.deadState="Base Layer.Dead";boss.patrolEnabled=false;
  var stage=boss.GetComponentInParent<BattleStage>();Undo.RecordObject(stage.settings,"Connect B2 attack patterns");stage.settings.patterns=patterns.Cast<BossAttackPattern>().ToArray();EditorUtility.SetDirty(stage.settings);
  foreach(var item in new UnityEngine.Object[]{boss,animator,combat,hit,controller})EditorUtility.SetDirty(item);
  clips["Idle"].SampleAnimation(animator.gameObject,0);
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);bool saved=EditorSceneManager.SaveScene(boss.gameObject.scene);
  Selection.activeGameObject=boss.gameObject;
  return new{saved,states=clips.Select(p=>new{state=p.Key,p.Value.length}).ToArray(),attacks=patterns.Select(p=>new{p.name,p.StrikeCount,p.hitWindows}).ToArray(),bossPosition=boss.transform.position.ToString(),swordScale=blade.lossyScale.ToString(),surfaceAttackRange=combat.attackRange,dashDistance=combat.dashStartDistance};
 }
}
