using System;
using System.Collections;
using UnityEngine;

public sealed class B2Pattern01 : IBattlePattern
{
    private static readonly int[] ChargeHours =
        { 1, 4, 9, 2, 12, 6, 11, 7, 3, 8, 5, 10 };

    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        c.Arena.Mode = BattleArena.Boundary.Box;
        c.Player.SetVisible(true);
        c.Player.SetMovementMode(PlayerController.MovementMode.Free);
        c.Player.Lock(false);
        scope.OnDispose(() =>
        {
            c.Box.CancelAnimations();
            c.Player.Lock(true);
            c.Player.SetMovementMode(PlayerController.MovementMode.Free);
        });

        // Reuse the twelve entrance enemies; the first four now belong to the box.
        var enemies = new Transform[12];
        var starts = new Vector3[12];
        var radii = new float[12];
        float largestRadius = 0f;
        for (int i = 0; i < 12; i++)
        {
            enemies[i] = c.Enemies.Find("Enemy " + (i + 5));
            if (enemies[i] == null)
                throw new InvalidOperationException("B2 Pattern 01 requires Enemy " + (i + 5));
            starts[i] = enemies[i].position;
            Vector3 extents = enemies[i].GetComponent<Renderer>().bounds.extents;
            radii[i] = Mathf.Min(extents.x, extents.y);
            largestRadius = Mathf.Max(largestRadius, new Vector2(extents.x, extents.y).magnitude);
        }
        foreach (Transform enemy in enemies)
            enemy.SetParent(scope.Root, true);

        Vector2 center = c.Arena.Bounds.center;
        Vector2 halfSize = new Vector2(
            (c.Box.rightWall.position.x - c.Box.leftWall.position.x) * 0.5f,
            (c.Box.topWall.position.y - c.Box.bottomWall.position.y) * 0.5f);
        halfSize += Vector2.one * (c.Box.wallThickness * 0.5f);
        float radius = halfSize.magnitude + largestRadius + Mathf.Max(0f, data.orbitPadding);
        float formationTime = Mathf.Max(0.1f, data.formationSeconds);
        for (float t = 0f; t < formationTime; t += c.Clock.Delta)
        {
            float blend = Mathf.SmoothStep(0f, 1f, t / formationTime);
            for (int i = 0; i < 12; i++)
                enemies[i].position = Vector3.Lerp(starts[i], CirclePosition(center, radius, i, 0f), blend);
            yield return null;
        }

        // Clockwise, exactly three turns. Index zero is twelve o'clock.
        float orbitTime = Mathf.Max(0.1f, data.orbitSecondsPerTurn) * 1f;
        for (float t = 0f; t < orbitTime; t += c.Clock.Delta)
        {
            float rotation = 360f * t / orbitTime;
            for (int i = 0; i < 12; i++)
                enemies[i].position = CirclePosition(center, radius, i, rotation);
            yield return null;
        }
        for (int i = 0; i < 12; i++)
            enemies[i].position = CirclePosition(center, radius, i, 0f);
        yield return c.Clock.Wait(Mathf.Max(0f, data.orbitPauseSeconds));

        var origins = new Vector2[12];
        var velocities = new Vector2[12];
        var damageSpent = new bool[12];

        var startRotations = new Quaternion[12];
        var targetRotations = new Quaternion[12];

        // 머리를 돌리는 데 걸리는 시간.
        float turnDuration = 0.2f;

        float speed = Mathf.Max(0.1f, data.speed);
        for (int i = 0; i < 12; i++)
        {
            origins[i] = enemies[i].position;

            Vector2 direction = (center - origins[i]).normalized;
            velocities[i] = direction * speed;

            startRotations[i] = enemies[i].rotation;

            // 적의 위쪽(+Y)이 중심을 향하도록 회전.
            float angle =
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;

            targetRotations[i] = Quaternion.Euler(0f, 0f, angle);
        }

        // This distance ends beyond the screen even when the ring starts offscreen.
        Rect screen = c.Arena.ScreenRect;
        float farX = Mathf.Max(Mathf.Abs(screen.xMin - center.x), Mathf.Abs(screen.xMax - center.x));
        float farY = Mathf.Max(Mathf.Abs(screen.yMin - center.y), Mathf.Abs(screen.yMax - center.y));
        float exitDistance = radius + new Vector2(farX, farY).magnitude + largestRadius + 1f;
        float flightTime = exitDistance / speed;
        // 기존 출발 간격을 유지하되, 회전 시간보다 짧아지지 않도록 설정.
        float interval = Mathf.Max(turnDuration, data.interval);
        float elapsed = 0f;
        int remaining = 12;
        while (remaining > 0 && c.Owner.Result == BattleResult.Running)
        {
            float dt = c.Clock.Delta;
            if (dt <= 0f) { yield return null; continue; }
            elapsed += dt;
            for (int order = 0; order < ChargeHours.Length; order++)
            {
                int index = ChargeHours[order] % 12;
                Transform enemy = enemies[index];
                float turnAge = elapsed - order * interval;

                if (enemy == null || turnAge < 0f)
                    continue;

                // 자기 차례에는 먼저 제자리에서 머리를 돌림.
                if (turnAge < turnDuration)
                {
                    float t = Mathf.SmoothStep(
                        0f, 1f, turnAge / turnDuration);

                    enemy.rotation = Quaternion.Slerp(
                        startRotations[index],
                        targetRotations[index],
                        t);

                    continue;
                }

                // 회전을 끝낸 방향으로 고정하고 돌진.
                enemy.rotation = targetRotations[index];

                // 이동 시간은 회전이 끝난 시점부터 계산.
                float age = turnAge - turnDuration;

                Vector2 before = enemy.position;
                Vector2 after = origins[index] + velocities[index] * Mathf.Min(age, flightTime);
                enemy.position = new Vector3(after.x, after.y, 0f);
                if (!damageSpent[index] && SlashResolver.DistanceToSegment(
                    c.Player.transform.position, before, after) <= c.Player.HitRadius + radii[index])
                {
                    // Each charge can hit once; the enemy still passes through the center.
                    damageSpent[index] = true;
                    c.Player.TakeDamage(1f);
                    if (c.Owner.Result != BattleResult.Running) yield break;
                }
                if (age >= flightTime)
                {
                    enemy.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(enemy.gameObject);
                    enemies[index] = null;
                    remaining--;
                }
            }
            yield return null;
        }
    }

    private static Vector3 CirclePosition(Vector2 center, float radius, int index, float rotation)
    {
        float angle = (index * 30f + rotation) * Mathf.Deg2Rad;
        return new Vector3(center.x + Mathf.Sin(angle) * radius,
            center.y + Mathf.Cos(angle) * radius, 0f);
    }
}