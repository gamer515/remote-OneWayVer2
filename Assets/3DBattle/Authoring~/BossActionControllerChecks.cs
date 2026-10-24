using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public sealed class ActionCheckPattern : BossAttackPattern
{
    public override IEnumerator Execute(BossPatternRunner runner)
    {
        runner.ShowTelegraph(2f, 90f);
        yield return new WaitForSeconds(.1f);
    }
}

public static class BossActionControllerChecks
{
    static void Check(bool value, string message) { if(!value) throw new Exception(message); }
    public static string Setup()
    {
        Check(!Application.isPlaying, "Edit mode required");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var b=c.FindStage(1).boss;
        Check(b.gameObject.scene.path=="Assets/Scenes/3DBattleScene.unity", "Wrong scene");
        if(b.GetComponent<BossActionController>()==null) Undo.AddComponent<BossActionController>(b.gameObject);
        var p=b.GetComponent<BossActionPriority>();
        Undo.RecordObject(p,"Configure action completion");
        var animatorController=(UnityEditor.Animations.AnimatorController)b.animator.runtimeAnimatorController;
        var clip=animatorController.layers[0].stateMachine.states
            .Select(x=>x.state).First(x=>"Base Layer."+x.name==b.deadState).motion as AnimationClip;
        if(clip!=null) p.downDuration=clip.length;
        EditorUtility.SetDirty(p);
        EditorSceneManager.MarkSceneDirty(b.gameObject.scene);
        EditorSceneManager.SaveScene(b.gameObject.scene);
        Selection.activeGameObject=b.gameObject;
        return "B1 controller attached and scene saved; down duration="+p.downDuration;
    }
    public static async Task<string> Test()
    {
        Check(Application.isPlaying,"Play mode required");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        c.SelectStage(1);
        var b=c.CurrentStage.boss;
        var a=b.GetComponent<BossActionController>();
        var j=b.GetComponent<BossJumpMotionDebug>();
        var p=b.GetComponent<BossActionPriority>();
        var runner=b.GetComponent<BossPatternRunner>();
        var pattern=ScriptableObject.CreateInstance<ActionCheckPattern>();
        var events=new List<string>();
        Action<BossActionKind> start=k=>events.Add("start:"+k);
        Action<BossActionKind,BossActionEnd> end=(k,r)=>events.Add(r+":"+k);
        a.ActionStarted+=start; a.ActionEnded+=end;
        bool background=Application.runInBackground;
        Application.runInBackground=true;
        try
        {
            Check(a.RequestJump(false) && a.Current==BossActionKind.Jump12,"12 start");
            Check(a.RequestAttack(pattern),"Attack replaces preparation");
            Check(!j.IsRunning && runner.IsRunning && a.Current==BossActionKind.Attack,"Exclusive ownership");
            Check(events.SequenceEqual(new[]{"start:Jump12","Interrupted:Jump12","start:Attack"}),"Transition event order");
            a.RequestStop();
            Check(!runner.IsRunning && a.Current==BossActionKind.Idle && !runner.telegraph.enabled,"Attack cleanup");
            events.Clear();
            Check(a.RequestAttack(pattern),"Natural attack start");
            for(int i=0;i<40 && a.IsBusy;i++) await Task.Delay(50);
            Check(a.Current==BossActionKind.Idle,"Natural attack completion");
            Check(events.SequenceEqual(new[]{"start:Attack","Completed:Attack"}),"One attack completion");
            j.ResetToSpawn(); events.Clear();
            Check(a.RequestJump(false),"12 complete start");
            j.Tick(10f);
            Check(!j.IsRunning && a.Current==BossActionKind.Idle,"12 natural completion");
            Check(events.SequenceEqual(new[]{"start:Jump12","Completed:Jump12"}),"One jump completion");
            j.ResetToSpawn();
            Check(a.RequestJump(true),"17 start");
            j.Tick(1.41f);
            Check(p.Airborne && !a.RequestAttack(pattern),"Airborne rejects attack");
            a.RequestStop();
            Check(j.IsRunning && a.StopQueued,"Stop deferred");
            j.Tick(.76f);
            Check(!j.IsRunning && !p.Airborne && a.Current==BossActionKind.Idle,"Stop on landing");
            j.ResetToSpawn(); a.RequestJump(true); j.Tick(1.41f);
            a.RequestStop(); b.TakeDamage(b.maxHealth*.34f); b.TakeDamage(b.maxHealth);
            Check(j.IsRunning && p.PendingDeath && !p.Down,"Death waits for landing");
            j.Tick(.76f);
            Check(p.Down && !j.IsRunning && !a.RequestJump(false),"Death wins at landing");
            events.Clear(); a.Tick(100f); a.Tick(100f);
            Check(events.SequenceEqual(new[]{"Completed:Down"}) && p.Down,"Terminal down completes once");
            j.ResetToSpawn(); a.RequestJump(false); b.TakeDamage(b.maxHealth*.34f);
            Check(p.HitReacting && !j.IsRunning,"Hit interrupts preparation");
            a.Tick(100f); Check(a.Current==BossActionKind.Idle,"Hit completes");
            j.ResetToSpawn(); a.RequestJump(false); j.Tick(1.71f);
            c.SelectStage(2); c.SelectStage(1);
            Check(!j.IsRunning && !p.Airborne && !a.IsBusy && b.animator.speed>0f,"Stage reset cleanup");
            return "PASS: action exclusivity, ordered single events, attack/jump completion, airborne stop, death/hit priority, terminal down, stage reset.";
        }
        finally
        {
            a.ActionStarted-=start; a.ActionEnded-=end;
            Application.runInBackground=background;
            c.SelectStage(2); c.SelectStage(1);
            UnityEngine.Object.Destroy(pattern);
        }
    }
}
