using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class SyncDebugPlayerSettings
{
    const string SourcePath = "Assets/Scenes/3DBattleScene.unity";
    const string TargetPath = "Assets/Scenes/3DBattleDebugScene.unity";
    const string PlayerName = "New Player - Attack Combo";

    public static string Inspect() => Compare(false);
    public static string VerifySaved() => Compare(true);

    static string Compare(bool saved)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode required.");
        var source = EditorSceneManager.OpenPreviewScene(SourcePath);
        var target = SceneManager.GetSceneByPath(TargetPath);
        bool previewTarget = saved || !target.IsValid() || !target.isLoaded;
        if (previewTarget) target = EditorSceneManager.OpenPreviewScene(TargetPath);
        try
        {
            var a = Player(source);
            var b = Player(target);
            var av = Values(a);
            var bv = Values(b);
            var differences = av.Keys.Union(bv.Keys).OrderBy(k => k)
                .Where(k => !av.ContainsKey(k) || !bv.ContainsKey(k) || av[k] != bv[k])
                .Select(k => new { property = k, source = av.ContainsKey(k) ? av[k] : "<missing>", debug = bv.ContainsKey(k) ? bv[k] : "<missing>" }).ToArray();
            return JsonConvert.SerializeObject(new { source = SourcePath, target = TargetPath, target.isDirty,
                sourceProperties = av.Count, targetProperties = bv.Count, differences,
                sourceCamera = CameraSettings(a), debugCamera = CameraSettings(b) }, Formatting.Indented);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(source);
            if (previewTarget) EditorSceneManager.ClosePreviewScene(target);
        }
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode required.");
        var target = SceneManager.GetSceneByPath(TargetPath);
        if (!target.IsValid() || !target.isLoaded) throw new Exception("Open the debug scene first.");
        if (target.isDirty) throw new Exception("Debug scene has unsaved edits; inspect before syncing.");
        var source = EditorSceneManager.OpenPreviewScene(SourcePath);
        try
        {
            var a = Player(source);
            var b = Player(target);
            var av = Values(a);
            var bv = Values(b);
            var allowed = new HashSet<string> {
                "$player/OneWay.Battle3D.BattlePlayerController/moveSpeed",
                "$player/OneWay.Battle3D.BattlePlayerController/rollDistance",
                "$player/OneWay.Battle3D.BattleSwordSlashEffect/slashPrefab",
                "$player/OneWay.Battle3D.BattleSwordSlashEffect/hitFlashPrefab"
            };
            var differences = av.Keys.Union(bv.Keys).Where(k => !av.ContainsKey(k) || !bv.ContainsKey(k) || av[k] != bv[k]).ToArray();
            if (differences.Any(k => !allowed.Contains(k)))
                throw new Exception("Unexpected differences: " + string.Join(", ", differences.Where(k => !allowed.Contains(k))));
            if (differences.Length > 0)
            {
                var srcPlayer = a.GetComponent<BattlePlayerController>();
                var dstPlayer = b.GetComponent<BattlePlayerController>();
                var srcVfx = a.GetComponent<BattleSwordSlashEffect>();
                var dstVfx = b.GetComponent<BattleSwordSlashEffect>();
                Undo.RecordObjects(new UnityEngine.Object[] { dstPlayer, dstVfx }, "Match debug player to battle player");
                dstPlayer.moveSpeed = srcPlayer.moveSpeed;
                dstPlayer.rollDistance = srcPlayer.rollDistance;
                dstVfx.slashPrefab = srcVfx.slashPrefab;
                dstVfx.hitFlashPrefab = srcVfx.hitFlashPrefab;
                EditorUtility.SetDirty(dstPlayer);
                EditorUtility.SetDirty(dstVfx);
                EditorSceneManager.MarkSceneDirty(target);
                if (!EditorSceneManager.SaveScene(target)) throw new Exception("Failed to save debug scene.");
            }
            return JsonConvert.SerializeObject(new { saved = TargetPath, changed = differences });
        }
        finally { EditorSceneManager.ClosePreviewScene(source); }
    }

    static GameObject Player(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
        .Single(t => t.name == PlayerName).gameObject;

    static string PathOf(Transform t, Transform root)
    {
        if (t == root) return "$player";
        var names = new List<string>();
        while (t != null && t != root) { names.Add(t.name); t = t.parent; }
        names.Reverse();
        return string.Join("/", names);
    }

    static string Reference(UnityEngine.Object value, Transform root)
    {
        if (value == null) return "null";
        if (EditorUtility.IsPersistent(value))
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id);
            return AssetDatabase.GetAssetPath(value) + "|" + guid + "|" + id;
        }
        var t = value is GameObject g ? g.transform : value is Component c ? c.transform : null;
        if (t != null && (t == root || t.IsChildOf(root))) return PathOf(t, root) + ":" + value.GetType().FullName;
        // Scene-local boss, feedback, arena and camera references must stay in their own scene.
        return "scene-local:" + value.GetType().FullName;
    }

    static Dictionary<string, string> Values(GameObject root)
    {
        var values = new Dictionary<string, string>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var path = PathOf(t, root.transform);
            values[path + "/GameObject.activeSelf"] = t.gameObject.activeSelf.ToString();
            values[path + "/GameObject.layer"] = t.gameObject.layer.ToString();
            foreach (var component in t.GetComponents<Component>())
            {
                if (component == null) throw new Exception("Missing script on " + path);
                var prefix = path + "/" + component.GetType().FullName + "/";
                var so = new SerializedObject(component);
                var p = so.GetIterator();
                while (p.Next(true))
                {
                    // Object reference identity is compared via the normalized parent property above.
                    if (p.propertyPath.EndsWith(".m_FileID")) continue;
                    if (p.propertyPath.StartsWith("m_CorrespondingSourceObject") || p.propertyPath.StartsWith("m_Prefab") ||
                        p.propertyPath == "m_GameObject" || p.propertyPath == "m_ObjectHideFlags" ||
                        p.propertyPath == "m_Father" || p.propertyPath.StartsWith("m_Children") ||
                        p.propertyPath == "m_RootOrder") continue;
                    if (t == root.transform && component is Transform &&
                        (p.propertyPath.StartsWith("m_LocalPosition") || p.propertyPath.StartsWith("m_LocalRotation") ||
                         p.propertyPath.StartsWith("m_LocalEulerAnglesHint"))) continue;
                    if (p.hasChildren && p.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = p.propertyType == SerializedPropertyType.ObjectReference
                        ? Reference(p.objectReferenceValue, root.transform)
                        : JsonConvert.SerializeObject(p.boxedValue);
                    values.Add(prefix + p.propertyPath, value);
                }
            }
        }
        return values;
    }

    static object CameraSettings(GameObject player)
    {
        var camera = player.GetComponent<BattlePlayerController>().viewCamera;
        var follow = camera != null ? camera.GetComponent<BattleFollowCamera>() : null;
        return follow == null ? null : new { offset = new[] { follow.offset.x, follow.offset.y, follow.offset.z },
            lookOffset = new[] { follow.lookOffset.x, follow.lookOffset.y, follow.lookOffset.z }, follow.smoothTime, camera.fieldOfView };
    }
}
