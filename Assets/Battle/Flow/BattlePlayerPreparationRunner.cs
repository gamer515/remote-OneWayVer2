using UnityEngine;

public static class BattlePlayerPreparationRunner
{
    public static void Apply(
        BattleContext c,
        BattlePlayerPreparation settings)
    {
        if (settings == null)
            settings = new BattlePlayerPreparation();

        // 준비 중에는 플레이어 입력을 잠근다.
        c.Player.Lock(true);

        bool useBox =
            settings.boundary == BattlePlayerBoundary.Box;

        c.Arena.Mode = useBox
            ? BattleArena.Boundary.Box
            : BattleArena.Boundary.Screen;

        // 상자를 사용하지 않으면 벽 표시와 벽 충돌을 끈다.
        c.Box.SetWalls(useBox, useBox);

        PlayerController.MovementMode movement =
            settings.movement == BattleMouseMovement.Gravity
                ? PlayerController.MovementMode.Gravity
                : PlayerController.MovementMode.Free;

        c.Player.SetVisible(settings.visible);
        c.Player.SetMovementMode(movement);

        // 일반 마우스 이동에서는 Rigidbody2D를 사용한다.
        c.Player.Body.simulated = true;
        c.Player.Body.linearVelocity = Vector2.zero;
        c.Player.Body.angularVelocity = 0f;

        Vector2 position = c.Player.transform.position;

        if (settings.startPosition ==
            BattlePlayerStartPosition.Normalized)
        {
            Rect bounds = c.Arena.Bounds;
            Vector2 half = c.Player.HalfSize;

            float paddingX = Mathf.Min(
                half.x, bounds.width * 0.5f);

            float paddingY = Mathf.Min(
                half.y, bounds.height * 0.5f);

            float x = Mathf.Clamp01(settings.normalizedPosition.x);
            float y = Mathf.Clamp01(settings.normalizedPosition.y);

            // 플레이어 몸이 벽에 겹치지 않는 범위에서 위치를 계산한다.
            position = new Vector2(
                Mathf.Lerp(
                    bounds.xMin + paddingX,
                    bounds.xMax - paddingX,
                    x),
                Mathf.Lerp(
                    bounds.yMin + paddingY,
                    bounds.yMax - paddingY,
                    y)
            );
        }

        // 현재 위치를 유지하는 경우에도 범위를 벗어나지 않게 보정한다.
        position = c.Arena.Clamp(position, c.Player.HalfSize);

        c.Player.transform.position =
            new Vector3(position.x, position.y, 0f);

        c.Player.Body.position = position;

        // 준비 완료 후 입력을 허용한다.
        c.Player.Lock(false);
    }

    public static void EndSpecial(BattleContext c)
    {
        // 다음 단계가 준비할 때까지 현재 위치를 유지하고 이동을 잠근다.
        // 회전된 상자에 일반 좌표계의 범위 보정을 적용하지 않는다.
        c.Player.Lock(true);
        c.Player.SetMovementMode(PlayerController.MovementMode.Free);
        c.Player.Body.position = c.Player.transform.position;
        c.Player.Body.linearVelocity = Vector2.zero;
        c.Player.Body.angularVelocity = 0f;
        c.Player.Body.simulated = true;
        c.Arena.Mode = BattleArena.Boundary.None;
        c.Box.SetWalls(true, false);
    }

}