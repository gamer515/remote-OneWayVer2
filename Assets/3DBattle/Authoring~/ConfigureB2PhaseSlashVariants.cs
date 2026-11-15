using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class ConfigureB2PhaseSlashVariants
{
    public static object Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Edit mode required.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/3DBattleScene.unity") throw new InvalidOperationException("Open 3DBattleScene.");
        var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var horizontal = all.Single(t => t.name == "B2 Flying Slash - Horizontal Preview" && t.GetComponent<B2SlashPlacementPreview>());
        var diagonal = all.Single(t => t.name == "B2 Flying Slash - Horizontal Preview (1)" && t.GetComponent<B2SlashPlacementPreview>());
        var phase = all.Select(t => t.GetComponent<B2PhaseSmokeAttack>()).Single(p => p != null);
        if ((horizontal.lossyScale - Vector3.one * phase.slashScale).sqrMagnitude > .0001f ||
            (diagonal.lossyScale - Vector3.one * phase.slashScale).sqrMagnitude > .0001f)
            throw new InvalidOperationException("Preview scales differ from runtime slash scale; inspect first.");
        Undo.RecordObject(phase, "Use authored horizontal and diagonal slashes");
        float ground = phase.arenaCenter ? phase.arenaCenter.position.y : 0f;
        phase.useAngledSlashes = true;
        phase.horizontalSlashHeight = horizontal.position.y - ground;
        phase.horizontalSlashEuler = horizontal.eulerAngles;
        phase.diagonalSlashHeight = diagonal.position.y - ground;
        phase.diagonalSlashEuler = diagonal.eulerAngles;
        EditorUtility.SetDirty(phase);
        PrefabUtility.RecordPrefabInstancePropertyModifications(phase);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
        return new { saved = true, phase.useAngledSlashes, phase.horizontalSlashHeight, phase.diagonalSlashHeight,
            horizontal = phase.horizontalSlashEuler.ToString("F3"), diagonal = phase.diagonalSlashEuler.ToString("F3"),
            phase.shotCount, phase.shotInterval, phase.launchDelay, phase.slashSpeed };
    }

    public static object Inspect()
    {
        var scene = SceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        return new { scene = scene.path, scene.isDirty, playing = Application.isPlaying,
            references = all.Where(t => t.GetComponent<B2SlashPlacementPreview>() != null).Select(t => new {
                name = t.name, id = t.gameObject.GetInstanceID(), position = t.position.ToString("F5"),
                rotation = t.eulerAngles.ToString("F5"), quaternion = t.rotation.ToString("F6"),
                scale = t.lossyScale.ToString("F5"), parent = t.parent ? t.parent.name : "(root)", t.gameObject.activeSelf,
                source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)
            }).ToArray(),
            phases = all.Select(t => t.GetComponent<B2PhaseSmokeAttack>()).Where(p => p != null).Select(p => new {
                p.name, p.slashHeight, p.slashScale, hitLocalCenter = p.hitLocalCenter.ToString("F5"), hitLocalSize = p.hitLocalSize.ToString("F5"), p.shotCount,
                center = p.arenaCenter ? p.arenaCenter.position.ToString("F3") : "none"
            }).ToArray() };
    }
}
