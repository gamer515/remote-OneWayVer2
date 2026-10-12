using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class BDPattern09 : IBattlePattern
{
    private sealed class FloorSlot
    {
        public SpriteRenderer visual;
        public float x;
        public float width;

        public readonly Queue<float> requests = new Queue<float>();

        public bool active;
        public float startedAt;
        public float availableAt;
    }

    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);

        // 기존 이동 속도와 점프 힘을 저장한다.
        float originalSpeed = c.Player.b2MouseMoveSpeed;
        float originalJumpForce = c.Player.jumpForce;

        // BD 09에서 사용할 값만 적용한다.
        c.Player.b2MouseMoveSpeed =
            Mathf.Max(0f, data.bdTrailPlayerSpeed);

        c.Player.jumpForce =
            Mathf.Max(0.1f, data.bdTrailJumpForce);

        // 정상 종료하거나 다른 패턴으로 이동하면 원래 값으로 복원한다.
        scope.OnDispose(() =>
        {
            c.Player.b2MouseMoveSpeed = originalSpeed;
            c.Player.jumpForce = originalJumpForce;
            BattlePlayerPreparationRunner.EndSpecial(c);
        });

        Rect box = c.Arena.Bounds;

        float duration = Mathf.Max(0.1f, data.bdTrailDuration);
        float delay = Mathf.Max(0f, data.bdTrailSpawnDelay);
        float hold = Mathf.Max(0f, data.bdTrailHoldSeconds);
        float grow = Mathf.Max(0.01f, data.bdTrailGrowSeconds);
        float retract = Mathf.Max(0.01f, data.bdTrailRetractSeconds);
        float damage = Mathf.Max(0f, data.bdTrailDamage);

        float width = Mathf.Clamp(
            data.bdTrailSquareSize.x, 0.05f, box.width);
        float height = Mathf.Clamp(
            data.bdTrailSquareSize.y, 0.05f, box.height);
        float gap = Mathf.Max(0f, data.bdTrailSquareGap);

        float cycle = grow + hold + retract;

        // 동일한 칸에 중복 생성되지 않도록 고정된 바닥 칸을 사용한다.
        int count = Mathf.Max(
            1, Mathf.FloorToInt((box.width + gap) / (width + gap)));

        float cellWidth = box.width / count;
        var slots = new FloorSlot[count];

        Texture2D white = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(
            white,
            new Rect(0f, 0f, white.width, white.height),
            new Vector2(0.5f, 0.5f),
            white.width,
            0,
            SpriteMeshType.FullRect);

        scope.OnDispose(() => Object.Destroy(square));

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject(
                "BD 09 바닥 Square " + (i + 1),
                typeof(SpriteRenderer));

            go.transform.SetParent(scope.Root, false);

            var visual = go.GetComponent<SpriteRenderer>();
            visual.sprite = square;
            visual.color = Color.white;
            visual.sortingOrder = 15;
            visual.enabled = false;

            slots[i] = new FloorSlot
            {
                visual = visual,
                x = box.xMin + (i + 0.5f) * cellWidth,
                width = width
            };
        }

        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;
        int previousCell = GetCell(
            previousPlayer.x, box.xMin, cellWidth, count);

        // 시작 위치도 첫 번째 생성 예약에 포함한다.
        slots[previousCell].requests.Enqueue(delay);

        while (elapsed < duration &&
               c.Owner.Result == BattleResult.Running)
        {
            float dt = Mathf.Min(c.Clock.Delta, duration - elapsed);

            if (dt <= 0f)
            {
                previousPlayer = c.Player.transform.position;
                yield return null;
                continue;
            }

            Vector2 currentPlayer = c.Player.transform.position;

            // 빠르게 이동해도 지나간 칸과 접촉 판정을 놓치지 않게 나눈다.
            float sampleDistance = Mathf.Max(
                0.01f,
                Mathf.Min(width, c.Player.HitRadius) * 0.5f);

            float travel = Vector2.Distance(
                previousPlayer, currentPlayer);

            float verticalTravel =
                height / Mathf.Min(grow, retract) * dt;

            int samples = Mathf.Max(
                1,
                Mathf.CeilToInt(dt / 0.02f),
                Mathf.CeilToInt(
                    (travel + verticalTravel) / sampleDistance));

            bool damagedThisFrame = false;

            for (int sample = 1; sample <= samples; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;

                if (time >= duration)
                    break;

                Vector2 player = Vector2.Lerp(
                    previousPlayer, currentPlayer, blend);

                int cell = GetCell(
                    player.x, box.xMin, cellWidth, count);

                if (cell != previousCell)
                {
                    // 지나간 모든 칸을 이동 방향 순서대로 예약한다.
                    int direction = cell > previousCell ? 1 : -1;

                    for (int index = previousCell + direction;
                         index != cell + direction;
                         index += direction)
                    {
                        slots[index].requests.Enqueue(time + delay);
                    }

                    previousCell = cell;
                }

                foreach (FloorSlot slot in slots)
                {
                    UpdateSlot(slot, time, cycle);

                    float visibleHeight = slot.active
                        ? GetHeight(
                            time - slot.startedAt,
                            height, grow, hold, retract)
                        : 0f;

                    Show(slot, box.yMin, visibleHeight);

                    if (damagedThisFrame ||
                        damage <= 0f ||
                        visibleHeight <= 0f)
                    {
                        continue;
                    }

                    Rect rect = new Rect(
                        slot.x - slot.width * 0.5f,
                        box.yMin,
                        slot.width,
                        visibleHeight);

                    if (!Touches(rect, player, c.Player.HitRadius))
                        continue;

                    c.Player.TakeDamage(damage);
                    damagedThisFrame = true;

                    if (c.Owner.Result != BattleResult.Running)
                        yield break;
                }
            }

            elapsed += dt;
            previousPlayer = currentPlayer;

            if (elapsed >= duration)
                break;

            yield return null;
        }

        // 패턴 실행 범위가 정리되면서 모든 Square가 제거된다.
    }

    private static int GetCell(
        float x, float left, float cellWidth, int count)
    {
        return Mathf.Clamp(
            Mathf.FloorToInt((x - left) / cellWidth),
            0,
            count - 1);
    }

    private static void UpdateSlot(
        FloorSlot slot, float time, float cycle)
    {
        while (true)
        {
            if (slot.active)
            {
                float finishedAt = slot.startedAt + cycle;

                if (time < finishedAt)
                    return;

                slot.active = false;
                slot.availableAt = finishedAt;
            }

            if (slot.requests.Count == 0)
                return;

            // 예약 시간이 지나도 기존 Square가 내려갈 때까지 기다린다.
            float start = Mathf.Max(
                slot.requests.Peek(),
                slot.availableAt);

            if (time < start)
                return;

            slot.requests.Dequeue();
            slot.startedAt = start;
            slot.active = true;
        }
    }

    private static float GetHeight(
        float age,
        float height,
        float grow,
        float hold,
        float retract)
    {
        if (age < grow)
            return height * Mathf.Clamp01(age / grow);

        if (age < grow + hold)
            return height;

        return height * (
            1f - Mathf.Clamp01((age - grow - hold) / retract));
    }

    private static void Show(
        FloorSlot slot, float floor, float height)
    {
        // 아래쪽 끝은 바닥에 고정하고 높이만 바꾼다.
        slot.visual.transform.position = new Vector3(
            slot.x, floor + height * 0.5f, 0f);

        slot.visual.transform.localScale = new Vector3(
            slot.width, height, 1f);

        slot.visual.enabled = height > 0f;
    }

    private static bool Touches(
        Rect rect, Vector2 player, float radius)
    {
        Vector2 nearest = new Vector2(
            Mathf.Clamp(player.x, rect.xMin, rect.xMax),
            Mathf.Clamp(player.y, rect.yMin, rect.yMax));

        return (player - nearest).sqrMagnitude <= radius * radius;
    }
}