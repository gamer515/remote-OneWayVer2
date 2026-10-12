using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BDPattern05 : IBattlePattern
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
        scope.OnDispose( () => BattlePlayerPreparationRunner.EndSpecial(c));

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
        const int shotCount = 7;
        float total = interval * (shotCount - 1) + lifetime;

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
        float thickness = Mathf.Max(0.05f, data.bdCrossPlatformSize.y);
        float playerHeight = c.Player.HalfSize.y * 2f;

        if (data.bdCrossLowerHeight <= thickness ||
            data.bdCrossUpperHeight - data.bdCrossLowerHeight
                <= thickness + playerHeight ||
            box.height - data.bdCrossUpperHeight <= playerHeight)
        {
            Debug.LogError(
                "발판 높이를 확인하세요. 발판 사이와 상단에 플레이어 공간이 필요합니다.");
            yield break;
        }

        var platformRoot = new GameObject("양방향 발판");
        platformRoot.transform.SetParent(scope.Root, false);

        platformRoot.AddComponent<BDCrossPlatforms>()
            .Initialize(c, data, box, square);

        var emitters = new List<Emitter>(shotCount * 2);

        int previousHorizontal = -1;

        for (int shot = 0; shot < shotCount; shot++)
        {
            int horizontal = PickHorizontal(previousHorizontal);

            // 왼쪽, 오른쪽, 없음은 직전 선택과 관계없이 뽑는다.
            int vertical = Random.Range(0, 3);

            AddRandomWave(
                emitters,
                horizontal,
                vertical,
                shot * interval,
                box,
                screen,
                c.Box.wallThickness,
                data,
                scope.Root,
                square,
                triangle,
                laserMaterial);

            previousHorizontal = horizontal;
        }

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

    private static void AddRandomWave(
     List<Emitter> emitters,
     int horizontal,
     int vertical,
     float startTime,
     Rect box,
     Rect screen,
     float wallThickness,
     BattlePatternData data,
     Transform parent,
     Sprite square,
     Mesh triangle,
     Material material)
    {
        float thickness = Mathf.Max(
            0.05f, data.bdCrossPlatformSize.y);

        float lowerTop = box.yMin + data.bdCrossLowerHeight;
        float lowerBottom = lowerTop - thickness;

        float upperTop = box.yMin + data.bdCrossUpperHeight;
        float upperBottom = upperTop - thickness;

        // 구역 안쪽의 중앙 높이에서 가로 레이저를 발사한다.
        float[] laneY =
        {
        (box.yMin + lowerBottom) * 0.5f,
        (lowerTop + upperBottom) * 0.5f,
        (upperTop + box.yMax) * 0.5f
    };

        int lane = horizontal / 2;
        bool fromLeft = horizontal % 2 == 0;

        float size = Mathf.Max(0.1f, data.bdLaserEmitterSize);
        float gap = Mathf.Max(0f, data.bdLaserBoxGap) + wallThickness;

        Vector2 horizontalTarget = new Vector2(
            fromLeft
                ? box.xMin - gap - size * 0.5f
                : box.xMax + gap + size * 0.5f,
            laneY[lane]);

        AddEmitter(
            emitters,
            horizontalTarget,
            fromLeft ? Vector2.right : Vector2.left,
            startTime,
            screen,
            data,
            parent,
            square,
            triangle,
            material);

        if (vertical == 2)
            return;

        // 세로 레이저는 상자의 왼쪽 또는 오른쪽 1/4 지점에서 내려온다.
        float x = Mathf.Lerp(
            box.xMin, box.xMax,
            vertical == 0 ? 0.25f : 0.75f);

        Vector2 verticalTarget = new Vector2(
            x,
            box.yMax + gap + size * 0.5f);

        AddEmitter(
            emitters,
            verticalTarget,
            Vector2.down,
            startTime,
            screen,
            data,
            parent,
            square,
            triangle,
            material);
    }

    private static void AddEmitter(
        List<Emitter> emitters,
        Vector2 target,
        Vector2 direction,
        float startTime,
        Rect screen,
        BattlePatternData data,
        Transform parent,
        Sprite square,
        Mesh triangle,
        Material material)
    {
        int number = emitters.Count + 1;
        float size = Mathf.Max(0.1f, data.bdLaserEmitterSize);

        var body = CreateRenderer(
            parent, square, "패턴 05 발사기 " + number, 16);

        float angle =
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        body.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        body.transform.localScale = new Vector3(size, size, 1f);

        Vector2 tip = target + direction * size * 0.5f;
        Vector2 normal = new Vector2(-direction.y, direction.x);

        float length = screen.size.magnitude
            + Vector2.Distance(tip, screen.center);

        float width = 2f * length * Mathf.Tan(
            Mathf.Clamp(data.bdLaserSpreadAngle, 1f, 60f)
            * 0.5f * Mathf.Deg2Rad);

        var beamObject = new GameObject(
            "패턴 05 레이저 " + number,
            typeof(MeshFilter),
            typeof(MeshRenderer));

        beamObject.transform.SetParent(parent, false);
        beamObject.GetComponent<MeshFilter>().sharedMesh = triangle;

        var beam = beamObject.GetComponent<MeshRenderer>();
        beam.sharedMaterial = material;
        beam.sortingOrder = 15;
        beam.shadowCastingMode = ShadowCastingMode.Off;
        beam.receiveShadows = false;
        beam.enabled = false;

        beam.transform.SetPositionAndRotation(
            tip, Quaternion.Euler(0f, 0f, angle));

        beam.transform.localScale = new Vector3(length, width, 1f);

        Vector2 outside = OutsidePosition(
            screen, target, -direction, size);

        body.transform.position = outside;

        emitters.Add(new Emitter
        {
            body = body,
            beam = beam,
            outside = outside,
            target = target,
            tip = tip,
            edgeA = tip + direction * length + normal * width * 0.5f,
            edgeB = tip + direction * length - normal * width * 0.5f,
            startTime = startTime
        });
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

    private static int PickHorizontal(int previous)
    {
        // A왼쪽, A오른쪽, B왼쪽, B오른쪽, C왼쪽, C오른쪽.
        int[] weights = { 3, 3, 1, 1, 1, 1 };

        int total = 0;

        for (int i = 0; i < weights.Length; i++)
        {
            if (i != previous)
                total += weights[i];
        }

        int roll = Random.Range(0, total);

        for (int i = 0; i < weights.Length; i++)
        {
            // 직전과 완전히 같은 조합은 뽑지 않는다.
            if (i == previous)
                continue;

            if (roll < weights[i])
                return i;

            roll -= weights[i];
        }

        return (previous + 1) % weights.Length;
    }
}