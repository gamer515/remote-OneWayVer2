using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class JumpMotionSetup
{
    public static string Setup()
    {
        if(Application.isPlaying) throw new Exception("Edit mode required");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        if(c.gameObject.scene.path!="Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var boss=c.FindStage(1).boss;
        var p=boss.GetComponent<BossActionPriority>();
        if(p==null) p=Undo.AddComponent<BossActionPriority>(boss.gameObject);
        p.hitDuration=boss.animator.runtimeAnimatorController.animationClips.First(x=>x.name.Split('|').Last()=="Hit_Reaction_1").length;
        var d=boss.GetComponent<BossJumpMotionDebug>();
        if(d==null) d=Undo.AddComponent<BossJumpMotionDebug>(boss.gameObject);
        d.battle=c; d.jump12Timing=boss.GetComponent<BossJumpTimingDebug>();
        if(d.jump12Timing==null) throw new Exception("Missing timing debug");
        Undo.RecordObject(d.jump12Timing,"Set confirmed jump timing");
        d.jump12Timing.takeoffTime=1.7f; d.jump12Timing.landingTime=2.38f;
        d.jump17Clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Boss1/Jump_with_Arms_Open_Boss1.anim");
        EditorUtility.SetDirty(p); EditorUtility.SetDirty(d); EditorUtility.SetDirty(d.jump12Timing);
        EditorSceneManager.MarkSceneDirty(c.gameObject.scene); EditorSceneManager.SaveScene(c.gameObject.scene);
        Selection.activeGameObject=boss.gameObject;
        return "Connected B1 jump motion: 12 takeoff=1.7 landing=2.38; 17 timings intentionally unset.";
    }
    static void Check(bool value,string text) { if(!value) throw new Exception(text); }
    public static string Test()
    {
        Check(Application.isPlaying,"Play required");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        c.SelectStage(1);
        var b=c.CurrentStage.boss; var d=b.GetComponent<BossJumpMotionDebug>(); var p=b.GetComponent<BossActionPriority>();
        try
        {
            d.Begin(); Check(d.IsRunning,"Begin failed");
            Vector3 start=b.transform.position;
            d.Tick(1.69f); Check(!p.Airborne && Vector3.Distance(start,b.transform.position)<.001f,"Windup moved");
            d.Tick(.02f); Check(p.Airborne,"Airborne not entered");
            Vector3 locked=d.LandingPosition;
            b.TakeDamage(b.maxHealth*.34f);
            Check(p.PendingHit && !p.HitReacting && d.IsRunning,"Hit interrupted air");
            b.TakeDamage(b.maxHealth);
            Check(!p.Down && d.IsRunning,"Death interrupted air");
            d.Tick(.68f);
            Check(p.Down && !p.HitReacting && !d.IsRunning && !p.Airborne,"Landing death priority failed");
            Check(Vector3.Distance(b.transform.position,locked)<.001f,"Wrong landing point");
            d.ResetToSpawn(); d.Begin(); d.Tick(1.71f); b.TakeDamage(b.maxHealth*.34f); d.Tick(.68f);
            Check(p.HitReacting && !p.Down && !d.IsRunning,"Landing pending hit failed");
            d.ResetToSpawn(); d.Begin(); d.Tick(.5f); b.TakeDamage(b.maxHealth*.34f);
            Check(p.HitReacting && !d.IsRunning,"Windup hit should interrupt");
            d.ResetToSpawn(); d.Begin(); d.Tick(2.4f); b.TakeDamage(b.maxHealth);
            Check(p.Down && !d.IsRunning,"Recovery death should interrupt");
            d.ResetToSpawn(); d.Begin(); d.Tick(1.71f); d.RequestStop();
            Check(d.IsRunning && d.StopAfterLanding,"Stop interrupted air");
            d.Tick(.68f); Check(!d.IsRunning && !p.Airborne,"Queued stop failed");
            d.ResetToSpawn(); d.playbackSpeed=.5f; d.Begin(); d.Tick(3.4f);
            Check(p.Airborne && Mathf.Abs(d.ClipTime-1.7f)<.001f,"Playback speed timing failed");
            d.Tick(1.36f); Check(!p.Airborne,"Slow landing timing failed");
            d.RequestStop(); d.ResetToSpawn(); d.playbackSpeed=1f;
            d.useJump17=true; d.Begin(); Check(!d.IsRunning,"Unconfigured 17 should reject"); d.useJump17=false;
            return "PASS: windup fixed, flight, landing, pending hit/death ordering, preparation/recovery interrupts, airborne stop deferral, playback speed and unconfigured 17 guard.";
        }
        finally { c.SelectStage(1); }
    }
}
