using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class TestB2CombatStateMachine
{
 static async Task Until(Func<bool> test,string name,double seconds=8)
 {
  double end=EditorApplication.timeSinceStartup+seconds;
  while(!test()){if(EditorApplication.timeSinceStartup>end)throw new Exception("Timeout: "+name);await Task.Delay(20);}
 }
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static void Place(BattlePlayerController player,Vector3 position)
 {var motor=player.GetComponent<CharacterController>();bool before=motor.enabled;motor.enabled=false;player.transform.position=position;motor.enabled=before;player.SyncBodyPosition();Physics.SyncTransforms();}
 public static async Task<object> Run()
 {
  if(!Application.isPlaying)throw new Exception("Play mode required");
  Application.runInBackground=true;
  var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
  if(battle.SelectedStageIndex!=2)battle.SelectStage(2);
  var intro=battle.GetComponent<BattleIntroWalkDebug>();intro?.Stop();battle.enabled=false;
  var boss=battle.CurrentStage.boss;var ai=boss.GetComponent<B2CombatController>();var animator=boss.animator;
  var player=battle.player;var health=player.GetComponent<BattlePlayerHealth>();var runner=boss.GetComponent<BossPatternRunner>();
  player.enabled=false;health.ignoreDamage=true;
  animator.speed=4;ai.afterAttackWait=.1f;ai.startWait=.1f;ai.transitionSeconds=.04f;ai.randomAttackOrder=false;
  var effects=animator.GetComponent<B2SwordSlash>();
  var results=new List<string>();
  boss.ResetBoss();Place(player,boss.transform.position+boss.transform.forward*5.6f);
  await Until(()=>ai.State==B2CombatState.Attack,"first attack");
  boss.TakeDamage(boss.maxHealth*.5f);Check(ai.State==B2CombatState.Attack&&!ai.PendingPhase,"Exact 50% incorrectly enters phase or interrupts attack");
  await Until(()=>ai.State==B2CombatState.HitReaction,"queued hit reaction");Check(ai.PhaseTransitionCount==0,"Exact 50% triggered recovery");
  await Until(()=>ai.State==B2CombatState.Attack,"attack after hit");results.Add("Exactly 50%: attack finishes, hit reaction plays, no phase transition");
  boss.TakeDamage(1);Check(ai.State==B2CombatState.Attack&&ai.PendingPhase,"Below 50% must queue until attack finishes");
  await Until(()=>ai.State==B2CombatState.PhaseTransition,"phase recovery");
  int emitted=effects.EmittedCount;var phasePosition=boss.transform.position;
  await Until(()=>ai.PhaseTransitionCompleted,"stand up",5);
  Check(ai.PhaseTransitionCount==1&&effects.EmittedCount==emitted,"Phase emitted attack or repeated");
  Check(Vector3.Distance(phasePosition,boss.transform.position)<.01f,"Phase recovery drifted actor root");
  await Until(()=>ai.State==B2CombatState.Attack,"resume after phase");
  boss.TakeDamage(1);await Task.Delay(750);Check(ai.PhaseTransitionCount==1,"Phase repeated while still below 50%");
  results.Add("Below 50%: one recovery, no attacks/travel during it, combat resumes");

  boss.ResetBoss();ai.dashCooldown=6;ai.dashStartDistance=12;
  Place(player,boss.transform.position+Vector3.right*23);var origin=boss.transform.position;
  await Until(()=>ai.State==B2CombatState.ApproachDash,"far dash");int beforeDashFx=effects.EmittedCount;
  await Until(()=>ai.State!=B2CombatState.ApproachDash,"dash end");
  float dashTravel=Vector3.Distance(origin,boss.transform.position);
  Check(dashTravel>.5f&&effects.EmittedCount==beforeDashFx,"Approach did not travel or emitted slash");
  Check(boss.PlanarDistance(player.transform.position)>=ai.attackRange-.2f,"Approach overshot target");
  results.Add("Far target: authored dash moves boss, stops before target, emits no slash");

  boss.ResetBoss();ai.dashStartDistance=100;Place(player,boss.transform.position+Vector3.right*10);
  await Until(()=>ai.State==B2CombatState.Run,"medium run");origin=boss.transform.position;
  await Task.Delay(250);Check(Vector3.Distance(origin,boss.transform.position)>.25f,"Run did not move");
  Place(player,boss.transform.position+boss.transform.forward*5.6f);
  var seen=new HashSet<string>();double deadline=EditorApplication.timeSinceStartup+8;
  while(seen.Count<4&&EditorApplication.timeSinceStartup<deadline){if(runner.CurrentPattern!=null)seen.Add(runner.CurrentPattern.animationState);await Task.Delay(20);}
  Check(seen.Count==4,"Did not use all four existing attacks");results.Add("Medium target: run; near target: all four attacks with recovery intervals");

  boss.ResetBoss();Place(player,boss.transform.position+boss.transform.forward*5.6f);
  await Until(()=>ai.State==B2CombatState.Attack,"attack before queued hit+phase");boss.TakeDamage(boss.maxHealth*.501f);
  await Until(()=>ai.State==B2CombatState.HitReaction,"hit before phase");Check(ai.PhaseTransitionCount==0,"Phase interrupted hit reaction");
  await Until(()=>ai.State==B2CombatState.PhaseTransition,"phase after hit");
  boss.TakeDamage(10000);Check(ai.State==B2CombatState.Dead&&!runner.IsRunning,"Death must interrupt recovery");
  int deathFx=effects.EmittedCount;origin=boss.transform.position;await Task.Delay(950);
  Check(ai.State==B2CombatState.Dead&&animator.GetCurrentAnimatorStateInfo(0).IsName("Dead"),"Death state restarted combat");
  Check(effects.EmittedCount==deathFx&&Vector3.Distance(origin,boss.transform.position)<.001f,"Dead boss emitted or moved");
  results.Add("Simultaneous hit/phase: full hit first; lethal damage interrupts recovery and holds death pose");
  boss.ResetBoss();Check(!ai.PhaseTransitionCompleted&&ai.PhaseTransitionCount==0&&!ai.PendingHit&&!ai.PendingPhase,"Reset left state flags");
  results.Add("Reset clears phase, pending reactions and terminal death");
  return new{results,dashTravel,attacks=seen.ToArray(),errors=0};
 }
}
