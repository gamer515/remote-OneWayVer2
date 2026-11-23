using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class BuildDebugPlayerCombatFeel
{
    const string ScenePath = "Assets/Scenes/3DBattleDebugScene.unity";
    const string SetPath = "Assets/3DBattle/3DBattleDebug/DebugPlayerAttackSet.asset";
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode required.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new Exception("Open the debug scene first.");
        var player = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BattlePlayerController>(true))
            .Single(p => p.name == "New Player - Attack Combo");
        var combo = player.GetComponent<NewPlayerAttackCombo>();
        var copy = BattleCharacterAssetPaths.Load<NewPlayerAttackSet>(SetPath);
        if (copy == null)
        {
            copy = UnityEngine.Object.Instantiate(combo.motions);
            copy.name = "DebugPlayerAttackSet";
            AssetDatabase.CreateAsset(copy, SetPath);
        }
        Undo.RecordObject(combo, "Use independent debug attack settings");
        combo.motions = copy;
        EditorUtility.SetDirty(combo);
        var feel = player.GetComponent<DebugPlayerCombatFeel>();
        if (feel == null) feel = Undo.AddComponent<DebugPlayerCombatFeel>(player.gameObject);
        var follow = player.viewCamera.GetComponent<BattleFollowCamera>();
        Undo.RecordObject(follow, "Leave headroom for jump apex");
        follow.lookOffset = new Vector3(follow.lookOffset.x, 1.6f, follow.lookOffset.z);
        EditorUtility.SetDirty(follow);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssetIfDirty(copy);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed.");
        Selection.activeGameObject = player.gameObject;
        return Report();
    }
    public static string Report()
    {
        var scene = SceneManager.GetSceneByPath(ScenePath);
        var player = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BattlePlayerController>(true))
            .Single(p => p.name == "New Player - Attack Combo");
        var combo = player.GetComponent<NewPlayerAttackCombo>();
        var feel = player.GetComponent<DebugPlayerCombatFeel>();
        if (feel == null) throw new Exception("Feel component missing.");
        var timing = combo.motions.attacks.Select((m, i) => new {
            m.label, chainSeconds = feel.Duration(i, 0, m.chainFrame, m.Frames, combo.playbackSpeed),
            fullSeconds = feel.Duration(i, 0, m.Frames, m.Frames, combo.playbackSpeed),
            originalChainSeconds = m.chainFrame / (30 * combo.playbackSpeed)
        }).ToArray();
        return JsonConvert.SerializeObject(new { player.moveSpeed, player.rollDistance, combo.playbackSpeed,
            combo.attackRetreatDistance, combo.skillDashDistance, combo.jumpHeightMultiplier,
            attackSet = AssetDatabase.GetAssetPath(combo.motions), feel.jumpCameraFollow, timing }, Formatting.Indented);
    }
}
