using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class ScreenBreakController
{
    public IEnumerator Run(BattleContext c, BattleStepScope scope, float duration)
    {
        c.Player.Lock(true);
        c.Player.SetVisible(false);
        c.Overlay.color = Color.white;
        yield return new WaitForSecondsRealtime(.6f);
        Rect r = c.Arena.ScreenRect;
        const int columns = 5, rows = 4;
        var points = new Vector3[columns + 1, rows + 1];
        for (int y = 0; y <= rows; y++)
        for (int x = 0; x <= columns; x++)
        {
            float px = Mathf.Lerp(r.xMin, r.xMax, x / (float)columns);
            float py = Mathf.Lerp(r.yMin, r.yMax, y / (float)rows);
            if (x > 0 && x < columns) px += Random.Range(-.8f, .8f);
            if (y > 0 && y < rows) py += Random.Range(-.7f, .7f);
            points[x, y] = new Vector3(px, py, -2);
        }
        var shards = new List<Transform>();
        var velocities = new List<Vector3>();
        var cracks = new List<Transform>();
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            MakeShard(c, scope, points[x, y], points[x + 1, y], points[x + 1, y + 1], shards, velocities, cracks);
            MakeShard(c, scope, points[x, y], points[x + 1, y + 1], points[x, y + 1], shards, velocities, cracks);
        }
        c.Overlay.color = Color.clear;
        for (int i = 0; i < cracks.Count; i++)
        {
            cracks[i].gameObject.SetActive(true);
            if (i % 9 == 0) yield return new WaitForSecondsRealtime(.035f);
        }
        yield return new WaitForSecondsRealtime(.25f);
        c.Player.transform.position = new Vector3(r.center.x, Mathf.Lerp(r.yMin, r.yMax, .3f), 0);
        c.Player.SetVisible(true);
        float seconds = Mathf.Max(2.5f, duration);
        for (float elapsed = 0; elapsed < seconds; elapsed += c.Clock.Delta)
        {
            for (int i = 0; i < shards.Count; i++)
            {
                float delay = (i % 7) * .035f;
                if (elapsed < delay) continue;
                velocities[i] += Vector3.down * (22f * c.Clock.Delta);
                shards[i].position += velocities[i] * c.Clock.Delta;
                shards[i].Rotate(0, 0, (i % 2 == 0 ? 8 : -8) * c.Clock.Delta);
            }
            yield return null;
        }
    }
    void MakeShard(BattleContext c, BattleStepScope scope, Vector3 a, Vector3 b, Vector3 d,
        List<Transform> shards, List<Vector3> velocities, List<Transform> cracks)
    {
        Vector3 center = (a + b + d) / 3;
        var go = c.Primitive("White Shard", PrimitiveType.Cube, scope.Root, center, Vector3.one);
        var mesh = new Mesh { name = "Screen Shard", vertices = new[] { a - center, b - center, d - center }, triangles = new[] { 0, 1, 2 } };
        mesh.RecalculateBounds();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        scope.OnDispose(() => Object.Destroy(mesh));
        var white = new MaterialPropertyBlock(); white.SetColor("_BaseColor", Color.white);
        go.GetComponent<Renderer>().SetPropertyBlock(white);
        Vector3[] corners = { a, b, d };
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = corners[i], q = corners[(i + 1) % 3];
            var edge = c.Primitive("Crack", PrimitiveType.Cube, go.transform, (p + q) / 2 + Vector3.back * .02f,
                new Vector3(Vector3.Distance(p, q), .035f, .01f));
            edge.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(q.y - p.y, q.x - p.x) * Mathf.Rad2Deg);
            var black = new MaterialPropertyBlock(); black.SetColor("_BaseColor", Color.black);
            edge.GetComponent<Renderer>().SetPropertyBlock(black);
            edge.SetActive(false); cracks.Add(edge.transform);
        }
        shards.Add(go.transform); velocities.Add(new Vector3(Random.Range(-.5f, .5f), Random.Range(-1f, 0), 0));
    }
}
