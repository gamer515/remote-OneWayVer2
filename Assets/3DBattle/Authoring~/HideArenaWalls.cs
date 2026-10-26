using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;

public static class HideArenaWalls
{
    public static string Apply()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var stage = battle.FindStage(1);
        if (stage.gameObject.scene.path != "Assets/Scenes/3DBattleScene.unity")
            throw new Exception("Wrong scene");
        var walls = Enumerable.Range(1, 4).Select(i => stage.GetComponentsInChildren<Transform>(true)
            .Single(t => t.name == "Wall" + i)).ToArray();
        foreach (var wall in walls)
        foreach (var renderer in wall.GetComponentsInChildren<Renderer>(true))
        {
            Undo.RecordObject(renderer, "Hide arena wall");
            renderer.enabled = false;
            EditorUtility.SetDirty(renderer);
        }
        var camera = battle.followCamera;
        Undo.RecordObject(camera, "Ignore arena walls for camera");
        camera.ignoredColliders = (camera.ignoredColliders ?? new Collider[0])
            .Concat(walls.SelectMany(w => w.GetComponentsInChildren<Collider>(true))).Distinct().ToArray();
        EditorUtility.SetDirty(camera);
        if (walls.Any(w => w.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled)) ||
            stage.boundary.walls.Any(w => !w.enabled || w.isTrigger || !camera.ignoredColliders.Contains(w)))
            throw new Exception("Wall visibility/collision verification failed");
        EditorSceneManager.MarkSceneDirty(stage.gameObject.scene);
        EditorSceneManager.SaveScene(stage.gameObject.scene);
        return "Verified: four walls hidden, boundary colliders remain solid, camera ignores all four walls.";
    }
}
