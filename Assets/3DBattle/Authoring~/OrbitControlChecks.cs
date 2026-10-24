using System;
using UnityEngine;
using OneWay.Battle3D;
public static class OrbitControlChecks
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); }
    public static string Run()
    {
        if (!Application.isPlaying) throw new Exception("Play mode required");
        var c=UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var p=c.player;
        var camera=c.followCamera;
        p.enabled=false;
        camera.enabled=false;
        try
        {
            p.ResetForBattle(c.CurrentStage.playerSpawn);
            camera.Snap();
            float radius=Vector3.Distance(camera.transform.position,p.transform.position);
            camera.Orbit(new Vector2(180f,0f));
            Check(Mathf.Abs(Vector3.Distance(camera.transform.position,p.transform.position)-radius)<0.01f,"Orbit distance");
            camera.Orbit(new Vector2(0f,10000f));
            Check(camera.transform.position.y >= p.transform.position.y + camera.groundClearance - .01f,"Ground limit");
            Check(Mathf.Abs(Vector3.Distance(camera.transform.position,p.transform.position)-radius)<.01f,"Ground correction retains radius");
            p.ProcessControlFrame(1f,false,false,.01f);
            Check(p.IsMoving,"Wheel starts movement");
            p.ProcessControlFrame(0f,false,false,.01f);
            Check(p.IsMoving,"Movement stays latched");
            camera.Orbit(new Vector2(180f,0f));
            var start=p.transform.position;
            var forward=p.CameraForward;
            p.Move(.02f);
            Check(Vector3.Dot(p.transform.position-start,forward)>0f,"Movement follows camera");
            p.ProcessControlFrame(-1f,false,false,.01f);
            Check(!p.IsMoving && !p.BackwardDashPending,"Wheel stop");
            p.ProcessControlFrame(0f,true,false,1f);
            Check(p.BackwardDashPending,"Attack arms backdash");
            start=p.transform.position;
            p.ProcessControlFrame(0f,false,true,.01f);
            p.Move(.02f);
            Check(Vector3.Dot(p.transform.position-start,p.CameraForward)<0f,"Attack then dash goes backward");
            p.Move(p.dashDuration);
            Check(!p.IsDashing && !p.BackwardDashPending,"Dash ends without recovery lock");
            start=p.transform.position;
            p.ProcessControlFrame(0f,false,true,0f);
            p.Move(.02f);
            Check(Vector3.Dot(p.transform.position-start,p.CameraForward)>0f,"Next dash goes forward immediately");
            p.Move(p.dashDuration);
            p.ProcessControlFrame(0f,true,false,1f);
            p.ProcessControlFrame(-1f,false,false,0f);
            Check(!p.BackwardDashPending,"Stop overrides attack backdash");
            p.ProcessControlFrame(1f,false,false,0f);
            p.ProcessControlFrame(0f,false,true,0f);
            p.Move(p.dashDuration+.02f);
            Check(p.IsMoving && !p.IsDashing,"Auto movement continues after dash");
            return "PASS: orbit radius, ground limit, persistent forward movement, steering, stop, attack-backdash-forward dash, no recovery lock, movement resumes.";
        }
        finally { p.ResetForBattle(c.CurrentStage.playerSpawn); p.enabled=true; camera.enabled=true; camera.Snap(); }
    }
}
