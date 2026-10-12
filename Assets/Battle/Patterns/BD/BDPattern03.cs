using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BDPattern03 : IBattlePattern
{
    private sealed class Emitter
    {
        public SpriteRenderer body;
        public MeshRenderer beam;
        public Vector2 outside;
        public Vector2 target;
        public Vector2 tip;
        public Vector2 edgeA;
        public Vector2 edgeB;
        public float startTime;
    }

    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);
        scope.OnDispose(() => c.Player.Lock(true));

        float interval = Mathf.Max(0.1f, data.bdLaserWaveInterval);
        float enter = Mathf.Max(0.01f, data.bdLaserEnterSeconds);
        float aim = Mathf.Max(0f, data.bdLaserAimSeconds);
        float hold = Mathf.Max(0.01f, data.bdLaserHoldSeconds);
        float fade = Mathf.Max(0f, data.bdLaserFadeSeconds);
        float exit = Mathf.Max(0.01f, data.bdLaserExitSeconds);
        float fireAt = enter + aim;
        float fadeAt = fireAt + hold;
        float exitAt = fadeAt + fade;
        float lifetime = exitAt + exit;
        float total = interval * 3f + lifetime;

        Texture2D white = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(white,
            new Rect(0f, 0f, white.width, white.height),
            new Vector2(0.5f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
        scope.OnDispose(() => Object.Destroy(square));
        // 삼각형 메시를 직접 만들어 표시와 충돌의 꼭짓점을 일치시킨다.
        var triangle = new Mesh { name = "BD 삼각형 레이저 메시" };
        triangle.vertices = new[]
        {
            Vector3.zero, new Vector3(1f, 0.5f, 0f), new Vector3(1f, -0.5f, 0f)
        };
        triangle.triangles = new[] { 0, 1, 2 };
        triangle.uv = new[] { new Vector2(0f, 0.5f), Vector2.one, Vector2.right };
        triangle.colors = new[] { Color.white, Color.white, Color.white };
        triangle.RecalculateBounds();
        scope.OnDispose(() => Object.Destroy(triangle));
        var laserMaterial = new Material(Shader.Find("Sprites/Default"));
        laserMaterial.mainTexture = white;
        scope.OnDispose(() => Object.Destroy(laserMaterial));
        var beamProperties = new MaterialPropertyBlock();
        Rect box = c.Arena.Bounds;
        Rect screen = c.Arena.ScreenRect;
        var emitters = new List<Emitter>(14);
        for (int wave = 0; wave < 4; wave++)
            AddWave(emitters, wave, interval, box, screen, c.Box.wallThickness,
                data, scope.Root, square, triangle, laserMaterial);

        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;
        while (elapsed < total && c.Owner.Result == BattleResult.Running)
        {
            float dt = Mathf.Min(c.Clock.Delta, total - elapsed);
            if (dt <= 0f)
            {
                previousPlayer = c.Player.transform.position;
                yield return null;
                continue;
            }

            float nextTime = elapsed + dt;
            Vector2 player = c.Player.transform.position;
            bool hit = false;
            foreach (var emitter in emitters)
            {
                float age = nextTime - emitter.startTime;
                emitter.body.enabled = age >= 0f && age < lifetime;
                if (age < enter)
                    emitter.body.transform.position = Vector2.Lerp(
                        emitter.outside, emitter.target, Mathf.Clamp01(age / enter));
                else if (age < exitAt)
                    emitter.body.transform.position = emitter.target;
                else
                    emitter.body.transform.position = Vector2.Lerp(
                        emitter.target, emitter.outside, Mathf.Clamp01((age - exitAt) / exit));

                float alpha = age < fadeAt ? 1f
                    : fade <= 0f ? 0f : 1f - Mathf.Clamp01((age - fadeAt) / fade);
                emitter.beam.enabled = age >= fireAt && age < exitAt;
                beamProperties.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
                emitter.beam.SetPropertyBlock(beamProperties);

                // 이번 프레임과 실제 발사 구간이 겹친 시간만 판정한다.
                // 흐려지는 동안과 발사기의 이동 중에는 피해를 주지 않는다.
                float activeStart = Mathf.Max(elapsed, emitter.startTime + fireAt);
                float activeEnd = Mathf.Min(nextTime, emitter.startTime + fadeAt);
                if (!hit && activeEnd > activeStart && data.bdLaserDamage > 0f)
                {
                    Vector2 from = Vector2.Lerp(previousPlayer, player, (activeStart - elapsed) / dt);
                    Vector2 to = Vector2.Lerp(previousPlayer, player, (activeEnd - elapsed) / dt);
                    hit = SweptCircleTouches(from, to, c.Player.HitRadius, emitter);
                }
            }

            if (hit)
            {
                c.Player.TakeDamage(data.bdLaserDamage);
                if (c.Owner.Result != BattleResult.Running)
                    yield break;
            }
            previousPlayer = player;
            elapsed = nextTime;
            yield return null;
        }
    }

    private static void AddWave(List<Emitter> emitters, int wave, float interval,
        Rect box, Rect screen, float wallThickness, BattlePatternData data,
        Transform parent, Sprite square, Mesh triangle, Material laserMaterial)
    {
        Vector2 center = box.center;
        Vector2 half = box.size * 0.5f;
        float scale = wave == 3 ? Mathf.Max(1f, data.bdLaserFinalScale) : 1f;
        float size = Mathf.Max(0.1f, data.bdLaserEmitterSize) * scale;
        float gap = Mathf.Max(0f, data.bdLaserBoxGap) + wallThickness;
        int count = wave == 3 ? 2 : 4;

        for (int i = 0; i < count; i++)
        {
            Vector2 target;
            Vector2 direction;
            if (wave == 1)
            {
                // 네 모서리에서는 발사기의 앞면이 중심을 향한다.
                Vector2 corner = new Vector2(i % 2 == 0 ? -1f : 1f, i < 2 ? 1f : -1f);
                target = center + Vector2.Scale(half, corner)
                    + corner * (gap + size * 0.707107f);
                direction = (center - target).normalized;
            }
            else if (wave == 3)
            {
                direction = i == 0 ? Vector2.right : Vector2.left;
                target = center - direction * (half.x + gap + size * 0.5f)
                    + Vector2.down * half.y * 0.3f;
            }
            else
            {
                direction = i == 0 ? Vector2.down : i == 1 ? Vector2.right
                    : i == 2 ? Vector2.left : Vector2.up;
                float distance = (i == 0 || i == 3 ? half.y : half.x) + gap + size * 0.5f;
                target = center - direction * distance;
                if (wave == 0)
                    target += i == 0 ? Vector2.left * half.x * 0.7f
                        : i == 1 ? Vector2.up * half.y * 0.65f
                        : i == 2 ? Vector2.down * half.y * 0.65f
                        : Vector2.right * half.x * 0.7f;
            }

            var body = CreateRenderer(parent, square, "레이저 발사기 " + (wave + 1) + "-" + (i + 1), 16);
            var beamObject = new GameObject("삼각형 레이저 " + (wave + 1) + "-" + (i + 1), typeof(MeshFilter), typeof(MeshRenderer));
            beamObject.transform.SetParent(parent, false);
            beamObject.GetComponent<MeshFilter>().sharedMesh = triangle;
            var beam = beamObject.GetComponent<MeshRenderer>();
            beam.sharedMaterial = laserMaterial;
            beam.sortingOrder = 15;
            beam.shadowCastingMode = ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.enabled = false;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            body.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            body.transform.localScale = new Vector3(size, size, 1f);

            Vector2 tip = target + direction * size * 0.5f;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            float length = screen.size.magnitude + Vector2.Distance(tip, screen.center);
            float width = 2f * length * Mathf.Tan(
                Mathf.Clamp(data.bdLaserSpreadAngle, 1f, 60f) * 0.5f * Mathf.Deg2Rad) * scale;
            beam.transform.SetPositionAndRotation(tip,
                Quaternion.Euler(0f, 0f, angle));
            beam.transform.localScale = new Vector3(length, width, 1f);

            Vector2 outside = OutsidePosition(screen, target, -direction, size);
            body.transform.position = outside;
            emitters.Add(new Emitter
            {
                body = body, beam = beam, target = target, outside = outside,
                tip = tip,
                edgeA = tip + direction * length + normal * width * 0.5f,
                edgeB = tip + direction * length - normal * width * 0.5f,
                startTime = wave * interval
            });
        }
    }

    private static SpriteRenderer CreateRenderer(Transform parent, Sprite sprite, string name, int order)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        return renderer;
    }

    private static Vector2 OutsidePosition(Rect screen, Vector2 target, Vector2 outward, float size)
    {
        // 회전한 사각형 전체가 화면 밖에 놓이도록 거리를 구한다.
        float margin = size * 0.707107f + 0.3f;
        float tx = outward.x > 0.0001f ? (screen.xMax + margin - target.x) / outward.x
            : outward.x < -0.0001f ? (screen.xMin - margin - target.x) / outward.x : float.PositiveInfinity;
        float ty = outward.y > 0.0001f ? (screen.yMax + margin - target.y) / outward.y
            : outward.y < -0.0001f ? (screen.yMin - margin - target.y) / outward.y : float.PositiveInfinity;
        return target + outward * Mathf.Max(0.5f, Mathf.Min(tx, ty));
    }

    private static bool SweptCircleTouches(Vector2 from, Vector2 to, float radius, Emitter emitter)
    {
        Vector2 a = emitter.tip, b = emitter.edgeA, c = emitter.edgeB;
        if (InsideTriangle(from, a, b, c) || InsideTriangle(to, a, b, c))
            return true;
        return SegmentsNear(from, to, a, b, radius)
            || SegmentsNear(from, to, b, c, radius)
            || SegmentsNear(from, to, c, a, radius);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static bool InsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float ab = Cross(b - a, point - a);
        float bc = Cross(c - b, point - b);
        float ca = Cross(a - c, point - c);
        return (ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f);
    }

    private static bool SegmentsNear(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float radius)
    {
        Vector2 travel = b - a, edge = d - c;
        float denominator = Cross(travel, edge);
        if (Mathf.Abs(denominator) > 0.000001f)
        {
            float t = Cross(c - a, edge) / denominator;
            float u = Cross(c - a, travel) / denominator;
            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
                return true;
        }
        return SlashResolver.DistanceToSegment(a, c, d) <= radius
            || SlashResolver.DistanceToSegment(b, c, d) <= radius
            || SlashResolver.DistanceToSegment(c, a, b) <= radius
            || SlashResolver.DistanceToSegment(d, a, b) <= radius;
    }
}