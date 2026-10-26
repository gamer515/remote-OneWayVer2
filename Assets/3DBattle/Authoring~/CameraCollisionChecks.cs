using System;
using System.Reflection;
using UnityEngine;
using OneWay.Battle3D;
public static class CameraCollisionChecks
{
    public static string Test()
    {
        var target = new GameObject("CameraTestTarget");
        var camera = new GameObject("CameraTest");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            target.transform.position = new Vector3(0, 10000, 0);
            var follow = camera.AddComponent<BattleFollowCamera>();
            follow.target = target.transform; follow.offset = new Vector3(0, 0, -10);
            follow.lookOffset = Vector3.up; follow.Snap();
            var rotation = camera.transform.rotation;
            wall.transform.position = target.transform.position + new Vector3(0, 1, -5);
            wall.transform.localScale = new Vector3(10, 10, 1);
            Physics.SyncTransforms(); follow.Orbit(Vector2.zero);
            if (follow.CurrentDistance > 4.4f) throw new Exception("Obstacle not avoided");
            if (Quaternion.Angle(rotation, camera.transform.rotation) > .01f) throw new Exception("Angle changed");
            float blocked = follow.CurrentDistance;
            wall.SetActive(false); Physics.SyncTransforms();
            var apply = typeof(BattleFollowCamera).GetMethod("ApplyPose", BindingFlags.NonPublic | BindingFlags.Instance);
            apply.Invoke(follow, new object[] { .016f });
            if (follow.CurrentDistance <= blocked || follow.CurrentDistance >= 9.9f) throw new Exception("Return not smoothed");
            for (int i=0; i<120; i++) apply.Invoke(follow, new object[] { .016f });
            if (follow.CurrentDistance < 9.99f) throw new Exception("Return incomplete");
            wall.SetActive(true); follow.ignoredColliders = new [] { wall.GetComponent<Collider>() };
            Physics.SyncTransforms(); follow.Orbit(Vector2.zero);
            if (follow.CurrentDistance < 9.99f) throw new Exception("Ignore list failed");
            follow.ignoredColliders = new Collider[0];
            var boundary = wall.AddComponent<BattleArenaBoundary>(); boundary.walls = new [] { wall.GetComponent<BoxCollider>() };
            follow.Orbit(Vector2.zero);
            if (follow.CurrentDistance < 9.99f) throw new Exception("Boundary exclusion failed");
            return "PASS: obstacle pull-in, preserved angle, smooth return, ignored collider and arena wall exclusion";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(camera); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(wall);
        }
    }
}
