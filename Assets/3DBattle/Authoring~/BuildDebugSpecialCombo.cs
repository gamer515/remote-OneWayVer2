using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class BuildDebugSpecialCombo
{
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode required.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/3DBattleDebugScene.unity") throw new Exception("Open the debug scene first.");
        var player = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NewPlayerAttackCombo>(true))
            .Single(c => c.name == "New Player - Attack Combo");
        if (!player.motions.order.SequenceEqual(new[] {4,1,2,5,4,3,0})) throw new Exception("Unexpected authored combo order.");
        var rules = player.GetComponent<DebugPlayerSpecialCombo>();
        if (rules == null) rules = Undo.AddComponent<DebugPlayerSpecialCombo>(player.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed.");
        Selection.activeGameObject = player.gameObject;
        return JsonConvert.SerializeObject(new { scene = scene.path, rules.alwaysFullGauge, gauge = rules.Gauge,
            rules.holdRecognitionSeconds, rules.specialEntryWindow, rules.finisherHoldSeconds, rules.finisherPoseFrame },Formatting.Indented);
    }
}
