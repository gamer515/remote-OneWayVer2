using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class B2Pattern06 : IBattlePattern
{
    private sealed class Sword
    {
        public Transform visual;
        public Vector2 origin;
        public float birth;
    }

    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {
        // 패턴에 지정한 플레이어 시작 상태를 적용한다.
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);
        c.Player.Lock(false);
        scope.OnDispose(() => c.Player.Lock(true));

        // 에셋 없이 흰색 Square 스프라이트 생성
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
        var hammer = new B2HammerMechanic(c, square, scope.Root);

        Rect box = c.Arena.Bounds;
        Rect screen = c.Arena.ScreenRect;

        const int laneCount = 6;

        int waveCount = Mathf.Clamp(data.count, 1, 40);
        float interval = Mathf.Max(0.05f, data.interval);
        float speed = Mathf.Max(0.1f, data.speed);

        // 가로 검이므로 X는 길이, Y는 두께
        Vector2 size = new Vector2(
            Mathf.Max(0.1f, data.swordSize.x),
            Mathf.Max(0.02f, data.swordSize.y)
        );
        Vector2 half = size * 0.5f;

        // 화면 오른쪽 바깥에서 출발
        float startX = Mathf.Max(screen.xMax, box.xMax) + half.x;

        // 검 전체가 화면 왼쪽을 벗어나면 제거
        float endX = Mathf.Min(screen.xMin, box.xMin) - half.x;

        // 아래에서 위로 0~5번 칸.
        // 연속된 두 칸의 빈 공간이 위아래로 이동한다.
        int[] holeSequence = { 1, 2, 3, 2 };

        var swords = new List<Sword>();
        int spawnedWaves = 0;
        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;

        while((spawnedWaves < waveCount ||
            swords.Count > 0 ||
        !   hammer.Finished)
        && c.Owner.Result == BattleResult.Running)
        {
            float dt = c.Clock.Delta;
            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            hammer.Tick(dt);

            // 오른쪽 벽이 왼쪽 고정 검에 닿으면 다음 단계로 진행.
            if (hammer.ReachedLeftSwords)
            {
                c.Player.Lock(true);

                float waitTime = 0f;

                while (waitTime < 0.8f &&
                       c.Owner.Result == BattleResult.Running)
                {
                    yield return null;
                    waitTime += c.Clock.Delta;
                }

                yield break;
            }

            float nextTime = elapsed + dt;

            // 한 번에 세로 6칸 중 4칸에 검 생성
            while (spawnedWaves < waveCount
                && spawnedWaves * interval <= nextTime)
            {
                float birth = spawnedWaves * interval;
                int hole = holeSequence[
                    spawnedWaves % holeSequence.Length
                ];

                for (int lane = 0; lane < laneCount; lane++)
                {
                    if (lane == hole || lane == hole + 1)
                        continue;

                    float y = Mathf.Lerp(
                        box.yMin,
                        box.yMax,
                        (lane + 0.5f) / laneCount
                    );
                    Vector2 origin = new Vector2(startX, y);

                    var go = new GameObject(
                        "B2 Horizontal Sword",
                        typeof(SpriteRenderer)
                    );
                    go.transform.SetParent(scope.Root, false);
                    go.transform.localScale =
                        new Vector3(size.x, size.y, 1f);
                    go.transform.position = origin;

                    var renderer = go.GetComponent<SpriteRenderer>();
                    renderer.sprite = square;
                    renderer.color = Color.white;
                    renderer.sortingOrder = 15;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;

                    go.SetActive(false);

                    swords.Add(new Sword
                    {
                        visual = go.transform,
                        origin = origin,
                        birth = birth
                    });
                }

                spawnedWaves++;
            }

            Vector2 player = c.Player.transform.position;

            // 빠른 이동 중에도 충돌을 놓치지 않도록
            // 프레임 사이를 작은 간격으로 나눠 검사
            int samples = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (speed * dt
                    + Vector2.Distance(previousPlayer, player)) / 0.04f
                )
            );

            bool contacted = false;

            for (int sample = 1; sample <= samples; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;
                Vector2 playerAtTime =
                    Vector2.Lerp(previousPlayer, player, blend);

                if (!contacted && hammer.HitsLeftSwords(
                    playerAtTime,
                    c.Player.HitRadius + 0.02f))
                {
                    contacted = true;
                    c.Player.TakeDamage(1f);

                    if (c.Owner.Result != BattleResult.Running)
                        yield break;
                }

                for (int i = swords.Count - 1; i >= 0; i--)
                {
                    Sword sword = swords[i];
                    float age = time - sword.birth;

                    if (age < 0f)
                        continue;

                    Vector2 position =
                        sword.origin + Vector2.left * (speed * age);

                    sword.visual.gameObject.SetActive(true);
                    sword.visual.position = position;

                    if (position.x < endX)
                    {
                        sword.visual.gameObject.SetActive(false);
                        Object.Destroy(sword.visual.gameObject);
                        swords.RemoveAt(i);
                        continue;
                    }

                    if (!contacted && Touches(
                        playerAtTime,
                        c.Player.HitRadius + 0.02f,
                        position,
                        half))
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

    private static bool Touches(
        Vector2 player,
        float radius,
        Vector2 center,
        Vector2 half)
    {
        Vector2 nearest = new Vector2(
            Mathf.Clamp(player.x, center.x - half.x, center.x + half.x),
            Mathf.Clamp(player.y, center.y - half.y, center.y + half.y)
        );

        return (player - nearest).sqrMagnitude <= radius * radius;
    }
}