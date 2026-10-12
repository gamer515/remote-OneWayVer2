using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BDPattern07 : IBattlePattern
{
    private const int ClawCount = 6;

    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);

        // 기존 이동 속도를 저장하고 좌우 이동을 막는다.
        float originalMoveSpeed = c.Player.b2MouseMoveSpeed;
        c.Player.b2MouseMoveSpeed = 0f;

        // 남아 있는 좌우 속도만 제거하고 수직 속도는 유지한다.
        Vector2 velocity = c.Player.Body.linearVelocity;
        c.Player.Body.linearVelocity = new Vector2(0f, velocity.y);

        // 패턴이 끝나거나 중단되면 기존 이동 속도로 복원한다.
        scope.OnDispose(() =>
        {
            c.Player.b2MouseMoveSpeed = originalMoveSpeed;
            BattlePlayerPreparationRunner.EndSpecial(c);
        });

        float width = Mathf.Max(0.02f, data.bdMirrorClawWidth);
        float spacing = Mathf.Max(0f, data.bdMirrorClawSpacing);
        float length = Mathf.Max(0.1f, data.bdMirrorClawLength);
        float growSeconds = Mathf.Max(0.01f, data.bdMirrorClawGrowSeconds);
        float holdSeconds = Mathf.Max(0f, data.bdMirrorClawHoldSeconds);
        float centerGap = Mathf.Max(0f, data.bdMirrorClawCenterGap);
        float damage = Mathf.Max(0f, data.bdMirrorClawDamage);
        float damageEnd = growSeconds + holdSeconds;
        float totalSeconds = damageEnd;

        Rect box = c.Arena.Bounds;
        float radians = Mathf.Clamp(data.bdMirrorClawAngle, 10f, 80f) * Mathf.Deg2Rad;
        var directions = new Vector2[ClawCount];
        var normals = new Vector2[ClawCount];

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
            // 양쪽 바깥 위에서 중앙 바닥을 향해 평행한 선 세 개씩 늘린다.
            float side = i < 3 ? -1f : 1f;
            Vector2 direction = new Vector2(-side * Mathf.Cos(radians), -Mathf.Sin(radians));
            directions[i] = direction;
            normals[i] = new Vector2(-direction.y, direction.x);
            // 할퀴기에 직각인 바깥쪽 아래 방향으로 줄을 나란히 배치한다.
            Vector2 spreadDirection = new Vector2(
                side * Mathf.Sin(radians),
                -Mathf.Cos(radians));

            // 가장 아래쪽 줄의 끝이 기존 바닥 높이를 유지하도록 보정한다.
            Vector2 innerEnd = new Vector2(
                box.center.x + side * centerGap * 0.5f,
                box.yMin
                    + Mathf.Max(0f, data.bdMirrorClawBottomOffset)
                    + 2f * spacing * Mathf.Cos(radians));

            Vector2 end = innerEnd
                + spreadDirection * ((i % 3) * spacing);

            starts[i] = end - direction * length;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            var go = new GameObject("BD 양쪽 할퀴기 " + (i + 1), typeof(SpriteRenderer));
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
            float travel = length * (Mathf.Clamp01(nextTime / growSeconds) - Mathf.Clamp01(elapsed / growSeconds))
                + Vector2.Distance(previousPlayer, player);
            float sampleDistance = Mathf.Min(0.04f, width * 0.5f);
            int samples = Mathf.Max(1, Mathf.CeilToInt(travel / sampleDistance));
            bool contacted = false;
            for (int sample = 1; sample <= samples && !contacted; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;
                // 고정 종료 시간이 되면 피해 판정도 끝낸다.
                if (time >= damageEnd || damage <= 0f)
                    break;

                float visibleLength = length * Mathf.Clamp01(time / growSeconds);
                Vector2 playerAtTime = Vector2.Lerp(previousPlayer, player, blend);
                for (int i = 0; i < ClawCount; i++)
                {
                    if (!Touches(playerAtTime, c.Player.HitRadius + sampleDistance * 0.5f,
                        starts[i], directions[i], normals[i], visibleLength, width))
                        continue;
                    contacted = true;
                    c.Player.TakeDamage(damage);
                    if (c.Owner.Result != BattleResult.Running)
                        yield break;
                    break;
                }
            }

            float currentLength = length * Mathf.Clamp01(nextTime / growSeconds);
            // 점프 여부와 관계없이 생성 시간과 유지 시간이 끝날 때까지 남는다.
            foreach (var claw in claws)
            {
                claw.transform.localScale = new Vector3(currentLength, width, 1f);
                claw.color = Color.white;
                claw.enabled = currentLength > 0f;
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
