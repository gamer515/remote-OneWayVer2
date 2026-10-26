using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;
public static class PlayerDefeatChecks
{
    public static string Test()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Play mode required");
        var p = UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
        var h = p.GetComponent<BattlePlayerHealth>();
        bool ignore = h.ignoreDamage;
        try
        {
            p.ResetForBattle(null);
            h.ignoreDamage = false;
            h.TakeDamage(h.maxHealth);
            if (!p.IsDefeated) throw new Exception("Death did not start");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(BattlePlayerController).GetField("deathElapsed", flags).SetValue(p, 2f);
            var late = typeof(BattlePlayerController).GetMethod("LateUpdate", flags);
            late.Invoke(p, null);
            var state = p.animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("MeshyCrouchPick_Player") || state.normalizedTime < .999f) throw new Exception("Final frame not reached: " + state.fullPathHash + " time=" + state.normalizedTime);
            var bones = p.animator.GetComponentsInChildren<Transform>(true);
            var poses = bones.Select(b => b.localRotation).ToArray();
            p.ProcessControlFrame(1, true, true, .1f);
            p.Move(.1f);
            late.Invoke(p, null);
            for (int i = 0; i < bones.Length; i++)
                if (Quaternion.Angle(poses[i], bones[i].localRotation) > .01f) throw new Exception("Final pose changed");
            p.ResetForBattle(null);
            if (p.IsDefeated || p.animator.speed <= 0f || !h.IsAlive) throw new Exception("Battle reset failed");
            return "PASS: lethal damage starts crouch clip, reaches final frame, holds pose against input, reset restores animation";
        }
        finally { h.ignoreDamage = ignore; }
    }
}


