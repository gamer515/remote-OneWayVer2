using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BDPattern01 : IBattlePattern
{
    private const int ClawCount = 3;

    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);
        scope.OnDispose(() => c.Player.Lock(true));

        float width = Mathf.Max(0.02f, data.bdClawWidth);
        float spacing = Mathf.Max(0f, data.bdClawSpacing);
        float length = Mathf.Max(0.1f, data.bdClawLength);
        float growSeconds = Mathf.Max(0.01f, data.bdClawGrowSeconds);
        float holdSeconds = Mathf.Max(0f, data.bdClawHoldSeconds);
        float fadeSeconds = Mathf.Max(0f, data.bdClawFadeSeconds);
        float damage = Mathf.Max(0f, data.bdClawDamage);
        float damageEnd = growSeconds + holdSeconds;
        float totalSeconds = damageEnd + fadeSeconds;

        Vector2 center = c.Arena.Bounds.center;
        Vector2 direction = new Vector2(1f, -1f).normalized;
        Vector2 normal = new Vector2(-direction.y, direction.x);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        Texture2D white = Texture2D.whiteTexture;
        // 왼쪽 끝을 피벗으로 사용하여 시작점을 고정하고 길이만 늘린다.
        Sprite square = Sprite.Create(white,
            new Rect(0f, 0f, white.width, white.height),
            new Vector2(0f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
        scope.OnDispose(() => Object.Destroy(square));

        var claws = new SpriteRenderer[ClawCount];
        var starts = new Vector2[ClawCount];
        for (int i = 0; i < ClawCount; i++)
        {
            starts[i] = center - direction * (length * 0.5f)
                + normal * ((i - 1) * spacing);
            var go = new GameObject("BD 할퀴기 " + (i + 1), typeof(SpriteRenderer));
            go.transform.SetParent(scope.Root, false);
            go.transform.SetPositionAndRotation(
                new Vector3(starts[i].x, starts[i].y, 0f),
                Quaternion.Euler(0f, 0f, angle));
            go.transform.localScale = new Vector3(0f, width, 1f);

            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = square;
            renderer.color = Color.white;
            renderer.sortingOrder = 15;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            claws[i] = renderer;
        }

        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;
        while (elapsed < totalSeconds && c.Owner.Result == BattleResult.Running)
        {
            float dt = Mathf.Min(c.Clock.Delta, totalSeconds - elapsed);
            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            float nextTime = elapsed + dt;
            Vector2 player = c.Player.transform.position;
            // 낮은 프레임에서도 늘어나는 끝점이나 플레이어가 판정을 건너뛰지 않게 나눈다.
            float travel = length / growSeconds * dt
                + Vector2.Distance(previousPlayer, player);
            float sampleDistance = Mathf.Min(0.04f, width * 0.5f);
            int samples = Mathf.Max(1, Mathf.CeilToInt(travel / sampleDistance));
            bool contacted = false;
            for (int sample = 1; sample <= samples && !contacted; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;
                // 소멸 구간에는 선이 보여도 피해를 주지 않는다.
                if (time >= damageEnd || damage <= 0f)
                    break;

                float visibleLength = length * Mathf.Clamp01(time / growSeconds);
                Vector2 playerAtTime = Vector2.Lerp(previousPlayer, player, blend);
                for (int i = 0; i < ClawCount; i++)
                {
                    if (!Touches(playerAtTime, c.Player.HitRadius + sampleDistance * 0.5f,
                        starts[i], direction, normal, visibleLength, width))
                        continue;
                    contacted = true;
                    c.Player.TakeDamage(damage);
                    if (c.Owner.Result != BattleResult.Running)
                        yield break;
                    break;
                }
            }

            float currentLength = length * Mathf.Clamp01(nextTime / growSeconds);
            float alpha = nextTime < damageEnd ? 1f
                : fadeSeconds <= 0f ? 0f
                : 1f - Mathf.Clamp01((nextTime - damageEnd) / fadeSeconds);
            foreach (var claw in claws)
            {
                claw.transform.localScale = new Vector3(currentLength, width, 1f);
                claw.color = new Color(1f, 1f, 1f, alpha);
                claw.enabled = currentLength > 0f && alpha > 0f;
            }
            elapsed = nextTime;
            previousPlayer = player;
            yield return null;
        }
    }

    private static bool Touches(Vector2 player, float radius,
        Vector2 start, Vector2 direction, Vector2 normal, float length, float width)
    {
        if (length <= 0f)
            return false;
        // 화면에 표시된 길이와 두께에 맞춘 회전 직사각형으로 검사한다.
        Vector2 delta = player - start;
        Vector2 local = new Vector2(Vector2.Dot(delta, direction), Vector2.Dot(delta, normal));
        Vector2 nearest = new Vector2(
            Mathf.Clamp(local.x, 0f, length),
            Mathf.Clamp(local.y, -width * 0.5f, width * 0.5f));
        return (local - nearest).sqrMagnitude <= radius * radius;
    }
}
