using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class FirstAttackDashChecks
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    public static string Test()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Play mode required");
        var p = UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
        var cameraRotation = p.viewCamera.transform.rotation;
        float range = p.attackRange;
        var start = p.transform.position;
        var rotation = p.transform.rotation;
        var timer = typeof(BattlePlayerController).GetField("attackAnimationRemaining", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            p.ResetForBattle(null);
            p.attackRange = 10000f;
            p.ProcessControlFrame(0, true, false, 0);
            Check(!p.IsFirstAttackDashing && p.ComboStep == 1, "In-range hit 1 must stay fixed");
            var locked = p.transform.rotation;
            p.viewCamera.transform.rotation = Quaternion.Euler(0, 90, 0);
            p.Move(.1f);
            Check(Quaternion.Angle(locked, p.transform.rotation) < .01f, "In-range rotation changed");
            p.attackRange = 0;
            for (int hit = 2; hit <= 5; hit++)
            {
                float gate = p.ComboChainDelay(hit - 2);
                p.ProcessControlFrame(0, true, false, 0);
                p.ProcessControlFrame(0, false, false, gate + .001f);
                Check(p.ComboStep == hit && !p.IsFirstAttackDashing, "Unexpected dash in hit " + hit);
            }
            p.ProcessControlFrame(0, true, false, 0);
            p.ProcessControlFrame(0, false, false, p.ComboChainDelay(4) + .001f);
            Check(p.ComboStep == 1 && p.IsFirstAttackDashing, "Next hit 1 did not dash");
            p.Move(.05f);
            Check(Vector3.Angle(p.transform.forward, p.CameraForward) < .01f, "Dash did not face camera");
            p.viewCamera.transform.rotation = Quaternion.Euler(0, 45, 0);
            p.Move(.05f);
            Check(Vector3.Angle(p.transform.forward, p.CameraForward) < .01f, "Dash rotation did not follow camera");
            p.ProcessControlFrame(0, true, false, 0);
            p.ProcessControlFrame(0, false, false, p.ComboChainDelay(0) + .001f);
            Check(p.ComboStep == 2 && !p.IsFirstAttackDashing, "Hit 2 did not lock");
            locked = p.transform.rotation;
            p.viewCamera.transform.rotation = Quaternion.Euler(0, -90, 0);
            p.Move(.1f);
            Check(Quaternion.Angle(locked, p.transform.rotation) < .01f, "Hit 2 rotation changed");
            p.ProcessControlFrame(-1, true, false, 0);
            Check(p.IsBlocking && !p.IsFirstAttackDashing, "Block interruption failed");
            return "PASS: in-range lock, no dash on hits 2-5, next hit 1 dash with camera steering, hit 2 re-lock, block interruption";
        }
        finally
        {
            p.attackRange = range;
            p.ResetForBattle(null);
            p.transform.SetPositionAndRotation(start, rotation);
            p.viewCamera.transform.rotation = cameraRotation;
        }
    }
}
