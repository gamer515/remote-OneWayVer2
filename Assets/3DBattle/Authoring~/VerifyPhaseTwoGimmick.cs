using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class VerifyPhaseTwoGimmick
{
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Call(object o,string name)=>o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);
    public static object Run()
    {
        Require(Application.isPlaying,"Play required");EditorApplication.isPaused=true;
        var battle=UnityEngine.Object.FindFirstObjectByType<BattleDebugBootstrap>().battle;
        battle.SelectStage(1);
        var boss=battle.CurrentStage.boss;var phase=boss.GetComponent<BossPhaseTwo>();var g=boss.GetComponent<BossPhaseTwoGimmick>();var actions=boss.GetComponent<BossActionController>();
        var health=battle.player.GetComponent<BattlePlayerHealth>();var a=boss.animator;
        boss.GetComponent<BossApproachController>().automaticApproach=false;
        var player=battle.player;var motor=player.GetComponent<CharacterController>();motor.enabled=false;
        player.transform.position=boss.transform.position+Vector3.forward*3.8f;motor.enabled=true;
        player.transform.position=boss.transform.position+Vector3.forward*30;
        Require(!g.PrepareEntry(.02f) && actions.Current==BossActionKind.Jump12,"initial jump missing");
        actions.ResetController();
        Require(!g.PrepareEntry(.02f) && actions.Current==BossActionKind.Jump12,"repeat jump missing");
        actions.ResetController();player.transform.position=boss.transform.position+Vector3.forward*10;
        Vector3 oldBoss=boss.transform.position;g.PrepareEntry(.1f);
        Require(actions.Current==BossActionKind.RunApproach && (boss.transform.position-oldBoss).sqrMagnitude>0,"run adjustment missing");
        actions.ResetController();g.ResetGimmick();
        motor.enabled=false;player.transform.position=boss.transform.position+Vector3.forward*3.8f;motor.enabled=true;
        player.animator.speed=0;
        Require(phase.ForcePhaseTwo(),"force button rejected");phase.Tick(0);
        Require(actions.Current==BossActionKind.PhaseChange,"transition missing");
        float hp=boss.Health;boss.TakeDamage(100);Require(boss.Health==hp,"transition not invulnerable");
        a.Play(phase.transitionState,0,1);a.Update(0);actions.Tick(0);phase.Tick(0);phase.Tick(1);
        Require(!g.Locked,"wait was skipped");phase.Tick(1.1f);
        Require(g.Locked && !player.enabled && player.shield.activeSelf,"entry controls not locked");
        a.Update(0);Call(g,"Update");Require(g.Successes==0 && g.Failures==0,"rotation1 damaged");
        hp=boss.Health;boss.TakeDamage(100);Require(boss.Health==hp,"spin not invulnerable");
        for(int i=0;i<7;i++)
        {
            float beat=g.strikeTimes[i];
            a.Play(phase.entryAttack.animationState,0,(beat-.1f)/phase.entryAttack.clip.length);a.Update(0);
            if(i==0){Require(g.PressParry(beat-.1f),"first click rejected");Require(!g.PressParry(beat-.05f),"duplicate click accepted");}
            if(i==2){Require(g.PressParry(g.rotationStarts[i+1]+.001f),"early click rejected");Require(!g.PressParry(beat-.01f),"early retry accepted");}
            if(i==4 || i==5 || i==6)g.PressParry(Mathf.Max(g.rotationStarts[i+1]+.001f,beat-.05f));
            a.Play(phase.entryAttack.animationState,0,(beat+.001f)/phase.entryAttack.clip.length);a.Update(0);Call(g,"Update");
        }
        Require(g.Successes==4 && g.Failures==3,"parry results incorrect: "+g.Successes+"/"+g.Failures);
        Require(Mathf.Approximately(health.Health,health.maxHealth-3*g.failureDamage),"independent damage failed");
        a.Play(phase.entryAttack.animationState,0,(g.rotationStarts[8]+.001f)/phase.entryAttack.clip.length);a.Update(0);Call(g,"Update");
        Require(!g.Locked && player.enabled && player.IsDefenseMode,"rotation9 did not release controls");
        hp=boss.Health;boss.TakeDamage(1);Require(boss.Health<hp,"rotation9 still invulnerable");
        Require(player.animator.speed>0,"player animation remained frozen");
        boss.TakeDamage(boss.maxHealth*.8f);Require(actions.Current==BossActionKind.HitReaction,"ninth rotation reaction blocked");
        boss.TakeDamage(boss.Health);Require(actions.Current==BossActionKind.Down,"ninth rotation death blocked");
        g.ResetGimmick();Require(battle.followCamera.enabled,"camera not restored");
        return new{passed=true,successes=g.Successes,failures=g.Failures,playerHealth=health.Health,phase=phase.Phase,checks="transition/wait/near entry/no first hit/single click/early retry/seven strikes/independent damage/ninth release/boss vulnerable/camera cleanup"};
    }
}


