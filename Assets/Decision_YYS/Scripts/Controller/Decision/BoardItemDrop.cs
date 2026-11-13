using System.Collections;
using UnityEngine;

/// <summary>Projects pointer movement onto the boards and releases items into board physics.</summary>
public static class BoardItemDrop
{
    public const float DefaultLift = .20f;

    public static float PointerLift(Camera camera, Vector2 pointer, float grabY)
    {
        float rise = Mathf.Max(0f, pointer.y - grabY);
        return DefaultLift + 1.20f * Mathf.Clamp01(rise / (camera.pixelHeight * .18f));
    }

    public static Bounds ItemBounds(Transform item)
    {
        var renderers = item.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(item.position, Vector3.zero);
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    public static Vector3 PositionAbove(Collider surface, Transform item, Vector3 point, float lift)
    {
        Bounds floor = surface.bounds;
        Bounds shape = ItemBounds(item);
        Vector3 centerOffset = shape.center - item.position;
        float insetX = Mathf.Min(shape.extents.x + .025f, floor.extents.x);
        float insetZ = Mathf.Min(shape.extents.z + .025f, floor.extents.z);
        return new Vector3(
            Mathf.Clamp(point.x + centerOffset.x, floor.min.x + insetX, floor.max.x - insetX) - centerOffset.x,
            floor.max.y + item.position.y - shape.min.y + lift,
            Mathf.Clamp(point.z + centerOffset.z, floor.min.z + insetZ, floor.max.z - insetZ) - centerOffset.z);
    }

    public static bool IsOnSurface(Collider surface, Transform item)
    {
        if (surface == null) return false;
        Bounds floor = surface.bounds;
        Bounds shape = ItemBounds(item);
        return shape.center.x >= floor.min.x && shape.center.x <= floor.max.x &&
            shape.center.z >= floor.min.z && shape.center.z <= floor.max.z &&
            shape.min.y >= floor.max.y - .04f && shape.min.y <= floor.max.y + .08f;
    }

    public static Collider FindSurface(Ray ray, Camera camera, Vector2 pointer, Transform item,
        Collider first, Collider second, out Vector3 point)
    {
        if (first != null && first.Raycast(ray, out var firstHit, 1000) &&
            (second == null || !second.Raycast(ray, out var otherHit, 1000) || firstHit.distance <= otherHit.distance))
        {
            point = firstHit.point;
            return first;
        }
        if (second != null && second.Raycast(ray, out var secondHit, 1000))
        {
            point = secondHit.point;
            return second;
        }
        point = default;
        if (item == null) return null;
        Vector3 firstPoint = ClosestPoint(ray, first, item);
        Vector3 secondPoint = ClosestPoint(ray, second, item);
        float firstDistance = first == null ? float.PositiveInfinity :
            (pointer - (Vector2)camera.WorldToScreenPoint(firstPoint)).sqrMagnitude;
        float secondDistance = second == null ? float.PositiveInfinity :
            (pointer - (Vector2)camera.WorldToScreenPoint(secondPoint)).sqrMagnitude;
        if (firstDistance <= secondDistance)
        {
            point = firstPoint;
            return first;
        }
        point = secondPoint;
        return second;
    }

    private static Vector3 ClosestPoint(Ray ray, Collider surface, Transform item)
    {
        if (surface == null) return default;
        Bounds floor = surface.bounds;
        var plane = new Plane(Vector3.up, new Vector3(0f, floor.max.y, 0f));
        Vector3 point = plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : floor.center;
        return PositionAbove(surface, item, point, 0f);
    }

    public static void Hold(Rigidbody body)
    {
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = false;
    }

    public static void Release(Rigidbody body)
    {
        // Commit the drag pose before interpolation resumes on the next physics step.
        body.position = body.transform.position;
        body.rotation = body.transform.rotation;
        Physics.SyncTransforms();
        body.detectCollisions = true;
        body.isKinematic = false;
        body.useGravity = true;
        body.WakeUp();
    }

    public static IEnumerator Play(Rigidbody body)
    {
        Release(body);
        // Allow contact and settling without replacing the pose produced by physics.
        for (float elapsed = 0f; elapsed < 1.25f; elapsed += Time.fixedDeltaTime)
        {
            yield return new WaitForFixedUpdate();
            if (body == null || !body.gameObject.activeInHierarchy || body.isKinematic || body.IsSleeping()) yield break;
        }
    }
}
