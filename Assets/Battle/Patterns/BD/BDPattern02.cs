using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BDPattern02 : IBattlePattern
{
    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);
        scope.OnDispose(() => c.Player.Lock(true));

        Rect box = c.Arena.Bounds;

        int count = Mathf.Max(4, data.sineColumnCount);
        float length = Mathf.Max(1f, data.sinePathLength);
        float spacing = length / (count - 1);

        // 막대 사이의 가로 간격이 사라지지 않도록 두께를 제한한다.
        float width = Mathf.Clamp(
            data.sineBarWidth, 0.02f, spacing);

        float speed = Mathf.Max(0.1f, data.sineMoveSpeed);
        float damage = Mathf.Max(0f, data.sineDamage);

        // 플레이어 몸이 통과할 수 있도록 최소 틈을 확보한다.
        float minimumGap = c.Player.HalfSize.y * 2f + 0.3f;
        float maximumGap = box.height - 0.2f;

        if (maximumGap < minimumGap)
        {
            Debug.LogError("사인 곡선 통로를 만들기에는 BattleBox가 너무 작습니다.");
            yield break;
        }

        float gap = Mathf.Clamp(
            data.sineGapHeight, minimumGap, maximumGap);

        // 곡선의 가장 높은 곳과 낮은 곳에서도 막대가 상자 안에 남는다.
        float maximumAmplitude =
            Mathf.Max(0f, (box.height - gap) * 0.5f - 0.1f);

        float amplitude = Mathf.Clamp(
            data.sineAmplitude, 0f, maximumAmplitude);

        Texture2D white = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(
            white,
            new Rect(0f, 0f, white.width, white.height),
            new Vector2(0.5f, 0.5f),
            white.width,
            0,
            SpriteMeshType.FullRect);

        scope.OnDispose(() => Object.Destroy(square));

        var upper = new SpriteRenderer[count];
        var lower = new SpriteRenderer[count];
        var gapCenters = new float[count];

        for (int i = 0; i < count; i++)
        {
            upper[i] = CreateBar(scope.Root, square, "위 막대 " + i);
            lower[i] = CreateBar(scope.Root, square, "아래 막대 " + i);

            // 배열은 왼쪽부터 오른쪽 순서이다.
            // 공간상의 0~3π 구간에 Y축 대칭 사인 곡선을 배치한다.
            float phase = i / (float)(count - 1) * Mathf.PI * 3f;
            gapCenters[i] = box.center.y - Mathf.Sin(phase) * amplitude;
        }

        // 가장 오른쪽 막대부터 상자의 왼쪽 벽으로 들어온다.
        float firstX = box.xMin - width * 0.5f - length;
        float travel = 0f;
        float totalTravel = box.width + length + width;

        Vector2 previousPlayer = c.Player.transform.position;

        while (travel < totalTravel &&
               c.Owner.Result == BattleResult.Running)
        {
            float dt = c.Clock.Delta;

            if (dt <= 0f)
            {
                previousPlayer = c.Player.transform.position;
                yield return null;
                continue;
            }

            float nextTravel = Mathf.Min(totalTravel, travel + speed * dt);
            Vector2 currentPlayer = c.Player.transform.position;

            // 빠른 이동 중 막대를 통과하는 경우도 확인한다.
            float checkDistance = nextTravel - travel
                + Vector2.Distance(previousPlayer, currentPlayer);

            int samples = Mathf.Max(
                1, Mathf.CeilToInt(checkDistance / 0.05f));

            bool hit = false;

            for (int sample = 0; sample <= samples && !hit; sample++)
            {
                float t = sample / (float)samples;
                float sampledTravel = Mathf.Lerp(travel, nextTravel, t);
                Vector2 player = Vector2.Lerp(
                    previousPlayer, currentPlayer, t);

                for (int i = 0; i < count; i++)
                {
                    float x = firstX + i * spacing + sampledTravel;

                    GetRects(
                        box, x, width, gapCenters[i], gap,
                        out Rect top, out Rect bottom);

                    if (Touches(top, player, c.Player.HitRadius) ||
                        Touches(bottom, player, c.Player.HitRadius))
                    {
                        hit = true;
                        break;
                    }
                }
            }

            travel = nextTravel;

            for (int i = 0; i < count; i++)
            {
                float x = firstX + i * spacing + travel;

                GetRects(
                    box, x, width, gapCenters[i], gap,
                    out Rect top, out Rect bottom);

                ShowRect(upper[i], top);
                ShowRect(lower[i], bottom);
            }

            if (hit && damage > 0f)
                c.Player.TakeDamage(damage);

            previousPlayer = currentPlayer;
            yield return null;
        }
    }

    private static SpriteRenderer CreateBar(
        Transform parent, Sprite sprite, string name)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);

        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = 15;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;

        return renderer;
    }

    private static void GetRects(
        Rect box, float x, float width, float gapCenter, float gap,
        out Rect top, out Rect bottom)
    {
        // 상자 바깥 부분을 잘라 표시와 데미지 범위를 일치시킨다.
        float left = Mathf.Max(box.xMin, x - width * 0.5f);
        float right = Mathf.Min(box.xMax, x + width * 0.5f);
        float visibleWidth = Mathf.Max(0f, right - left);

        float gapTop = gapCenter + gap * 0.5f;
        float gapBottom = gapCenter - gap * 0.5f;

        top = new Rect(
            left, gapTop, visibleWidth,
            Mathf.Max(0f, box.yMax - gapTop));

        bottom = new Rect(
            left, box.yMin, visibleWidth,
            Mathf.Max(0f, gapBottom - box.yMin));
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

    private static bool Touches(Rect rect, Vector2 player, float radius)
    {
        if (rect.width <= 0f || rect.height <= 0f)
            return false;

        Vector2 closest = new Vector2(
            Mathf.Clamp(player.x, rect.xMin, rect.xMax),
            Mathf.Clamp(player.y, rect.yMin, rect.yMax));

        return (player - closest).sqrMagnitude <= radius * radius;
    }
}