using System.Collections;
using UnityEngine;

public static class BattleBoxTransitionRunner
{
    public static IEnumerator Run(
        BattleContext c,
        BattleBoxPreparation settings)
    {
        if (settings == null ||
            settings.transition == BattleBoxTransition.Unchanged)
        {
            yield break;
        }

        var box = c.Box;

        box.CancelAnimations();
        c.Player.Lock(true);

        // 전환 중에는 공격을 실행하지 않고 플레이어 입력도 잠금.
        box.HideUI();
        c.HideMessage();

        var start = box.Capture();
        Quaternion startRotation = box.transform.rotation;

        // 상자 안에서의 플레이어 상대 위치를 유지.
        Vector3 playerLocal =
            box.transform.InverseTransformPoint(
                c.Player.transform.position);

        Vector2 size = new Vector2(
            Mathf.Max(
                settings.size.x,
                c.Player.HalfSize.x * 2f + box.wallThickness + 0.1f),
            Mathf.Max(
                settings.size.y,
                c.Player.HalfSize.y * 2f + box.wallThickness + 0.1f));

        Vector2 center = settings.useScreenCenter
            ? c.Arena.ScreenRect.center
            : settings.keepCurrentCenter ? (Vector2)start.center : settings.center;

        if (settings.fitRotationToScreen)
        {
            Rect screen = c.Arena.ScreenRect;
            float available = 2f * Mathf.Min(
                Mathf.Min(center.x - screen.xMin, screen.xMax - center.x),
                Mathf.Min(center.y - screen.yMin, screen.yMax - center.y));
            available -= Mathf.Max(0f, settings.rotationScreenMargin);
            // 벽 두께를 포함한 바깥 모서리의 회전 반경도 확보한다.
            float diameter = (size + Vector2.one * box.wallThickness).magnitude;
            if (available <= 0f)
                throw new System.InvalidOperationException("상자 회전을 위한 화면 공간이 없습니다.");
            if (diameter > available)
            {
                float scale = available / diameter;
                size = (size + Vector2.one * box.wallThickness) * scale
                    - Vector2.one * box.wallThickness;
            }
            Vector2 minimum = c.Player.HalfSize * 2f
                + Vector2.one * (box.wallThickness + 0.1f);
            if (size.x < minimum.x || size.y < minimum.y)
                throw new System.InvalidOperationException("회전 상자 안에 플레이어가 들어갈 공간이 없습니다.");
        }

        Vector3 targetCenter = new Vector3(center.x, center.y, start.center.z);

        float thickness = box.wallThickness;

        // Walls 순서: top, left, bottom, right
        Vector3[] targetPositions =
        {
            new Vector3(0f, size.y * 0.5f, 0f),
            new Vector3(-size.x * 0.5f, 0f, 0f),
            new Vector3(0f, -size.y * 0.5f, 0f),
            new Vector3(size.x * 0.5f, 0f, 0f)
        };

        Vector3[] targetScales =
        {
            new Vector3(size.x + thickness, thickness, 1f),
            new Vector3(thickness, size.y + thickness, 1f),
            new Vector3(size.x + thickness, thickness, 1f),
            new Vector3(thickness, size.y + thickness, 1f)
        };

        Transform[] walls = box.Walls;

        float duration =
            settings.transition == BattleBoxTransition.Instant
                ? 0f
                : Mathf.Max(0f, settings.seconds);

        float elapsed = 0f;

     
        box.SetWalls(true, false);

        while (true)
        {
            if (c.Owner.Result != BattleResult.Running)
                yield break;

            float dt = c.Clock.Delta;

            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            elapsed += dt;

            float progress = duration <= 0f
                ? 1f
                : Mathf.Clamp01(elapsed / duration);

            float t = Mathf.SmoothStep(0f, 1f, progress);

            box.transform.position = Vector3.Lerp(
                start.center, targetCenter, t);

            box.transform.rotation = Quaternion.Slerp(
                startRotation, Quaternion.identity, t);

            for (int i = 0; i < walls.Length; i++)
            {
                walls[i].localPosition = Vector3.Lerp(
                    start.positions[i], targetPositions[i], t);

                walls[i].localScale = Vector3.Lerp(
                    start.scales[i], targetScales[i], t);
            }

            
            float minX =
                box.leftWall.localPosition.x + thickness * 0.5f;
            float maxX =
                box.rightWall.localPosition.x - thickness * 0.5f;
            float minY =
                box.bottomWall.localPosition.y + thickness * 0.5f;
            float maxY =
                box.topWall.localPosition.y - thickness * 0.5f;

            
            float padding = c.Player.HalfSize.magnitude;
            float padX = Mathf.Min(
                padding, Mathf.Max(0f, maxX - minX) * 0.5f);
            float padY = Mathf.Min(
                padding, Mathf.Max(0f, maxY - minY) * 0.5f);

            Vector3 local = new Vector3(
                Mathf.Clamp(playerLocal.x, minX + padX, maxX - padX),
                Mathf.Clamp(playerLocal.y, minY + padY, maxY - padY),
                0f);

            Vector3 world = box.transform.TransformPoint(local);

            c.Player.transform.position =
                new Vector3(world.x, world.y, 0f);

            c.Player.Body.position =
                new Vector2(world.x, world.y);

            c.Player.Body.linearVelocity = Vector2.zero;

            if (progress >= 1f)
                break;

            yield return null;
        }

        c.Arena.Mode = BattleArena.Boundary.Box;
        box.SetWalls(true, true);

    }
}