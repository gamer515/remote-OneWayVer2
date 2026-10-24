using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using OneWay.Battle3D;
public sealed class RestCheckAttack : BossAttackPattern
{
    public override IEnumerator Execute(BossPatternRunner runner) { yield return null; }
}
public static class BossRestChecks
{
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static async Task<string> Test()
    {
        var scene=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        scene.SelectStage(1);
        var boss=scene.CurrentStage.boss; var brain=boss.GetComponent<BossApproachController>();
        var actions=boss.GetComponent<BossActionController>();
        var pattern=ScriptableObject.CreateInstance<RestCheckAttack>();
        bool background=Application.runInBackground;
        try
        {
            Application.runInBackground=true;
            brain.automaticApproach=false; actions.RequestStop();
            Check(actions.RequestAttack(pattern),"Attack start");
            for(int i=0;i<20 && actions.IsBusy;i++) await Task.Delay(50);
            Check(!actions.IsBusy,"Attack completion");
            scene.player.transform.position=boss.transform.position+Vector3.forward*4;
            brain.automaticApproach=true; brain.Tick(0);
            Check(actions.Current==BossActionKind.Rest,"Near target must rest");
            boss.animator.Update(0);
            Check(boss.animator.GetCurrentAnimatorStateInfo(0).IsName(boss.idleState),"Rest uses motion 5");
            actions.Tick(2.99f); brain.Tick(0);
            Check(actions.Current==BossActionKind.Rest,"Wait full 3 seconds");
            actions.Tick(.02f); brain.Tick(0);
            Check(actions.Current==BossActionKind.Attack,"Attack resumes after rest");
            actions.RequestStop();
            actions.RequestRest(3);
            scene.player.transform.position=boss.transform.position+Vector3.forward*6;
            brain.Tick(0);
            Check(actions.Current==BossActionKind.RunApproach,"Far target interrupts rest to run");
            actions.RequestStop();
            actions.RequestRest(3);
            scene.player.transform.position=boss.transform.position+Vector3.forward*20;
            brain.Tick(0);
            Check(actions.Current==BossActionKind.Jump12 || actions.Current==BossActionKind.Jump17,"Far target interrupts rest to jump");
            actions.RequestStop(); brain.automaticApproach=false;
            Check(actions.RequestAttack(pattern),"Far completion attack");
            for(int i=0;i<20 && actions.IsBusy;i++) await Task.Delay(50);
            scene.player.transform.position=boss.transform.position+Vector3.forward*6;
            brain.automaticApproach=true; brain.Tick(0);
            Check(actions.Current==BossActionKind.RunApproach,"Far after attack approaches immediately");
            actions.RequestStop(); actions.RequestRest(3);
            boss.TakeDamage(boss.maxHealth*.34f);
            Check(actions.Current==BossActionKind.HitReaction,"Hit priority interrupts rest");
            return "PASS: attack completion -> near rest 5 for 3 sec -> next attack; far immediately approaches; leaving during rest runs/jumps; hit priority retained.";
        }
        finally
        {
            actions.RequestStop(); brain.automaticApproach=true; Application.runInBackground=background;
            scene.SelectStage(2); scene.SelectStage(1); UnityEngine.Object.Destroy(pattern);
        }
    }
}
