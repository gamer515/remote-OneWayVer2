using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class B2Pattern10 : IBattlePattern
{
    // ───── 직접 조절하는 설정 ─────

    private const int RoundTripCount = 6;

    private const float GeneratorSpeed = 6f;
    private const float GeneratorHeight = 2f;

    private const float ShotInterval = 0.05f;
    private const float BulletSpeed = 6f;
    private const float Damage = 1f;

    private static readonly Vector2 GeneratorSize =
        new Vector2(1f, 0.5f);

    private static readonly Vector2 BulletSize =
        new Vector2(0.18f, 0.45f);

    private static readonly Color RedColor =
        new Color(1f, 0.25f, 0.25f);

    private static readonly Color GreenColor =
        new Color(0.35f, 1f, 0.35f);

    private sealed class Bullet
    {
        public Transform visual;
        public Vector2 origin;
        public float birth;
        public bool isRed;
    }

    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {
        // 이 패턴에 필요한 이동 방식과 시작 위치를 적용한다.
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);

        // 다음 단계가 준비될 때까지 이동을 잠근다.
        scope.OnDispose(() => c.Player.Lock(true));

        Texture2D texture = Texture2D.whiteTexture;

        Sprite square = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            Vector2.one * 0.5f,
            texture.width,
            0,
            SpriteMeshType.FullRect
        );

        scope.OnDispose(() => Object.Destroy(square));

       
        float leftX = c.Box.leftWall.position.x;
        float rightX = c.Box.rightWall.position.x;
        float width = rightX - leftX;

        if (width <= 0f)
        {
            throw new System.InvalidOperationException(
                "Pattern10: BattleBoxの左右の壁位置を確認してください。");
        }

        float generatorY =
            c.Box.topWall.position.y + GeneratorHeight;

        float oneWaySeconds = width / GeneratorSpeed;
        float totalSeconds =
            oneWaySeconds * 2f * RoundTripCount;

        Transform redGenerator = MakeSquare(
            "Red Bullet Generator",
            square, scope.Root,
            GeneratorSize, Color.white, 20);

        Transform greenGenerator = MakeSquare(
            "Green Bullet Generator",
            square, scope.Root,
            GeneratorSize, Color.white, 20);

        redGenerator.position = new Vector3(leftX, generatorY, 0f);
        greenGenerator.position = new Vector3(rightX, generatorY, 0f);

        var bullets = new List<Bullet>();

        Vector2 half = BulletSize * 0.5f;

        // 화면 및 상자 아래로 완전히 지나간 탄환 제거.
        float removeY = Mathf.Min(
            c.Arena.ScreenRect.yMin,
            c.Arena.Bounds.yMin);

        float elapsed = 0f;
        int shotIndex = 0;

        Vector2 previousPlayer = c.Player.transform.position;

        while ((elapsed < totalSeconds || bullets.Count > 0) &&
               c.Owner.Result == BattleResult.Running)
        {
            float dt = c.Clock.Delta;

            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            float nextTime = elapsed + dt;

            
            float generatorTime = Mathf.Min(nextTime, totalSeconds);

            redGenerator.position = new Vector3(
                GeneratorX(generatorTime, true, leftX, rightX),
                generatorY, 0f);

            greenGenerator.position = new Vector3(
                GeneratorX(generatorTime, false, leftX, rightX),
                generatorY, 0f);

            // 각 발사 시점의 생성기 중심 좌표에서 생성.
            // 프레임 속도가 낮아져도 발사 간격을 유지한다.
            while (shotIndex * ShotInterval <= nextTime &&
                   shotIndex * ShotInterval < totalSeconds)
            {
                float birth = shotIndex * ShotInterval;

                for (int emitter = 0; emitter < 2; emitter++)
                {
                    bool isRed = emitter == 0;

                    Vector2 origin = new Vector2(
                        GeneratorX(birth, isRed, leftX, rightX),
                        generatorY);

                    Transform visual = MakeSquare(
                        isRed ? "Red Bullet" : "Green Bullet",
                        square, scope.Root,
                        BulletSize,
                        isRed ? RedColor : GreenColor,
                        15);

                    visual.position = origin;
                    visual.gameObject.SetActive(false);

                    bullets.Add(new Bullet
                    {
                        visual = visual,
                        origin = origin,
                        birth = birth,
                        isRed = isRed
                    });
                }

                shotIndex++;
            }

            // 클릭을 누르고 있는 동안 true.
            bool holdingClick = Input.GetMouseButton(0);

            Vector2 player = c.Player.transform.position;

            int samples = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (BulletSpeed * dt +
                     Vector2.Distance(previousPlayer, player)) / 0.04f)
            );

            bool contacted = false;

            for (int sample = 1; sample <= samples; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;

                Vector2 playerAtTime = Vector2.Lerp(
                    previousPlayer, player, blend);

                for (int i = bullets.Count - 1; i >= 0; i--)
                {
                    Bullet bullet = bullets[i];
                    float age = time - bullet.birth;

                    if (age < 0f)
                        continue;

                    Vector2 position =
                        bullet.origin + Vector2.down * (BulletSpeed * age);

                    bullet.visual.gameObject.SetActive(true);
                    bullet.visual.position = position;

                    if (position.y + half.y < removeY)
                    {
                        bullet.visual.gameObject.SetActive(false);
                        Object.Destroy(bullet.visual.gameObject);
                        bullets.RemoveAt(i);
                        continue;
                    }

                    // 빨강: 클릭 중이면 위험.
                    // 초록: 클릭하지 않으면 위험.
                    bool dangerous = bullet.isRed
                        ? holdingClick
                        : !holdingClick;

                    if (dangerous && !contacted && Touches(
                        playerAtTime,
                        c.Player.HitRadius + 0.02f,
                        position,
                        half))
                    {
                        contacted = true;
                        c.Player.TakeDamage(Damage);

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

    private static float GeneratorX(
        float time, bool isRed,
        float leftX, float rightX)
    {
        float width = rightX - leftX;
        float distance = Mathf.PingPong(
            time * GeneratorSpeed, width);

        return isRed
            ? leftX + distance
            : rightX - distance;
    }

    private static bool Touches(
        Vector2 player, float radius,
        Vector2 center, Vector2 half)
    {
        Vector2 nearest = new Vector2(
            Mathf.Clamp(
                player.x, center.x - half.x, center.x + half.x),
            Mathf.Clamp(
                player.y, center.y - half.y, center.y + half.y)
        );

        return (player - nearest).sqrMagnitude <= radius * radius;
    }

    private static Transform MakeSquare(
        string name,
        Sprite sprite,
        Transform parent,
        Vector2 size,
        Color color,
        int sortingOrder)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);

        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return go.transform;
    }
}