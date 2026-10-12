using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BDPattern04 : IBattlePattern
{
    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);

        // 종료하거나 다른 패턴으로 이동할 때 특수 이동 상태를 정리한다.
        scope.OnDispose(
            () => BattlePlayerPreparationRunner.EndSpecial(c));

        Rect box = c.Arena.Bounds;

        float spikeSpeed = Mathf.Max(0.1f, data.bdSpikeSpeed);
        float spikeLength = Mathf.Max(0.1f, data.bdSpikeLength);
        float spikeHeight = Mathf.Clamp(
            data.bdSpikeHeight, 0.05f, box.height);

        float spikeSpacing = Mathf.Max(0.03f, data.bdSpikeSpacing);
        float spikeWidth = Mathf.Clamp(
            data.bdSpikeWidth, 0.02f, spikeSpacing);

        // 발판이 가시보다 빠르게 이동하도록 최소 속도를 보정한다.
        float platformSpeed = Mathf.Max(
            spikeSpeed + 0.1f, data.bdPlatformSpeed);

        float platformWidth = Mathf.Max(0.1f, data.bdPlatformSize.x);
        float platformHeight = Mathf.Max(0.05f, data.bdPlatformSize.y);

        float normalSpacing = Mathf.Max(
            platformWidth + 0.1f, data.bdPlatformSpacing);

        float lastSpacing = Mathf.Max(
            normalSpacing + 0.1f, data.bdPlatformLastSpacing);

        float firstSpawn = Mathf.Max(0.01f, data.bdPlatformDelay);

        
        float[] spawnTimes =
        {
            firstSpawn,
            firstSpawn + normalSpacing / platformSpeed,
            firstSpawn + normalSpacing * 2f / platformSpeed,
            firstSpawn + (normalSpacing * 2f + lastSpacing)
                / platformSpeed
        };

        // 하단 → 중단 → 상단 → 하단.
        float low = Mathf.Max(platformHeight, data.bdPlatformLowHeight);
        float heightStep = Mathf.Max(0.1f, data.bdPlatformHeightStep);

        float[] heights =
        {
            low,
            low + heightStep,
            low + heightStep * 2f,
            low
        };

        // 상단 발판 위에도 플레이어가 설 공간이 있어야 한다.
        float requiredHeight =
            heights[2] + c.Player.HalfSize.y * 2f + 0.1f;

        if (requiredHeight > box.height)
        {
            Debug.LogError(
                "상단 발판이 너무 높습니다. 발판 높이나 높이 간격을 줄여 주세요.");
            yield break;
        }

        Texture2D white = Texture2D.whiteTexture;

        Sprite square = Sprite.Create(
            white,
            new Rect(0f, 0f, white.width, white.height),
            new Vector2(0.5f, 0.5f),
            white.width,
            0,
            SpriteMeshType.FullRect);

        scope.OnDispose(() => Object.Destroy(square));

        int spikeCount = Mathf.Max(
            1, Mathf.CeilToInt(spikeLength / spikeSpacing));

        var spikes = new SpriteRenderer[spikeCount];

        for (int i = 0; i < spikeCount; i++)
        {
            spikes[i] = CreateVisual(
                scope.Root, square, "이동 가시 " + (i + 1));
        }

        var platforms = new BDMovingPlatform[4];

        float elapsed = 0f;
        float spikeEndTime = (box.width + spikeLength) / spikeSpeed;

        Vector2 previousPlayer = c.Player.transform.position;

        while (c.Owner.Result == BattleResult.Running)
        {
            float dt = c.Clock.Delta;

            if (dt <= 0f)
            {
                previousPlayer = c.Player.transform.position;
                yield return null;
                continue;
            }

            float nextTime = elapsed + dt;

            for (int i = 0; i < platforms.Length; i++)
            {
                if (platforms[i] != null ||
                    nextTime < spawnTimes[i])
                {
                    continue;
                }

                // 프레임이 늦어져도 원래 등장 간격에 맞는 위치에서 시작한다.
                float overdue = nextTime - spawnTimes[i];

                Vector2 position = new Vector2(
                    box.xMin - platformWidth * 0.5f
                        + overdue * platformSpeed,
                    box.yMin + heights[i] - platformHeight * 0.5f);

                platforms[i] = CreatePlatform(
                    c,
                    scope.Root,
                    square,
                    position,
                    new Vector2(platformWidth, platformHeight),
                    platformSpeed,
                    box.xMax,
                    i + 1);
            }

            Vector2 player = c.Player.transform.position;

            // 가시와 플레이어가 빠르게 움직여도 접촉을 놓치지 않도록 나눈다.
            float travel = spikeSpeed * dt
                + Vector2.Distance(previousPlayer, player);

            int samples = Mathf.Max(
                1, Mathf.CeilToInt(travel / 0.05f));

            bool hit = false;

            for (int sample = 0; sample <= samples && !hit; sample++)
            {
                float t = sample / (float)samples;
                float time = Mathf.Lerp(elapsed, nextTime, t);
                Vector2 sampledPlayer = Vector2.Lerp(
                    previousPlayer, player, t);

                for (int i = 0; i < spikeCount; i++)
                {
                    Rect rect = SpikeRect(
                        box, time, i, spikeSpeed,
                        spikeLength, spikeSpacing,
                        spikeWidth, spikeHeight);

                    if (Touches(rect, sampledPlayer, c.Player.HitRadius))
                    {
                        hit = true;
                        break;
                    }
                }
            }

            for (int i = 0; i < spikeCount; i++)
            {
                Rect rect = SpikeRect(
                    box, nextTime, i, spikeSpeed,
                    spikeLength, spikeSpacing,
                    spikeWidth, spikeHeight);

                ShowRect(spikes[i], rect);
            }

            if (hit)
            {
                c.Player.TakeDamage(Mathf.Max(0f, data.bdSpikeDamage));

                if (c.Owner.Result != BattleResult.Running)
                    yield break;
            }

            elapsed = nextTime;
            previousPlayer = player;

            bool platformsFinished = true;

            foreach (var platform in platforms)
            {
                if (platform == null || !platform.Finished)
                {
                    platformsFinished = false;
                    break;
                }
            }

            if (elapsed >= spikeEndTime && platformsFinished)
                break;

            yield return null;
        }
    }

    private static BDMovingPlatform CreatePlatform(
        BattleContext c,
        Transform parent,
        Sprite sprite,
        Vector2 position,
        Vector2 size,
        float speed,
        float rightEdge,
        int number)
    {
        SpriteRenderer visual = CreateVisual(
            parent, sprite, "이동 발판 " + number);

        GameObject go = visual.gameObject;
        go.transform.position = position;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        visual.enabled = true;

        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        var collider = go.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;
        collider.usedByEffector = true;

        // 아래에서는 통과하고, 위에서 내려올 때만 착지한다.
        var effector = go.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.useOneWayGrouping = true;
        effector.surfaceArc = 160f;
        effector.useSideFriction = false;
        effector.useSideBounce = false;

        var moving = go.AddComponent<BDMovingPlatform>();
        moving.Initialize(c, speed, size.x, rightEdge);

        return moving;
    }

    private static SpriteRenderer CreateVisual(
        Transform parent, Sprite sprite, string name)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);

        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = 8;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;

        return renderer;
    }

    private static Rect SpikeRect(
        Rect box,
        float time,
        int index,
        float speed,
        float length,
        float spacing,
        float width,
        float height)
    {
        float front = box.xMin + time * speed;
        float offset = index * spacing;

        float toothWidth = Mathf.Min(
            width, Mathf.Max(0f, length - offset));

        float right = front - offset;
        float left = right - toothWidth;

        // 가시는 BattleBox 안에서만 보이고 피해를 준다.
        float clippedLeft = Mathf.Max(box.xMin, left);
        float clippedRight = Mathf.Min(box.xMax, right);

        return new Rect(
            clippedLeft,
            box.yMin,
            Mathf.Max(0f, clippedRight - clippedLeft),
            height);
    }

    private static void ShowRect(SpriteRenderer renderer, Rect rect)
    {
        renderer.enabled = rect.width > 0f && rect.height > 0f;

        if (!renderer.enabled)
            return;

        renderer.transform.position =
            new Vector3(rect.center.x, rect.center.y, 0f);

        renderer.transform.localScale =
            new Vector3(rect.width, rect.height, 1f);
    }

    private static bool Touches(
        Rect rect, Vector2 player, float radius)
    {
        if (rect.width <= 0f || rect.height <= 0f)
            return false;

        Vector2 closest = new Vector2(
            Mathf.Clamp(player.x, rect.xMin, rect.xMax),
            Mathf.Clamp(player.y, rect.yMin, rect.yMax));

        return (player - closest).sqrMagnitude <= radius * radius;
    }
}