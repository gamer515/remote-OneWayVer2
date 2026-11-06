using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using OneWay.Battle3D;
public static class VerifyBossPhaseTwo
{
    static void Check(bool value,string label,List<string> results){if(!value)throw new Exception(label);results.Add(label);}
    static async Task Until(Func<bool> condition,float seconds=12)
    {
        double end=UnityEditor.EditorApplication.timeSinceStartup+seconds;
        while(!condition()){if(UnityEditor.EditorApplication.timeSinceStartup>end)throw new Exception("Timed out");await Task.Delay(30);}
    }
    public static async Task<object> Run()
    {
        var results=new List<string>();
        var boss=UnityEngine.Object.FindFirstObjectByType<BattleBoss>();
        var phase=boss.GetComponent<BossPhaseTwo>();var actions=boss.GetComponent<BossActionController>();
        var ai=boss.GetComponent<BossApproachController>();var runner=boss.GetComponent<BossPatternRunner>();
        ai.automaticApproach=false;actions.RequestStop();runner.Target.ignoreDamage=true;
        boss.ResetBoss();boss.TakeDamage(500);
        await Until(()=>!actions.IsBusy);
        await Task.Delay(100);
        Check(phase.Phase==1,"Exactly 50 percent stays phase 1",results);
        Check(!actions.RequestAttack(phase.entryAttack)&&!actions.RequestAttack(phase.originalAttack8),"Attack 8 blocked before phase entry",results);
        boss.ResetBoss();
        Check(actions.RequestAttack(ai.basicAttack),"Normal attack started",results);
        boss.TakeDamage(501);
        await Task.Delay(100);
        Check(actions.Current==BossActionKind.Attack&&phase.Phase==1,"Threshold crossing waits for attack completion",results);
        await Until(()=>actions.Current==BossActionKind.HitReaction);
        Check(phase.Phase==1,"Queued hit reaction finishes before phase 2",results);
        await Until(()=>phase.IsInvulnerable);
        float hp=boss.Health;boss.TakeDamage(100);
        Check(boss.Health==hp&&phase.Phase==2,"Skill_01 phase 2 is invulnerable",results);
        await Until(()=>!phase.IsInvulnerable);
        float skillEnd=Time.time;
        boss.TakeDamage(1);
        Check(boss.Health==hp-1,"Invulnerability ends with Skill_01",results);
        await Until(()=>runner.CurrentPattern==phase.entryAttack);
        float wait=Time.time-skillEnd;
        Check(wait>=1.9f&&wait<2.5f,"Attack 8 starts after 2 second wait",results);
        Check(phase.entryAttack.IsValid(runner)&&phase.entryAttack.downwardExtension==4.5f,"Attack 8 timing and downward hitbox valid",results);
        await Until(()=>phase.EntryFinished);
        Check(!actions.RequestAttack(phase.entryAttack)&&!actions.RequestAttack(phase.originalAttack8),"Attack 8 cannot repeat after entry",results);
        boss.ResetBoss();Check(phase.Phase==1&&!phase.EntryFinished,"Reset returns to phase 1",results);
        return new {results,waitSeconds=wait};
    }
}
