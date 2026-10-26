using System;
using System.Linq;
using UnityEngine;
using OneWay.Battle3D;
public static class BossJumpDustChecks
{
    static Transform[] Dust(BossJumpMotionDebug j,string phase) => j.battle.CurrentStage.GetComponentsInChildren<Transform>().Where(t=>t.name=="Boss Jump Dust "+phase).ToArray();
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static string Run()
    {
        Check(Application.isPlaying,"Play mode required");
        var j=UnityEngine.Object.FindFirstObjectByType<BossJumpMotionDebug>();
        var approach=j.GetComponent<BossApproachController>(); if(approach!=null)approach.enabled=false;
        j.battle.SelectStage(j.battle.SelectedStageIndex); Physics.SyncTransforms();
        foreach(var dust in Dust(j,"Takeoff").Concat(Dust(j,"Landing"))) UnityEngine.Object.DestroyImmediate(dust.gameObject);
        int checkedCount=0;
        foreach(bool variant in new[]{false,true})
        {
            if(!j.CanExecute(variant))continue;
            var timing=variant?j.jump17Timing:j.jump12Timing;
            int startCount=Dust(j,"Takeoff").Length,landCount=Dust(j,"Landing").Length;
            j.useJump17=variant; j.Begin(); Check(j.IsRunning,"Jump not started");
            j.Tick(Mathf.Max(0,timing.takeoffTime-.01f)/j.playbackSpeed);
            Check(Dust(j,"Takeoff").Length==startCount,"Dust before takeoff");
            j.Tick(.02f/j.playbackSpeed);
            Check(Dust(j,"Takeoff").Length==startCount+1,"Takeoff burst count");
            var takeoff=Dust(j,"Takeoff").Last(); var position=takeoff.position;
            Check(Mathf.Abs(takeoff.lossyScale.x-2.6f)<.001f,"Expected 2x prefab scale");
            Check(Dust(j,"Landing").Length==landCount,"Landing burst too early");
            j.Tick((timing.landingTime-j.ClipTime+.001f)/j.playbackSpeed);
            Check(Dust(j,"Landing").Length==landCount+1,"Landing burst count");
            var landing=Dust(j,"Landing").Last();
            Check(Vector2.Distance(new Vector2(landing.position.x,landing.position.z),new Vector2(j.LandingPosition.x,j.LandingPosition.z))<.01f,"Landing position mismatch");
            Check(takeoff.position==position,"Takeoff dust followed boss");
            j.Tick(10);
            Check(Dust(j,"Takeoff").Length==startCount+1 && Dust(j,"Landing").Length==landCount+1,"Duplicate bursts");
            checkedCount++;
            j.battle.SelectStage(j.battle.SelectedStageIndex); Physics.SyncTransforms();
        }
        Check(checkedCount>0,"No configured jump tested");
        return "PASS: "+checkedCount+" jump variants; no preparation dust, one takeoff/landing burst, fixed ground positions, world scale 2.6 (2x current prefab), no duplicates.";
    }
}
