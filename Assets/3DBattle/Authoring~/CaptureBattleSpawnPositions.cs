using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class CaptureBattleSpawnPositions
{
    public static string Apply()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var stage = battle.CurrentStage;
        if (stage == null || stage.gameObject.scene.path != "Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene or missing stage");
        if (stage.playerSpawn == null || stage.bossSpawn == null || battle.player == null || stage.boss == null) throw new Exception("Missing spawn references");
        var playerPosition = battle.player.transform.position;
        var playerRotation = battle.player.transform.rotation;
        var bossPosition = stage.boss.transform.position;
        var bossRotation = stage.boss.transform.rotation;
        Undo.RecordObjects(new UnityEngine.Object[] { stage.playerSpawn, stage.bossSpawn }, "Use current character positions as battle spawns");
        stage.playerSpawn.SetPositionAndRotation(playerPosition, playerRotation);
        stage.bossSpawn.SetPositionAndRotation(bossPosition, bossRotation);
        EditorUtility.SetDirty(stage.playerSpawn);
        EditorUtility.SetDirty(stage.bossSpawn);
        EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);
        if (!EditorSceneManager.SaveScene(stage.gameObject.scene)) throw new Exception("Scene save failed");
        if (Vector3.Distance(stage.playerSpawn.position, playerPosition) > .001f || Vector3.Distance(stage.bossSpawn.position, bossPosition) > .001f) throw new Exception("Spawn mismatch");
        return stage.name + " saved: player=" + playerPosition.ToString("F3") + ", boss=" + bossPosition.ToString("F3") + "; rotations preserved";
    }
}
