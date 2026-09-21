using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class B2Pattern03 : IBattlePattern
{
    private const int SwordCount = 16;

    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        c.Arena.Mode = BattleArena.Boundary.Box;
        c.Player.SetMovementMode(PlayerController.MovementMode.Free);
        c.Player.SetVisible(true);
        c.Player.Lock(false);
        scope.OnDispose(() => c.Player.Lock(true));

        // A local sorting group confines the mask to this pattern's swords.
        var group = new GameObject("B2 Wall Sword Group", typeof(SortingGroup));
        group.transform.SetParent(scope.Root, false);
        group.GetComponent<SortingGroup>().sortingOrder = 15;
        Texture2D white = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(white, new Rect(0, 0, white.width, white.height),
            new Vector2(0.5f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
        scope.OnDispose(() => Object.Destroy(square));
        var maskObject = new GameObject("BattleBox Interior Mask", typeof(SpriteMask));
        maskObject.transform.SetParent(group.transform, false);
        var mask = maskObject.GetComponent<SpriteMask>();
        mask.sprite = square;
        mask.alphaCutoff = 0.5f;
        mask.isCustomRangeActive = true;
        mask.frontSortingOrder = 1;
        mask.backSortingOrder = -1;

        float length = Mathf.Max(0.1f, data.wallSwordLength);
        float thickness = Mathf.Max(0.02f, data.wallSwordThickness);
        float speed = Mathf.Max(0.1f, data.speed);
        float interval = Mathf.Max(0.05f, data.interval);
        float spin = Mathf.Abs(data.swordRotationSpeed);
        float reach = new Vector2(length, thickness * 0.5f).magnitude;
        float motionBound = speed + spin * Mathf.Deg2Rad * reach;
        var swords = new Transform[SwordCount];
        var startY = new float[SwordCount];
        var finished = new bool[SwordCount];
        var polygon = new List<Vector2>(12);
        var clipped = new List<Vector2>(12);
        int spawned = 0, remaining = SwordCount;
        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;

        while (remaining > 0 && c.Owner.Result == BattleResult.Running)
        {
            Rect bounds = c.Arena.Bounds;
            maskObject.transform.position = new Vector3(bounds.center.x, bounds.center.y, 0f);
            maskObject.transform.localScale = new Vector3(bounds.width, bounds.height, 1f);
            float dt = c.Clock.Delta;
            if (dt <= 0f) { yield return null; continue; }
            float nextTime = elapsed + dt;
            while (spawned < SwordCount && spawned * interval <= nextTime)
            {
                swords[spawned] = CreateSword(group.transform, square, length, thickness, spawned);
                startY[spawned] = bounds.yMin - thickness * 0.5f;
                spawned++;
            }

            Vector2 player = c.Player.transform.position;
            float playerTravel = Vector2.Distance(previousPlayer, player);
            int samples = Mathf.Max(1, Mathf.CeilToInt((motionBound * dt + playerTravel) / 0.04f));
            bool contacted = false;
            for (int sample = 1; sample <= samples; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;
                Vector2 playerAtTime = Vector2.Lerp(previousPlayer, player, blend);
                for (int i = 0; i < spawned; i++)
                {
                    if (finished[i]) continue;
                    float age = time - i * interval;
                    if (age < 0f) continue;
                    bool left = i % 2 == 0;
                    float y = startY[i] + speed * age;
                    Transform sword = swords[i];
                    sword.gameObject.SetActive(true);
                    // Keep the endpoint on the inner face of its wall.
                    Vector2 pivot = new Vector2(left ? bounds.xMin : bounds.xMax, y);
                    float angle = left ? spin * age : 180f - spin * age;
                    sword.SetPositionAndRotation(new Vector3(pivot.x, pivot.y, 0f),
                        Quaternion.Euler(0f, 0f, angle));
                    if (y - reach > bounds.yMax)
                    {
                        sword.gameObject.SetActive(false);
                        Object.Destroy(sword.gameObject);
                        finished[i] = true;
                        remaining--;
                        continue;
                    }
                    if (!contacted && HitsVisiblePart(playerAtTime, c.Player.HitRadius,
                        pivot, angle, length, thickness, bounds, polygon, clipped))
                    {
                        contacted = true;
                        c.Player.TakeDamage(1f);
                        if (c.Owner.Result != BattleResult.Running) yield break;
                    }
                }
            }
            elapsed = nextTime;
            previousPlayer = player;
            yield return null;
        }
    }

    private static Transform CreateSword(Transform parent, Sprite square, float length,
        float thickness, int index)
    {
        var pivot = new GameObject((index % 2 == 0 ? "Left" : "Right") + " Wall Sword " + (index / 2 + 1));
        pivot.transform.SetParent(parent, false);
        var blade = new GameObject("Square", typeof(SpriteRenderer));
        blade.transform.SetParent(pivot.transform, false);
        // The square extends along local +X from its endpoint, never around its center.
        blade.transform.localPosition = new Vector3(length * 0.5f, 0f, 0f);
        blade.transform.localScale = new Vector3(length, thickness, 1f);
        var renderer = blade.GetComponent<SpriteRenderer>();
        renderer.sprite = square;
        renderer.color = Color.white;
        renderer.sortingOrder = 0;
        renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        pivot.SetActive(false);
        return pivot.transform;
    }

    private static bool HitsVisiblePart(Vector2 player, float radius, Vector2 pivot,
        float angle, float length, float thickness, Rect bounds,
        List<Vector2> polygon, List<Vector2> scratch)
    {
        float radians = angle * Mathf.Deg2Rad;
        Vector2 along = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 across = new Vector2(-along.y, along.x) * (thickness * 0.5f);
        polygon.Clear();
        polygon.Add(pivot - across);
        polygon.Add(pivot + along * length - across);
        polygon.Add(pivot + along * length + across);
        polygon.Add(pivot + across);
        // Clip the rotated blade polygon to exactly the same rectangle as the mask.
        for (int edge = 0; edge < 4; edge++)
        {
            if (polygon.Count == 0) return false;
            scratch.Clear();
            Vector2 previous = polygon[polygon.Count - 1];
            float previousDistance = InsideDistance(previous, bounds, edge);
            foreach (Vector2 current in polygon)
            {
                float distance = InsideDistance(current, bounds, edge);
                if ((distance >= 0f) != (previousDistance >= 0f))
                {
                    float t = previousDistance / (previousDistance - distance);
                    scratch.Add(Vector2.LerpUnclamped(previous, current, t));
                }
                if (distance >= 0f) scratch.Add(current);
                previous = current;
                previousDistance = distance;
            }
            polygon.Clear();
            polygon.AddRange(scratch);
        }
        if (polygon.Count < 3) return false;
        float area = 0f;
        bool inside = true;
        float nearestSquared = float.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            area += a.x * b.y - a.y * b.x;
            Vector2 edge = b - a, relative = player - a;
            if (edge.x * relative.y - edge.y * relative.x < -0.000001f) inside = false;
            float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(relative, edge) / edge.sqrMagnitude) : 0f;
            nearestSquared = Mathf.Min(nearestSquared, (player - (a + edge * t)).sqrMagnitude);
        }
        // Merely touching the mask boundary has no visible area and cannot damage.
        return Mathf.Abs(area) > 0.000001f && (inside || nearestSquared <= radius * radius);
    }

    private static float InsideDistance(Vector2 point, Rect bounds, int edge)
    {
        switch (edge)
        {
            case 0: return point.x - bounds.xMin;
            case 1: return bounds.xMax - point.x;
            case 2: return point.y - bounds.yMin;
            default: return bounds.yMax - point.y;
        }
    }
}