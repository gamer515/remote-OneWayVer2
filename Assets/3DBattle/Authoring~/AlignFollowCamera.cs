using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class AlignFollowCamera
{
    public static string Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/3DBattleScene.unity")
            throw new InvalidOperationException("Open 3DBattleScene in Edit mode.");
        var view = SceneView.lastActiveSceneView;
        var player = UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
        var camera = player != null ? player.viewCamera : null;
        var follow = camera != null ? camera.GetComponent<BattleFollowCamera>() : null;
        if (view == null || follow == null) throw new InvalidOperationException("Scene view or follow camera is missing.");

        var reference = view.camera;
        Vector3 position = reference.transform.position;
        Quaternion rotation = reference.transform.rotation;
        Undo.RecordObjects(new UnityEngine.Object[] { camera, camera.transform, follow }, "Match battle camera to Scene view");
        follow.target = player.transform;
        follow.offset = position - player.transform.position;
        float distance = Mathf.Max(1f, follow.offset.magnitude);
        follow.lookOffset = follow.offset + reference.transform.forward * distance;
        camera.orthographic = reference.orthographic;
        camera.fieldOfView = reference.fieldOfView;
        camera.orthographicSize = reference.orthographicSize;
        follow.Snap();

        if (Vector3.Distance(camera.transform.position, position) > 0.001f ||
            Quaternion.Angle(camera.transform.rotation, rotation) > 0.01f)
            throw new InvalidOperationException("Camera alignment did not match Scene view.");
        EditorUtility.SetDirty(camera);
        EditorUtility.SetDirty(follow);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = camera.gameObject;
        return $"Matched Scene camera. Offset={follow.offset:F3}, LookOffset={follow.lookOffset:F3}, FOV={camera.fieldOfView:F2}, Rotation={camera.transform.eulerAngles:F3}";
    }
}
