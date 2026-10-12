using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class B2Pattern08 : IBattlePattern
{
    
    private const float FaintAlpha = 0.25f;

    // 첫 검 생성 기준 시간.
    private static readonly float[] SpawnTimes =
    {
        0f, 1f, 2f,
        3f, 3.8f, 4.6f, 5.4f, 6.2f, 7f,
        7.8f, 7.8f, 7.8f
    };

    private sealed class Sword
    {
        public SpriteRenderer renderer;
        public Vector2 origin;
        public float birth;
        public float startAngle;

        public bool aimed;
        public float extraAngle;
        public Vector2 direction;
    }

    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {
        // 상자 크기와 위치는 공통 상자 준비에서 적용한다.
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);

        // 종료 후 다음 단계가 준비될 때까지 이동을 잠근다.
        scope.OnDispose(() => c.Player.Lock(true));

        // 검의 생성 위치와 화면 밖 이탈을 판단하는 데 사용한다.
        Rect screen = c.Arena.ScreenRect;

        Texture2D texture = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            Vector2.one * 0.5f,
            texture.width,
            0,
            SpriteMeshType.FullRect);

        scope.OnDispose(() => Object.Destroy(square));

        // 세로 Square의 위쪽(+Y)을 검 머리로 취급.
        Vector2 size = new Vector2(
            Mathf.Max(0.02f, data.swordSize.x),
            Mathf.Max(0.1f, data.swordSize.y));

        Vector2 half = size * 0.5f;
        float radius = half.magnitude;

        float speed = Mathf.Max(0.1f, data.speed);

        // 양수 방향으로만 회전하여 총 2 + 0.n회전 유지.
        float spin = Mathf.Max(1f, Mathf.Abs(data.swordRotationSpeed));
        float oneTurnSeconds = 360f / spin;
        float twoTurnSeconds = oneTurnSeconds * 2f;

        // 벽 두께까지 포함한 생성 금지 영역.
        float wallHalf = c.Box.wallThickness * 0.5f;

        Rect forbidden = Rect.MinMaxRect(
            c.Box.leftWall.position.x - wallHalf,
            c.Box.bottomWall.position.y - wallHalf,
            c.Box.rightWall.position.x + wallHalf,
            c.Box.topWall.position.y + wallHalf);

        var swords = new List<Sword>(12);

        int spawned = 0;
        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;

        while ((spawned < SpawnTimes.Length || swords.Count > 0) &&
               c.Owner.Result == BattleResult.Running)
        {
            float dt = c.Clock.Delta;

            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            float nextTime = elapsed + dt;

            while (spawned < SpawnTimes.Length &&
                   SpawnTimes[spawned] <= nextTime)
            {
                Vector2 origin = FindSpawnPoint(
                    screen, forbidden, radius);

                var go = new GameObject(
                    "B2 Aimed Sword " + (spawned + 1),
                    typeof(SpriteRenderer));

                go.transform.SetParent(scope.Root, false);
                go.transform.localScale =
                    new Vector3(size.x, size.y, 1f);

                var renderer = go.GetComponent<SpriteRenderer>();
                renderer.sprite = square;
                renderer.sortingOrder = 15;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;

                swords.Add(new Sword
                {
                    renderer = renderer,
                    origin = origin,
                    birth = SpawnTimes[spawned],
                    startAngle = Random.Range(0f, 360f)
                });

                spawned++;
            }

            Vector2 player = c.Player.transform.position;

           
            float motionBound = speed + spin * Mathf.Deg2Rad * radius;

            int samples = Mathf.Max(1, Mathf.CeilToInt(
                (motionBound * dt +
                 Vector2.Distance(previousPlayer, player)) / 0.04f));

            bool contacted = false;

            for (int sample = 1; sample <= samples; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;

                Vector2 playerAtTime =
                    Vector2.Lerp(previousPlayer, player, blend);

                for (int i = swords.Count - 1; i >= 0; i--)
                {
                    Sword sword = swords[i];
                    float age = time - sword.birth;

                    if (age < 0f)
                        continue;

                    // 정확히 두 바퀴를 마친 시점의 목표 위치를 고정.
                    if (!sword.aimed && age >= twoTurnSeconds)
                    {
                        float aimTime = sword.birth + twoTurnSeconds;

                        float aimBlend = Mathf.Clamp01(
                            (aimTime - elapsed) / dt);

                        Vector2 target = Vector2.Lerp(
                            previousPlayer, player, aimBlend);

                        Vector2 direction = target - sword.origin;

                        if (direction.sqrMagnitude < 0.0001f)
                            direction = Vector2.down;

                        sword.direction = direction.normalized;

                        // 검 머리가 local +Y이므로 90도 보정.
                        float targetAngle = Mathf.Atan2(
                            sword.direction.y,
                            sword.direction.x) * Mathf.Rad2Deg - 90f;

                        // 역회전하지 않고 같은 방향으로 0~360도 추가.
                        sword.extraAngle = Mathf.Repeat(
                            targetAngle - sword.startAngle, 360f);

                        sword.aimed = true;
                    }

                    float launchAge = sword.aimed
                        ? twoTurnSeconds + sword.extraAngle / spin
                        : float.PositiveInfinity;

                    bool flying = sword.aimed && age >= launchAge;

                    float angle = flying
                        ? sword.startAngle + 720f + sword.extraAngle
                        : sword.startAngle + spin * age;

                    Vector2 position = sword.origin;

                    if (flying)
                    {
                        position += sword.direction *
                            (speed * (age - launchAge));
                    }

                    sword.renderer.enabled = true;
                    sword.renderer.color = new Color(
                        1f, 1f, 1f,
                        age < oneTurnSeconds ? FaintAlpha : 1f);

                    sword.renderer.transform.SetPositionAndRotation(
                        new Vector3(position.x, position.y, 0f),
                        Quaternion.Euler(0f, 0f, angle));

                    // 검 전체가 화면 바깥으로 나가면 제거.
                    if (flying &&
                        (position.x + radius < screen.xMin ||
                         position.x - radius > screen.xMax ||
                         position.y + radius < screen.yMin ||
                         position.y - radius > screen.yMax))
                    {
                        sword.renderer.gameObject.SetActive(false);
                        Object.Destroy(sword.renderer.gameObject);
                        swords.RemoveAt(i);
                        continue;
                    }

                    if (flying && !contacted && Touches(
                        playerAtTime,
                        c.Player.HitRadius + 0.02f,
                        position, angle, half))
                    {
                        contacted = true;
                        c.Player.TakeDamage(1f);

                        if (c.Owner.Result != BattleResult.Running)
                            yield break;
                    }
                }
            }

            elapsed = nextTime;
            previousPlayer = player;

            yield return null;
        }
    }

    private static Vector2 FindSpawnPoint(
        Rect screen, Rect forbidden, float radius)
    {
        // 회전 중인 검 전체가 상자와 겹치지 않는 위치 선택.
        for (int attempt = 0; attempt < 2000; attempt++)
        {
            Vector2 point = new Vector2(
                Random.Range(screen.xMin, screen.xMax),
                Random.Range(screen.yMin, screen.yMax));

            Vector2 nearest = new Vector2(
                Mathf.Clamp(point.x, forbidden.xMin, forbidden.xMax),
                Mathf.Clamp(point.y, forbidden.yMin, forbidden.yMax));

            if ((point - nearest).sqrMagnitude >
                (radius + 0.05f) * (radius + 0.05f))
            {
                return point;
            }
        }

        throw new System.InvalidOperationException(
            "Pattern 08: 검을 생성할 공간이 부족합니다. " +
            "검 길이나 BattleBox 크기를 줄여주세요.");
    }

    private static bool Touches(
        Vector2 player, float radius,
        Vector2 center, float angle, Vector2 half)
    {
        Vector2 delta = player - center;
        float radians = angle * Mathf.Deg2Rad;

        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        Vector2 local = new Vector2(
            cos * delta.x + sin * delta.y,
            -sin * delta.x + cos * delta.y);

        Vector2 nearest = new Vector2(
            Mathf.Clamp(local.x, -half.x, half.x),
            Mathf.Clamp(local.y, -half.y, half.y));

        return (local - nearest).sqrMagnitude <= radius * radius;
    }

    
}