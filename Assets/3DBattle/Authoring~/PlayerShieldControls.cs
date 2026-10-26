using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class PlayerShieldControls
{
    public static string ConnectShield3x()
    {
        if (EditorApplication.isPlaying) throw new Exception("Edit mode required");
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/3DBattle/Animations/KnightBattle.controller");
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/MeshyPlayer/Converted/MeshyShieldPushLeft_Player_3x.anim");
        if (clip == null) throw new Exception("Missing shield 3x clip");
        var state = c.layers[0].stateMachine.states.Single(x => x.state.name == "MeshyShieldPushLeft_Player").state;
        Undo.RecordObject(state, "Connect shield 3x");
        state.motion = clip;
        EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssetIfDirty(c);
        return state.name + " => " + state.motion.name + ", duration=" + clip.length + ", speed=" + state.speed + ", parameter=" + state.speedParameter;
    }

    public static string Setup()
    {
        if (EditorApplication.isPlaying) throw new Exception("Edit mode required");
        var p = UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
        if (p.gameObject.scene.path != "Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var c = (AnimatorController)p.animator.runtimeAnimatorController;
        if (!AssetDatabase.GetAssetPath(c).StartsWith("Assets/3DBattle/")) throw new Exception("Controller outside scope");
        Undo.RecordObject(p, "Configure shield controls");
        p.shield = p.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Shield4").gameObject;
        Undo.RecordObject(p.shield, "Hide shield in attack mode");
        p.shield.SetActive(false);
        foreach (string parameter in new [] { "ShieldSpeed", "RollSpeed" })
            if (!c.parameters.Any(x => x.name == parameter)) c.AddParameter(parameter, AnimatorControllerParameterType.Float);
        foreach (var pair in new [] { new [] { "MeshyShieldPushLeft_Player", "ShieldSpeed" }, new [] { "MeshyGroundRoll_Player", "RollSpeed" } })
        {
            var s = c.layers[0].stateMachine.states.Single(x => x.state.name == pair[0]).state;
            s.speed = 1; s.speedParameterActive = true; s.speedParameter = pair[1];
            EditorUtility.SetDirty(s);
        }
        p.animator.applyRootMotion = false;
        EditorUtility.SetDirty(p.animator);
        EditorUtility.SetDirty(p); EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssetIfDirty(c);
        EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
        EditorSceneManager.SaveScene(p.gameObject.scene);
        return "Shield assigned and hidden; roll/block speed parameters connected; existing attacks preserved";
    }

    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static string Test()
    {
        if (!EditorApplication.isPlaying) throw new Exception("Play mode required");
        var p = UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
        var h = p.GetComponent<BattlePlayerHealth>();
        bool ignoredDamage = h.ignoreDamage;
        h.ignoreDamage = false;
        var originalPosition = p.transform.position;
        var originalRotation = p.transform.rotation;
        p.ResetForBattle(null);
        Require(!p.IsDefenseMode && !p.shield.activeSelf, "Default shield visible");
        Require(p.UsesFiveHitCombo, "Existing combo lost");
        p.ProcessControlFrame(-1, false, false, 0);
        Require(p.IsDefenseMode && p.shield.activeSelf && !p.IsMoving, "Defense mode failed");
        h.TakeDamage(10);
        Require(h.Health == h.maxHealth - 10, "Passive mode should not block");
        h.ResetHealth();
        p.ProcessControlFrame(0, true, false, 0);
        p.animator.Update(0);
        Require(p.IsBlocking && p.animator.GetCurrentAnimatorStateInfo(0).IsName("MeshyShieldPushLeft_Player"), "Block motion failed");
        h.TakeDamage(10); Require(h.Health == h.maxHealth, "Block failed");
        p.ProcessControlFrame(1, false, false, 0);
        Require(!p.IsBlocking && !p.shield.activeSelf, "Attack mode failed");
        p.ProcessControlFrame(0, true, false, 0);
        Require(p.IsAttackLocked && p.ComboStep == 1, "Normal attack failed");
        foreach (float wheel in new [] { 1f, -1f })
        {
            p.ProcessControlFrame(wheel, false, true, 0);
            p.animator.Update(0);
            Require(p.IsDashing && !p.IsBlocking && !p.BackwardDashPending, "Roll start failed");
            Require(p.animator.GetCurrentAnimatorStateInfo(0).IsName("MeshyGroundRoll_Player"), "Roll clip failed");
            var start = p.transform.position; var forward = p.CameraForward;
            p.Move(.05f);
            Require(Vector3.Dot(p.transform.position - start, forward) > 0, "Roll moved backward");
            p.Move(3f);
            Require(!p.IsDashing && p.IsMoving, "Auto run failed");
        }
        p.ResetForBattle(null);
        p.transform.SetPositionAndRotation(originalPosition, originalRotation);
        h.ignoreDamage = ignoredDamage;
        Require(!p.shield.activeSelf && !p.IsMoving, "Reset failed");
        return "PASS: default, mode switching, passive damage, active block, original combo, forward roll in both modes, auto run, reset";
    }
}
