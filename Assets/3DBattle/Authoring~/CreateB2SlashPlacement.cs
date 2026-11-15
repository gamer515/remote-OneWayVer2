using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class CreateB2SlashPlacement
{
    const string ObjectName = "B2 Flying Slash - Horizontal Preview";
    public static object Create()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/3DBattleScene.unity") throw new InvalidOperationException("Open 3DBattleScene.");
        if (scene.GetRootGameObjects().Any(g => g.name == ObjectName)) throw new InvalidOperationException("Preview already exists.");
        var phase = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<B2PhaseSmokeAttack>(true)).Single();
        if (!phase.slashPrefab) throw new InvalidOperationException("Phase slash prefab missing.");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(phase.slashPrefab, scene);
        Undo.RegisterCreatedObjectUndo(root, "Add horizontal slash preview");
        root.name = ObjectName;
        root.tag = "EditorOnly";
        // Centre it in the arena so the user can compare the blade against the floor/player.
        Vector3 origin = phase.arenaCenter ? phase.arenaCenter.position : Vector3.zero;
        root.transform.position = origin + Vector3.up * phase.slashHeight;
        root.transform.rotation = Quaternion.AngleAxis(90f, Vector3.forward) * Quaternion.Euler(20f, 0, 0);
        root.transform.localScale = Vector3.one * phase.slashScale;
        var layout = root.GetComponent<B2SlashTrailLayout>();
        var sword = phase.swordEffect;
        if (layout && sword) layout.ApplyLengths(sword.topTrailLength, sword.upperMiddleTrailLength,
            sword.centerTrailLength, sword.lowerMiddleTrailLength, sword.bottomTrailLength);
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false;
            ps.useAutoRandomSeed = false;
            if (ps.randomSeed == 0) ps.randomSeed = 7331;
            PrefabUtility.RecordPrefabInstancePropertyModifications(ps);
        }
        var preview = Undo.AddComponent<B2SlashPlacementPreview>(root);
        preview.Sample();
        PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
        foreach (var part in root.GetComponentsInChildren<Transform>(true))
            PrefabUtility.RecordPrefabInstancePropertyModifications(part);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
        Selection.activeGameObject = root;
        if (SceneView.lastActiveSceneView)
            SceneView.lastActiveSceneView.LookAt(root.transform.position,
                Quaternion.LookRotation(new Vector3(.5f, -.55f, 1f)), 22f);
        SceneView.RepaintAll();
        return Inspect();
    }

    public static object Inspect()
    {
        var root = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == ObjectName);
        var systems = root.GetComponentsInChildren<ParticleSystem>(true);
        return new { root.name, position = root.transform.position, rotation = root.transform.eulerAngles,
            scale = root.transform.localScale, source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root),
            particles = systems.Sum(p => p.particleCount), systems = systems.Length,
            hasDamage = root.GetComponent<B2PhaseSlashProjectile>() != null,
            hasFlight = root.GetComponent<B2SlashFlight>() != null, root.tag,
            editable = root.GetComponent<B2SlashPlacementPreview>() != null };
    }

    public static object InspectParticles()
    {
        var root = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == ObjectName);
        return root.GetComponentsInChildren<ParticleSystem>(true).Select(p => new {
            p.name, p.particleCount, duration = p.main.duration, lifetime = p.main.startLifetime.constantMax,
            active = p.gameObject.activeInHierarchy, material = p.GetComponent<ParticleSystemRenderer>().sharedMaterial?.name
        }).ToArray();
    }
}
