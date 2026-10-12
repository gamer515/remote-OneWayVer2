using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class B2Pattern07 : IBattlePattern
{
    // 직접 조절하는 설정
    
    private const float TipHeight = 0.25f;
    private const float WarningSeconds = 0.5f;
    private const float FadeSeconds = 0.3f;

    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {

        // 상자 준비는 실행 순서 관리자가 먼저 처리한다.
        // 여기서는 이 패턴에 필요한 플레이어 상태를 적용한다.
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);

        // 패턴 종료 후 다음 준비가 끝날 때까지 이동을 잠근다.
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

        Rect box = c.Arena.Bounds;

        int attackCount = Mathf.Clamp(data.count, 1, 40);
        float speed = Mathf.Max(0.1f, data.speed);
        float gapSeconds = Mathf.Max(0f, data.interval);

        float width = Mathf.Clamp(
            data.swordSize.x,
            0.02f,
            box.width / 3f * 0.8f
        );

        float length = Mathf.Max(0.1f, data.swordSize.y);
        float tipHeight = Mathf.Min(TipHeight, length, box.height);

        var blades = new SpriteRenderer[3];
        var laneX = new float[3];

        for (int i = 0; i < 3; i++)
        {
            laneX[i] = Mathf.Lerp(
                box.xMin,
                box.xMax,
                (i + 0.5f) / 3f
            );

            var go = new GameObject(
                "Bottom Sword " + (i + 1),
                typeof(SpriteRenderer)
            );

            go.transform.SetParent(scope.Root, false);

            blades[i] = go.GetComponent<SpriteRenderer>();
            blades[i].sprite = square;
            blades[i].color = Color.white;
            blades[i].sortingOrder = 15;
            blades[i].shadowCastingMode = ShadowCastingMode.Off;
            blades[i].receiveShadows = false;

            // 처음에는 세 검을 모두 숨김.
            blades[i].enabled = false;
        }

        // 첫 공격 전에도 바닥이 비어 있는 시간을 둠.
        // B2.asset의 Interval 값 사용.
        float initialWait = 0f;

        while (initialWait < gapSeconds)
        {
            if (c.Owner.Result != BattleResult.Running)
                yield break;

            yield return null;
            initialWait += c.Clock.Delta;
        }

        float startTipY = box.yMin + tipHeight;
        float travelSeconds = (box.yMax - startTipY) / speed;
        float fadeStart = WarningSeconds + travelSeconds;
        float attackEnd = fadeStart + FadeSeconds;

        for (int attack = 0; attack < attackCount; attack++)
        {

            int lane = Random.Range(0, 3);
            SpriteRenderer blade = blades[lane];

            // 선택된 위치의 검 끝만 내밀어서 공격을 예고.
            ShowBlade(
                blade,
                laneX[lane],
                startTipY,
                width,
                length,
                box,
                Color.white
            );

            float elapsed = 0f;
            Vector2 previousPlayer = c.Player.transform.position;

            while (elapsed < attackEnd + gapSeconds)
            {
                if (c.Owner.Result != BattleResult.Running)
                    yield break;

                float dt = c.Clock.Delta;
                if (dt <= 0f)
                {
                    yield return null;
                    continue;
                }

                float nextTime = elapsed + dt;
                Vector2 player = c.Player.transform.position;

                
                int samples = Mathf.Max(1, Mathf.CeilToInt(
                    (speed * dt +
                     Vector2.Distance(previousPlayer, player)) / 0.04f
                ));

                bool contacted = false;

                for (int sample = 1; sample <= samples; sample++)
                {
                    float blend = (float)sample / samples;
                    float time = Mathf.Lerp(elapsed, nextTime, blend);

                    
                    if (time < WarningSeconds || time > fadeStart)
                        continue;

                    float tipY = Mathf.Min(
                        box.yMax,
                        startTipY + speed * (time - WarningSeconds)
                    );

                    Vector2 playerAtTime = Vector2.Lerp(
                        previousPlayer, player, blend);

                    if (!contacted && HitsBlade(
                        playerAtTime,
                        c.Player.HitRadius + 0.02f,
                        laneX[lane], tipY, width, length, box))
                    {
                        contacted = true;
                        c.Player.TakeDamage(1f);

                        if (c.Owner.Result != BattleResult.Running)
                            yield break;
                    }
                }

                if (nextTime < attackEnd)
                {
                    float tipY = Mathf.Min(
                        box.yMax,
                        startTipY +
                        speed * Mathf.Max(0f, nextTime - WarningSeconds)
                    );

                    float alpha = nextTime < fadeStart
                        ? 1f
                        : 1f - Mathf.Clamp01(
                            (nextTime - fadeStart) / FadeSeconds);

                    ShowBlade(
                        blade, laneX[lane], tipY,
                        width, length, box,
                        new Color(1f, 1f, 1f, alpha)
                    );
                }
                else
                {
                    blade.enabled = false;
                }

                elapsed = nextTime;
                previousPlayer = player;
                yield return null;
            }


            blade.enabled = false;
        }
    }

   
    private static void ShowBlade(
        SpriteRenderer blade,
        float x, float tipY,
        float width, float length,
        Rect box, Color color)
    {
        float bottom = Mathf.Max(box.yMin, tipY - length);
        float top = Mathf.Min(box.yMax, tipY);
        float height = Mathf.Max(0f, top - bottom);

        blade.enabled = height > 0f && color.a > 0f;
        blade.color = color;
        blade.transform.position =
            new Vector3(x, (bottom + top) * 0.5f, 0f);
        blade.transform.localScale =
            new Vector3(width, height, 1f);
    }

    private static bool HitsBlade(
        Vector2 player, float radius,
        float x, float tipY,
        float width, float length,
        Rect box)
    {
        float bottom = Mathf.Max(box.yMin, tipY - length);
        float top = Mathf.Min(box.yMax, tipY);

        if (top <= bottom)
            return false;

        Vector2 nearest = new Vector2(
            Mathf.Clamp(player.x, x - width * 0.5f, x + width * 0.5f),
            Mathf.Clamp(player.y, bottom, top)
        );

        return (player - nearest).sqrMagnitude <= radius * radius;
    }

    
}