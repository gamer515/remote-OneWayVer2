using System;
using UnityEngine;
using OneWay.Battle3D;
public static class AttackLockChecks
{
    public static string Run()
    {
        if (!Application.isPlaying) throw new Exception("Play mode required");
        var c = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var p = c.player;
        p.enabled = false;
        try
        {
            p.ResetForBattle(c.CurrentStage.playerSpawn);
            p.ProcessControlFrame(0f, true, false, 0f);
            if (!p.IsAttackLocked) throw new Exception("Attack not locked");
            var position = p.transform.position;
            var rotation = p.transform.rotation;
            c.followCamera.Orbit(new Vector2(500f, 0f));
            p.ProcessControlFrame(1f, false, false, 0f);
            p.Move(.1f);
            if (Vector3.Distance(position,p.transform.position) > .001f || Quaternion.Angle(rotation,p.transform.rotation) > .01f || p.IsMoving)
                throw new Exception("Camera/scroll changed locked player pose");
            p.ProcessControlFrame(0f, true, false, 0f);
            p.Move(.1f);
            if (Quaternion.Angle(rotation,p.transform.rotation) > .01f) throw new Exception("Buffered input redirected attack");
            // Reproduce the frame where one clip has ended but the next click is
            // still inside the combo window. This used to re-aim at the camera.
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            foreach (var field in new[] { "attackAnimationRemaining", "comboGateRemaining", "nextAttack" })
                typeof(BattlePlayerController).GetField(field, flags).SetValue(p, 0f);
            p.ProcessControlFrame(0f, true, false, 0f);
            p.Move(.01f);
            if (Quaternion.Angle(rotation, p.transform.rotation) > .01f)
                throw new Exception("Next attack re-aimed at clip boundary");
            p.ProcessControlFrame(0f, false, true, 0f);
            if (p.IsAttackLocked || !p.IsDashing) throw new Exception("Dash failed to cancel lock");
            p.Move(.02f);
            if (Vector3.Distance(position,p.transform.position) < .01f) throw new Exception("Dash remained frozen");
            return "PASS: camera rotation + wheel + buffered attack AND next-clip boundary keep player pose; dash cancels lock and moves.";
        }
        finally { p.ResetForBattle(c.CurrentStage.playerSpawn); p.enabled = true; }
    }
}

