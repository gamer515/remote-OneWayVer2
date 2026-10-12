using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BDPattern06 : IBattlePattern
{
    private sealed class FallingSquare
    {
        public SpriteRenderer renderer;
        public Vector2 position;
    }

    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);
        scope.OnDispose(() =>
        {
            c.Player.ClearGravityScroll();
            BattlePlayerPreparationRunner.EndSpecial(c);
        });

        Rect box = c.Arena.Bounds;
        Rect screen = c.Arena.ScreenRect;
        c.Player.EnableGravityScroll(box.center.x);

        Vector2 size = new Vector2(
            Mathf.Clamp(data.bdFallingSquareSize.x, 0.1f, box.width),
            Mathf.Max(0.1f, data.bdFallingSquareSize.y));
        float speed = Mathf.Max(0.1f, data.bdFallingSquareSpeed);
        float interval = Mathf.Max(0.05f, data.bdFallingSquareInterval);
        float gap = Mathf.Max(0f, data.bdFallingSquareGap);
        int randomCount = Mathf.Max(0, data.bdFallingSquareRandomCount);
        float spawnY = screen.yMax + size.y * 0.5f + 0.1f;

        Texture2D white = Texture2D.whiteTexture;
        Sprite sprite = Sprite.Create(white,
            new Rect(0f, 0f, white.width, white.height),
            new Vector2(0.5f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
        scope.OnDispose(() => Object.Destroy(sprite));

        // 화면 위에서 시간차를 두고 들어와 그림의 높낮이를 만든다.
        // 순서는 왼쪽 아래, 왼쪽 위, 오른쪽, 중앙, 중앙 오른쪽이다.
        float[] introX = { 0.10f, 0.23f, 0.88f, 0.48f, 0.65f };
        float[] introTimes = { 0f, 0.34f, 0.50f, 0.98f, 1.20f };
        var squares = new List<FallingSquare>();
        int introIndex = 0;
        int randomSpawned = 0;
        float nextSpawn = 0f;
        float elapsed = 0f;
        float previousScroll = c.Player.GravityScrollDistance;
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

            float scroll = c.Player.GravityScrollDistance;
            Vector2 movement = new Vector2(scroll - previousScroll, -speed * dt);
            previousScroll = scroll;
            Vector2 player = c.Player.transform.position;
            bool hit = false;

            for (int i = squares.Count - 1; i >= 0; i--)
            {
                FallingSquare square = squares[i];
                Vector2 before = square.position;
                square.position += movement;
                square.renderer.transform.position = square.position;

                // 플레이어와 낙하물의 상대 이동으로 빠른 접촉도 확인한다.
                if (!hit && data.bdFallingSquareDamage > 0f)
                    hit = SweptTouches(previousPlayer - before,
                        player - square.position, size, c.Player.HitRadius);

                // 가로로 화면을 벗어나도 다시 돌아올 수 있게 남겨 둔다.
                // 아래로 완전히 빠져나간 뒤에만 제거한다.
                if (square.position.y + size.y * 0.5f < screen.yMin)
                {
                    square.renderer.enabled = false;
                    Object.Destroy(square.renderer.gameObject);
                    squares.RemoveAt(i);
                }
            }

            if (hit)
            {
                c.Player.TakeDamage(data.bdFallingSquareDamage);
                if (c.Owner.Result != BattleResult.Running)
                    yield break;
            }

            if (elapsed >= nextSpawn)
            {
                if (introIndex < introX.Length)
                {
                    // 초기 다섯 개도 같은 이동 좌표계를 공유한다.
                    float x = Mathf.Lerp(box.xMin + size.x * 0.5f,
                        box.xMax - size.x * 0.5f, introX[introIndex]) + scroll;
                    Vector2 position = new Vector2(x, spawnY);
                    if (IsClear(squares, position, size, gap))
                    {
                        squares.Add(CreateSquare(scope.Root, sprite, position, size,
                            "초기 낙하물 " + (introIndex + 1)));
                        introIndex++;
                        nextSpawn = introIndex < introTimes.Length
                            ? Mathf.Max(elapsed, introTimes[introIndex]) : elapsed + interval;
                    }
                    else
                    {
                        nextSpawn = elapsed + 0.05f;
                    }
                }
                else if (randomSpawned < randomCount)
                {
                    if (TryRandomX(squares, box, spawnY, size, gap, out float x))
                    {
                        squares.Add(CreateSquare(scope.Root, sprite,
                            new Vector2(x, spawnY), size,
                            "무작위 낙하물 " + (randomSpawned + 1)));
                        randomSpawned++;
                        nextSpawn = elapsed + interval;
                    }
                    else
                    {
                        // 빈자리가 없으면 생성 개수를 줄이지 않고 기다린다.
                        nextSpawn = elapsed + 0.05f;
                    }
                }
            }

            if (introIndex == introX.Length && randomSpawned == randomCount && squares.Count == 0)
                yield break;

            elapsed += dt;
            previousPlayer = player;
            yield return null;
        }
    }

    private static FallingSquare CreateSquare(Transform parent, Sprite sprite,
        Vector2 position, Vector2 size, string name)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = 8;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return new FallingSquare { renderer = renderer, position = position };
    }

    private static bool IsClear(List<FallingSquare> squares, Vector2 position, Vector2 size, float gap)
    {
        foreach (var square in squares)
        {
            Vector2 difference = position - square.position;
            if (Mathf.Abs(difference.x) < size.x + gap &&
                Mathf.Abs(difference.y) < size.y + gap)
                return false;
        }
        return true;
    }

    private static bool TryRandomX(List<FallingSquare> squares, Rect box, float y,
        Vector2 size, float gap, out float x)
    {
        float left = box.xMin + size.x * 0.5f;
        float right = box.xMax - size.x * 0.5f;
        x = box.center.x;
        if (right - left <= 0.0001f)
            return IsClear(squares, new Vector2(x, y), size, gap);

        // 후보 범위에서 이미 점유된 구간을 빼고 남은 길이에 비례해 뽑는다.
        var ranges = new List<Vector2> { new Vector2(left, right) };
        foreach (var square in squares)
        {
            if (Mathf.Abs(y - square.position.y) >= size.y + gap)
                continue;
            float blockedLeft = square.position.x - size.x - gap;
            float blockedRight = square.position.x + size.x + gap;
            for (int i = ranges.Count - 1; i >= 0; i--)
            {
                Vector2 range = ranges[i];
                if (blockedRight <= range.x || blockedLeft >= range.y)
                    continue;
                ranges.RemoveAt(i);
                if (blockedLeft > range.x)
                    ranges.Add(new Vector2(range.x, blockedLeft));
                if (blockedRight < range.y)
                    ranges.Add(new Vector2(blockedRight, range.y));
            }
        }
        float total = 0f;
        foreach (Vector2 range in ranges)
            total += range.y - range.x;
        if (total <= 0.0001f)
            return false;
        float pick = Random.value * total;
        foreach (Vector2 range in ranges)
        {
            float width = range.y - range.x;
            if (pick <= width)
            {
                x = range.x + pick;
                return true;
            }
            pick -= width;
        }
        x = ranges[ranges.Count - 1].y;
        return true;
    }

    private static bool SweptTouches(Vector2 from, Vector2 to, Vector2 size, float radius)
    {
        const float sampleDistance = 0.04f;
        int samples = Mathf.Max(1,
            Mathf.CeilToInt(Vector2.Distance(from, to) / sampleDistance));
        float paddedRadius = radius + sampleDistance * 0.5f;
        Vector2 half = size * 0.5f;
        for (int i = 0; i <= samples; i++)
        {
            Vector2 point = Vector2.Lerp(from, to, i / (float)samples);
            Vector2 nearest = new Vector2(Mathf.Clamp(point.x, -half.x, half.x),
                Mathf.Clamp(point.y, -half.y, half.y));
            if ((point - nearest).sqrMagnitude <= paddedRadius * paddedRadius)
                return true;
        }
        return false;
    }
}