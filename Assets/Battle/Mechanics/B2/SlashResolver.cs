using System.Collections.Generic;
using UnityEngine;

public static class SlashResolver
{
    public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 direction = b - a;
        float t = direction.sqrMagnitude < .000001f ? 0 : Mathf.Clamp01(Vector2.Dot(point - a, direction) / direction.sqrMagnitude);
        return Vector2.Distance(point, a + direction * t);
    }
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float x = Cross(b - a, p - a), y = Cross(c - b, p - b), z = Cross(a - c, p - c);
        if (Mathf.Abs(Cross(b - a, c - a)) < .0001f) return false;
        return (x >= 0 && y >= 0 && z >= 0) || (x <= 0 && y <= 0 && z <= 0);
    }
    static bool Segments(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float width)
    {
        float cross = Cross(b - a, d - c);
        if (Mathf.Abs(cross) > .00001f)
        {
            float t = Cross(c - a, d - c) / cross, u = Cross(c - a, b - a) / cross;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1) return true;
        }
        return DistanceToSegment(a, c, d) <= width || DistanceToSegment(b, c, d) <= width ||
            DistanceToSegment(c, a, b) <= width || DistanceToSegment(d, a, b) <= width;
    }
    public static bool Hits(Camera camera, MeshFilter filter, Vector2 start, Vector2 end, float width)
    {
        Vector3[] vertices = filter.sharedMesh.vertices;
        int[] triangles = filter.sharedMesh.triangles;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = camera.WorldToScreenPoint(filter.transform.TransformPoint(vertices[i]));
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
            if (a.z <= 0 || b.z <= 0 || c.z <= 0) continue;
            if (Inside(start, a, b, c) || Inside(end, a, b, c) ||
                Segments(start, end, a, b, width / 2) || Segments(start, end, b, c, width / 2) || Segments(start, end, c, a, width / 2)) return true;
        }
        return false;
    }
    // Clip each triangle against the cutting plane and cap the convex cross section.
    // Targets are convex primitives, so an angle-sorted cap is sufficient.
    public static Mesh Clip(Mesh source, Plane plane, bool positive)
    {
        var output = new List<Vector3>();
        var indices = new List<int>();
        var cuts = new List<Vector3>();
        Vector3[] vertices = source.vertices;
        int[] triangles = source.triangles;
        float sign = positive ? 1 : -1;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var polygon = new List<Vector3>(4);
            for (int j = 0; j < 3; j++)
            {
                Vector3 a = vertices[triangles[i + j]], b = vertices[triangles[i + (j + 1) % 3]];
                float da = plane.GetDistanceToPoint(a) * sign, db = plane.GetDistanceToPoint(b) * sign;
                if (da >= 0) polygon.Add(a);
                if ((da >= 0) != (db >= 0))
                {
                    Vector3 point = Vector3.Lerp(a, b, da / (da - db));
                    polygon.Add(point);
                    if (!cuts.Exists(v => (v - point).sqrMagnitude < .000001f)) cuts.Add(point);
                }
            }
            for (int j = 1; j + 1 < polygon.Count; j++) AddTriangle(output, indices, polygon[0], polygon[j], polygon[j + 1]);
        }
        if (cuts.Count >= 3)
        {
            Vector3 center = Vector3.zero;
            foreach (Vector3 v in cuts) center += v;
            center /= cuts.Count;
            Vector3 u = (cuts[0] - center).normalized, vAxis = Vector3.Cross(plane.normal, u);
            cuts.Sort((a, b) => Mathf.Atan2(Vector3.Dot(a - center, vAxis), Vector3.Dot(a - center, u)).CompareTo(
                Mathf.Atan2(Vector3.Dot(b - center, vAxis), Vector3.Dot(b - center, u))));
            for (int j = 0; j < cuts.Count; j++) AddTriangle(output, indices, center, cuts[j], cuts[(j + 1) % cuts.Count]);
        }
        var mesh = new Mesh { name = "Slash Fragment" };
        mesh.SetVertices(output); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
    static void AddTriangle(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c)
    {
        int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
        indices.Add(n); indices.Add(n + 1); indices.Add(n + 2);
    }
}
