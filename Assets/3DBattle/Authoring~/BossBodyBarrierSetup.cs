using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using OneWay.Battle3D;
public static class BossBodyBarrierSetup
{
    public static string Setup()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required");
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var boss = battle.FindStage(1).boss;
        if (boss.gameObject.scene.path != "Assets/Scenes/3DBattleScene.unity") throw new Exception("Wrong scene");
        var barrier = boss.GetComponent<BossBodyBarrier>() ?? Undo.AddComponent<BossBodyBarrier>(boss.gameObject);
        Undo.RecordObject(barrier, "Configure boss body barrier");
        barrier.player = UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
        barrier.torso = boss.GetComponent<BattleArenaBody>().torso;
        if (barrier.bodyCollider == null)
        {
            var body = new GameObject("Body Barrier");
            Undo.RegisterCreatedObjectUndo(body, "Create invisible body barrier");
            body.transform.SetParent(boss.transform, false);
            barrier.bodyCollider = body.AddComponent<CapsuleCollider>();
        }
        barrier.bodyCollider.isTrigger = false;
        barrier.bodyCollider.direction = 1;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        // Position the collider in edit mode without applying gameplay corrections.
        var shape = barrier.bodyCollider.transform;
        var center = barrier.torso != null ? barrier.torso.position : boss.transform.position;
        center.y = boss.transform.position.y + barrier.bodyHeight * .5f;
        shape.SetPositionAndRotation(center, Quaternion.identity);
        var scale = boss.transform.lossyScale;
        shape.localScale = new Vector3(1f / Mathf.Abs(scale.x), 1f / Mathf.Abs(scale.y), 1f / Mathf.Abs(scale.z));
        barrier.bodyCollider.radius = barrier.bodyRadius;
        barrier.bodyCollider.height = barrier.bodyHeight;
        EditorUtility.SetDirty(barrier);
        EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);
        EditorSceneManager.SaveScene(boss.gameObject.scene);
        return "Saved body capsule radius=" + barrier.bodyRadius + ", height=" + barrier.bodyHeight + "; colliders=" + string.Join(", ", boss.GetComponentsInChildren<Collider>().Select(c => c.name + ":" + c.GetType().Name));
    }
    public static string Test()
    {
        var barrier = UnityEngine.Object.FindFirstObjectByType<BossBodyBarrier>();
        var p = barrier.player;
        var motor = p.GetComponent<CharacterController>();
        var start = p.transform.position;
        var bossStart = barrier.transform.position;
        var update = typeof(BossBodyBarrier).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        try
        {
            foreach (var direction in new [] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                motor.enabled = false;
                var point = barrier.bodyCollider.transform.position + direction * barrier.bodyRadius;
                point.y = start.y;
                p.transform.position = point;
                motor.enabled = true;
                for (int i = 0; i < 3; i++) update.Invoke(barrier, null);
                Physics.SyncTransforms();
                if (Physics.ComputePenetration(motor, p.transform.position, p.transform.rotation, barrier.bodyCollider,
                    barrier.bodyCollider.transform.position, barrier.bodyCollider.transform.rotation, out var axis, out var depth) && depth > .025f)
                    throw new Exception("Overlap remains: " + direction + " depth=" + depth);
            }
            return "PASS: player separated from boss body on all four sides";
        }
        finally
        {
            motor.enabled = false; p.transform.position = start; motor.enabled = true;
            barrier.transform.position = bossStart; p.SyncBodyPosition();
        }
    }
}
