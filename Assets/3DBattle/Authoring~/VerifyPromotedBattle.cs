using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class VerifyPromotedBattle
{
 static void Check(bool ok,string m){if(!ok)throw new Exception(m);}
 public static object Run()
 {
  Check(Application.isPlaying,"Play required");EditorApplication.isPaused=true;
  var battle=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();var player=battle.player;
  Check(battle.gameObject.scene.name=="3DBattleScene" && battle.enabled,"Wrong production scene");
  Check(player.GetComponent<NewPlayerAttackCombo>()!=null && player.GetComponent<NewPlayerLocomotion>()!=null,"New player components missing");
  var intro=battle.GetComponent<BattleIntroWalkDebug>();
  Check(intro!=null && player.animator.HasState(0,Animator.StringToHash(intro.movementState)),"Intro run state missing");
  bool introStarted=intro.IsRunning;intro.Stop();intro.autoPlayOnStageStart=false;
  foreach(int stage in new[]{2,3,1})
  {
   Check(battle.SelectStage(stage),"Stage switch failed "+stage);
   Check(battle.CurrentStage.patternRunner.Target==player.GetComponent<BattlePlayerHealth>(),"Boss targeting wrong player");
   Check(battle.followCamera.target==player.transform,"Camera target wrong");
   Check(player.boss==battle.CurrentStage.boss,"Player boss target wrong");
  }
  var boss=battle.CurrentStage.boss;var phase=boss.GetComponent<BossPhaseTwo>();var gimmick=boss.GetComponent<BossPhaseTwoGimmick>();var actions=boss.GetComponent<BossActionController>();
  var motor=player.GetComponent<CharacterController>();motor.enabled=false;player.transform.position=boss.transform.position+Vector3.forward*3.8f;motor.enabled=true;
  Check(phase.ForcePhaseTwo(),"Phase trigger unavailable");phase.Tick(0);
  boss.animator.Play(phase.transitionState,0,1);boss.animator.Update(0);actions.Tick(0);phase.Tick(0);phase.Tick(2.1f);
  Check(gimmick.Locked && !player.enabled && player.shield.activeSelf,"Production gimmick lock failed");
  boss.animator.Play(phase.entryAttack.animationState,0,(gimmick.rotationStarts[8]+.001f)/phase.entryAttack.clip.length);boss.animator.Update(0);
  gimmick.SendMessage("Update");Check(!gimmick.Locked && player.enabled,"Production release failed");
  gimmick.ResetGimmick();Check(battle.followCamera.enabled,"Camera cleanup failed");
  var components=battle.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true));
  int uiRefs=0;
  foreach(var c in components)
  {
   Check(c!=null,"Missing script");var so=new SerializedObject(c);var p=so.GetIterator();
   while(p.Next(true))
   {
    if(p.propertyType!=SerializedPropertyType.ObjectReference)continue;
    var v=p.objectReferenceValue;var go=v is Component other?other.gameObject:v as GameObject;
    if(go!=null && go.scene.IsValid())Check(go.scene==battle.gameObject.scene,"Cross-scene reference: "+c.name+"."+p.propertyPath);
    if(v==player.GetComponent<BattlePlayerHealth>())uiRefs++;
   }
  }
  return new{passed=true,player=player.name,stages=3,introStarted,phase2LockAndRelease=true,externalSceneReferences=0,healthReferences=uiRefs};
 }
}
