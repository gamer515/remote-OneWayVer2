using System;
using UnityEngine;
using OneWay.Battle3D;
public static class IntroWalkDebugChecks
{
    public static string Run()
    {
        if(!Application.isPlaying) throw new Exception("Play required");
        var d=UnityEngine.Object.FindFirstObjectByType<BattleIntroWalkDebug>();
        var p=d.battle.player;
        var camera=d.battle.followCamera;
        bool enabled=p.enabled, cameraEnabled=camera.enabled;
        d.Begin();
        if(!d.IsRunning || p.enabled || camera.enabled) throw new Exception("Exclusive movement/camera freeze failed");
        Vector3 cameraPosition=camera.transform.position;
        for(int i=0;i<100 && d.IsRunning;i++) d.Tick(.05f);
        if(d.IsRunning || d.Status!="Completed" || Mathf.Abs(d.Travelled-d.TotalDistance)>.01f)
            throw new Exception("Distance result: "+d.Status+" "+d.Travelled+" / "+d.TotalDistance);
        if(p.enabled!=enabled || camera.enabled!=cameraEnabled) throw new Exception("Components not restored");
        if(Vector3.Distance(cameraPosition,camera.transform.position)>.001f) throw new Exception("Camera moved during test");
        float distance=d.Travelled;
        d.ReturnToStart();
        if(Vector3.Distance(p.transform.position,d.battle.CurrentStage.playerSpawn.position)>.001f) throw new Exception("Return failed");
        d.Begin(); d.Tick(.1f); d.Stop();
        if(d.IsRunning || p.enabled!=enabled || camera.enabled!=cameraEnabled) throw new Exception("Stop restoration failed");
        d.ReturnToStart();
        return "PASS: exact distance="+distance+", fixed camera, input restore, stop and return to scene spawn.";
    }
}
