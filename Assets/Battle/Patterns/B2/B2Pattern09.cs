using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class B2Pattern09 : IBattlePattern
{
    // ───── 조절할 설정 ─────
    private const float TimeLimit = 30f;
    private const float RotationSpeed = 15f; // 초당 회전 각도
    private const float WallDamage = 1f;
    private const float TimeoutDamage = 5f;

    private const float WallWidth = 0.16f;
    private const float SpiralTurns = 2.3f;
    private const float InnerRadius = 0.85f;
    private const float GoalRadius = 0.4f;
    private const int SegmentCount = 180;

    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);
        scope.OnDispose(() => BattlePlayerPreparationRunner.EndSpecial(c));

        // 회전 좌표계의 이동과 충돌은 이 패턴에서 직접 처리한다.
        c.Player.SetMovementMode(PlayerController.MovementMode.External);
        c.Player.Body.simulated = false;
        c.Arena.Mode = BattleArena.Boundary.None;

        Vector2 center = c.Box.transform.position;
        float width = c.Box.rightWall.localPosition.x - c.Box.leftWall.localPosition.x;
        float height = c.Box.topWall.localPosition.y - c.Box.bottomWall.localPosition.y;
        if (Mathf.Abs(width - height) > 0.01f || width <= 0f)
            throw new System.InvalidOperationException(
                "패턴 09의 상자 준비에는 정사각형 크기를 지정해주세요.");
        float side = width;

        float thickness = c.Box.wallThickness;
        float innerHalf = side * 0.5f - thickness * 0.5f;
        float playerRadius = c.Player.HitRadius;

        // 회오리 바깥쪽 끝을 bottomWall 가까이까지 확장.
        // 0.05f는 회오리 벽과 BattleBox 사이의 여백.
        float outerRadius = innerHalf - WallWidth * 0.5f - 0.05f;

        // 너무 작은 화면에서는 통로를 확보할 수 없다.
        float pitch = (outerRadius - InnerRadius) / SpiralTurns;
        if (pitch <= playerRadius * 2f + WallWidth + 0.12f)
        {
            throw new System.InvalidOperationException(
                "Pattern09: 회오리 통로가 너무 좁습니다. " +
                "카메라 표시 범위를 넓히거나 SpiralTurns를 줄여주세요.");
        }

        // 물리 충돌 대신 아래의 회전 좌표계 충돌을 사용.
        c.Box.SetWalls(true, false);

        var rotor = new GameObject("Spiral Rotor").transform;
        rotor.SetParent(scope.Root, false);
        rotor.position = new Vector3(center.x, center.y, 0f);

        Texture2D texture = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            Vector2.one * 0.5f,
            texture.width,
            0,
            SpriteMeshType.FullRect);

        scope.OnDispose(() => Object.Destroy(square));

        // 회오리 중심선의 점들을 계산.
        var points = new Vector2[SegmentCount + 1];

        for (int i = 0; i <= SegmentCount; i++)
        {
            float t = (float)i / SegmentCount;
            float radius = Mathf.Lerp(outerRadius, InnerRadius, t);
            float angle = (-90f + t * SpiralTurns * 360f)
                * Mathf.Deg2Rad;

            points[i] = new Vector2(
                Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        // 짧은 Square들을 이어서 곡선 벽 생성.
        for (int i = 0; i < SegmentCount; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[i + 1];
            Vector2 direction = b - a;

            Transform wall = MakeSquare(
                "Spiral Wall " + i, square, rotor, Color.white);

            wall.localPosition = (a + b) * 0.5f;
            wall.localRotation = Quaternion.Euler(
                0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);

            // 조금 겹쳐서 이어 붙여 이음새를 숨긴다.
            wall.localScale = new Vector3(
                direction.magnitude + 0.025f,
                WallWidth,
                1f);
        }

        // 중심의 도착 표시.
        Transform goal = MakeSquare(
            "Spiral Goal", square, rotor,
            new Color(0.4f, 1f, 0.6f));

        goal.localPosition = Vector3.zero;
        goal.localScale = Vector3.one * 0.25f;

        // 바깥쪽 통로에서 시작.
        // BattleBox 왼쪽 아래에서 시작.
        // 플레이어 모습이 바깥 벽에 겹치지 않도록 여유를 둔다.
        float startPadding = Mathf.Max(
            c.Player.HalfSize.x,
            c.Player.HalfSize.y
        ) + 0.15f;

        Vector2 start = new Vector2(
            -innerHalf + startPadding,
            -innerHalf + startPadding
        );

        SetPlayerPosition(c, center + start);

        float elapsed = 0f;
        float rotation = 0f;
        Vector2 lastSafeLocal = start;

        while (elapsed < TimeLimit &&
               c.Owner.Result == BattleResult.Running)
        {
            float dt = Mathf.Min(c.Clock.Delta, TimeLimit - elapsed);

            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            Vector2 mouseTarget = c.Player.transform.position;

            Ray ray = c.Camera.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(Vector3.forward, Vector3.zero);

            if (plane.Raycast(ray, out float distance))
                mouseTarget = ray.GetPoint(distance);

            float moveSpeed = Mathf.Max(0f, c.Player.b2MouseMoveSpeed);

            
            float motionBound =
                moveSpeed +
                Mathf.Abs(RotationSpeed) * Mathf.Deg2Rad * side;

            int samples = Mathf.Max(
                1, Mathf.CeilToInt(motionBound * dt / 0.025f));

            float step = dt / samples;
            bool damagedThisFrame = false;

            for (int sample = 0; sample < samples; sample++)
            {
                rotation += RotationSpeed * step;

                Quaternion q = Quaternion.Euler(0f, 0f, rotation);
                c.Box.transform.rotation = q;
                rotor.rotation = q;

                Vector2 world = Vector2.MoveTowards(
                    c.Player.transform.position,
                    mouseTarget,
                    moveSpeed * step);

                
                Vector2 local = Quaternion.Inverse(q) *
                    (Vector3)(world - center);

                bool touched = false;

               
                for (int pass = 0; pass < 6; pass++)
                {
                    float limit = innerHalf - playerRadius;
                    local.x = Mathf.Clamp(local.x, -limit, limit);
                    local.y = Mathf.Clamp(local.y, -limit, limit);

                    bool corrected = false;

                    for (int i = 0; i < SegmentCount; i++)
                    {
                        Vector2 nearest = ClosestPoint(
                            local, points[i], points[i + 1]);

                        Vector2 delta = local - nearest;
                        float safeDistance =
                            playerRadius + WallWidth * 0.5f;

                        if (delta.sqrMagnitude >=
                            safeDistance * safeDistance)
                            continue;

                        touched = true;
                        corrected = true;

                        Vector2 normal = delta.sqrMagnitude > 0.000001f
                            ? delta.normalized
                            : (lastSafeLocal - nearest).normalized;

                        if (normal.sqrMagnitude < 0.5f)
                            normal = Vector2.up;

                        local = nearest + normal * (safeDistance + 0.001f);
                    }

                    if (!corrected)
                        break;
                }

                
                if (!IsSafe(local, points, innerHalf, playerRadius))
                {
                    local = lastSafeLocal;
                    touched = true;
                }
                else
                {
                    lastSafeLocal = local;
                }

                Vector2 nextWorld = center + (Vector2)(q * (Vector3)local);
                SetPlayerPosition(c, nextWorld);

                if (touched && !damagedThisFrame)
                {
                    damagedThisFrame = true;
                    c.Player.TakeDamage(WallDamage);

                    if (c.Owner.Result != BattleResult.Running)
                        yield break;
                }

                
                if (local.sqrMagnitude <= GoalRadius * GoalRadius)
                    yield break;
            }

            elapsed += dt;
            yield return null;
        }

        
        if (c.Owner.Result == BattleResult.Running)
            c.Player.TakeArrivalDamage(TimeoutDamage);
    }

    private static bool IsSafe(
        Vector2 point, Vector2[] points,
        float innerHalf, float playerRadius)
    {
        float limit = innerHalf - playerRadius;

        if (Mathf.Abs(point.x) > limit + 0.001f ||
            Mathf.Abs(point.y) > limit + 0.001f)
            return false;

        float radius = playerRadius + WallWidth * 0.5f;

        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector2 nearest = ClosestPoint(
                point, points[i], points[i + 1]);

            if ((point - nearest).sqrMagnitude <
                radius * radius - 0.00001f)
                return false;
        }

        return true;
    }

    private static Vector2 ClosestPoint(
        Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 line = b - a;
        float t = Mathf.Clamp01(
            Vector2.Dot(point - a, line) /
            Mathf.Max(0.000001f, line.sqrMagnitude));

        return a + line * t;
    }

    private static Transform MakeSquare(
        string name, Sprite sprite, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);

        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = 15;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return go.transform;
    }

    private static void SetPlayerPosition(
        BattleContext c, Vector2 position)
    {
        c.Player.transform.position =
            new Vector3(position.x, position.y, 0f);

        c.Player.Body.position = position;
        c.Player.Body.linearVelocity = Vector2.zero;
    }

}
