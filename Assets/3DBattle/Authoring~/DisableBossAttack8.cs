using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class DisableBossAttack8
{
    public static string Apply()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        var scene = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var brain = scene.FindStage(1).boss.GetComponent<BossApproachController>();
        if (brain.gameObject.scene.path != "Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var attack8 = AssetDatabase.LoadAssetAtPath<BossAttackPattern>("Assets/3DBattle/Settings/B1_Attack8.asset");
        if (attack8 == null) throw new Exception("Attack 8 asset missing");
        Undo.RecordObject(brain, "Exclude boss attack 8");
        brain.attacks = brain.attacks.Where(p => p != attack8).ToArray();
        if (brain.basicAttack == attack8) throw new Exception("Unexpected basic attack 8");
        EditorUtility.SetDirty(brain);
        EditorSceneManager.MarkSceneDirty(brain.gameObject.scene);
        EditorSceneManager.SaveScene(brain.gameObject.scene);
        return "Saved attack pool: " + string.Join(", ", brain.attacks.Select(p => p.name));
    }
}
