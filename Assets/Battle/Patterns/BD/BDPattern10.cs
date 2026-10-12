using System.Collections;
using UnityEngine;

public sealed class BDPattern10 : IBattlePattern
{
    private const float BoxSize = 2f;

    private sealed class Laser
    {
        public SpriteRenderer emitter;
        public SpriteRenderer beam;
        public Vector2 outside;
        public Vector2 target;
        public Rect hitRect;
    }

    // 번호는 그림의 발사체 번호를 그대로 사용한다.
    private static readonly int[,] Pairs =
    {
        { 3, 8 }, { 3, 5 }, { 6, 8 }, // A 안전
        { 2, 8 }, { 2, 5 }, { 7, 5 }, // B 안전
        { 2, 4 }, { 7, 1 }, { 7, 4 }, // C 안전
        { 3, 1 }, { 6, 1 }, { 6, 4 }  // D 안전
    };

    private BattleContext context;
    private Vector2[] centers;
    private Rect screen;
    private int currentBox;

    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {
        context = c;
        screen = c.Arena.ScreenRect;

        // A, B, C, D 순서.
        centers = new[]
        {
            Point(0.30f, 0.70f),
            Point(0.30f, 0.30f),
            Point(0.70f, 0.30f),
            Point(0.70f, 0.70f)
        };

        currentBox = 0;

        // 레이저가 이웃 행이나 열까지 덮지 않을 공간을 확보한다.
        float beamWidth = BoxSize + 0.2f;
        float required = BoxSize * 0.5f
            + beamWidth * 0.5f + c.Player.HalfSize.magnitude;

        if (centers[3].x - centers[0].x <= required ||
            centers[0].y - centers[1].y <= required)
        {
            Debug.LogError("BD 10: 네 박스와 레이저를 배치할 화면 공간이 부족합니다.");
            yield break;
        }

        scope.OnDispose(() =>
        {
            // 마지막에 사용한 박스 위치를 다음 패턴에 이어 준다.
            Vector3 position = c.Box.transform.position;
            position.x = centers[currentBox].x;
            position.y = centers[currentBox].y;
            c.Box.transform.position = position;

            BattlePlayerPreparationRunner.EndSpecial(c);
        });

        c.Player.Lock(true);
        c.Player.SetVisible(true);
        c.Player.SetMovementMode(PlayerController.MovementMode.External);

        // 기존 박스를 왼쪽 위로 이동하면서 2×2로 줄인다.
        yield return BattleBoxTransitionRunner.Run(
            c,
            new BattleBoxPreparation
            {
                transition = BattleBoxTransition.Morph,
                size = new Vector2(BoxSize, BoxSize),
                keepCurrentCenter = false,
                useScreenCenter = false,
                center = centers[0],
                seconds = Mathf.Max(0.01f, data.bdQuadMoveSeconds)
            });

        if (c.Owner.Result != BattleResult.Running)
            yield break;

        SetPlayer(centers[0]);

        Texture2D white = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(
            white,
            new Rect(0f, 0f, white.width, white.height),
            new Vector2(0.5f, 0.5f),
            white.width,
            0,
            SpriteMeshType.FullRect);

        scope.OnDispose(() => Object.Destroy(square));

        // 나머지 세 박스는 각자 위치에서 동시에 커지며 등장한다.
        Transform[] extraBoxes = new Transform[3];

        for (int i = 0; i < 3; i++)
        {
            extraBoxes[i] = CreateBox(
                scope.Root, square, centers[i + 1],
                c.Box.wallThickness, i + 1);

            extraBoxes[i].localScale = Vector3.zero;
        }

        float createTime = Mathf.Max(0.01f, data.bdQuadCreateSeconds);
        float elapsed = 0f;

        while (elapsed < createTime &&
               c.Owner.Result == BattleResult.Running)
        {
            elapsed += c.Clock.Delta;
            float ratio = Mathf.Clamp01(elapsed / createTime);
            ratio = Mathf.SmoothStep(0f, 1f, ratio);

            foreach (Transform box in extraBoxes)
                box.localScale = Vector3.one * ratio;

            yield return null;
        }

        if (c.Owner.Result != BattleResult.Running)
            yield break;

        foreach (Transform box in extraBoxes)
            box.localScale = Vector3.one;

        // 원래 박스의 충돌벽에 다른 구역 이동이 막히지 않게 한다.
        // 네 박스 내부 제한은 MovePlayer에서 직접 처리한다.
        c.Box.SetWalls(true, false);
        c.Arena.Mode = BattleArena.Boundary.None;
        c.Player.Body.simulated = false;
        c.Player.Lock(false);

        float enter = Mathf.Max(0.01f, data.bdQuadEnterSeconds);
        float aim = Mathf.Max(0f, data.bdQuadAimSeconds);
        float hold = Mathf.Max(0.01f, data.bdQuadHoldSeconds);
        float fade = Mathf.Max(0f, data.bdQuadFadeSeconds);
        float exit = Mathf.Max(0.01f, data.bdQuadExitSeconds);

        float fireAt = enter + aim;
        float fadeAt = fireAt + hold;
        float exitAt = fadeAt + fade;
        float lifetime = exitAt + exit;

        int attackCount = Mathf.Max(1, data.bdQuadAttackCount);

        for (int wave = 0; wave < attackCount; wave++)
        {
            if (c.Owner.Result != BattleResult.Running)
                yield break;

            // 안전한 박스를 고른 뒤 해당 박스의 세 조합 중 하나를 선택한다.
            int safeBox = Random.Range(0, 4);
            int pair = safeBox * 3 + Random.Range(0, 3);

            Laser horizontal = CreateLaser(
                Pairs[pair, 0], c, data, scope.Root, square, beamWidth);

            Laser vertical = CreateLaser(
                Pairs[pair, 1], c, data, scope.Root, square, beamWidth);

            elapsed = 0f;

            while (elapsed < lifetime &&
                   c.Owner.Result == BattleResult.Running)
            {
                float dt = Mathf.Min(c.Clock.Delta, lifetime - elapsed);

                if (dt <= 0f)
                {
                    yield return null;
                    continue;
                }

                MovePlayer();

                float nextTime = elapsed + dt;

                UpdateLaser(
                    horizontal, nextTime,
                    enter, fireAt, fadeAt, exitAt, exit);

                UpdateLaser(
                    vertical, nextTime,
                    enter, fireAt, fadeAt, exitAt, exit);

                // 구역 이동은 순간 이동이므로 박스 사이 경로는 공격 판정하지 않는다.
                // 이번 프레임이 발사 구간과 겹칠 때 현재 위치를 검사한다.
                bool firing = nextTime > fireAt && elapsed < fadeAt;

                if (firing && data.bdQuadDamage > 0f)
                {
                    Vector2 player = c.Player.transform.position;

                    if (Touches(horizontal.hitRect, player, c.Player.HitRadius) ||
                        Touches(vertical.hitRect, player, c.Player.HitRadius))
                    {
                        c.Player.TakeDamage(data.bdQuadDamage);
                    }
                }

                elapsed = nextTime;
                yield return null;
            }

            DestroyLaser(horizontal);
            DestroyLaser(vertical);

            if (wave + 1 < attackCount)
            {
                elapsed = 0f;
                float gap = Mathf.Max(0f, data.bdQuadAttackGap);

                while (elapsed < gap &&
                       c.Owner.Result == BattleResult.Running)
                {
                    if (c.Clock.Delta > 0f)
                    {
                        MovePlayer();
                        elapsed += c.Clock.Delta;
                    }

                    yield return null;
                }
            }
        }
    }

    private Vector2 Point(float x, float y)
    {
        return new Vector2(
            Mathf.Lerp(screen.xMin, screen.xMax, x),
            Mathf.Lerp(screen.yMin, screen.yMax, y));
    }

    private void MovePlayer()
    {
        Ray ray = context.Camera.ScreenPointToRay(Input.mousePosition);
        Plane plane = new Plane(Vector3.forward, Vector3.zero);

        if (!plane.Raycast(ray, out float distance))
            return;

        Vector2 mouse = ray.GetPoint(distance);
        bool right = mouse.x >= screen.center.x;
        bool top = mouse.y >= screen.center.y;

        currentBox = top
            ? (right ? 3 : 0)
            : (right ? 2 : 1);

        // 각 화면 구역의 마우스 좌표를 해당 박스 내부 좌표로 변환한다.
        float minX = right ? screen.center.x : screen.xMin;
        float maxX = right ? screen.xMax : screen.center.x;
        float minY = top ? screen.center.y : screen.yMin;
        float maxY = top ? screen.yMax : screen.center.y;

        float u = Mathf.InverseLerp(minX, maxX, mouse.x);
        float v = Mathf.InverseLerp(minY, maxY, mouse.y);

        Vector2 half = context.Player.HalfSize;
        float insideHalf = BoxSize * 0.5f
            - context.Box.wallThickness * 0.5f;

        float rangeX = Mathf.Max(0f, insideHalf - half.x);
        float rangeY = Mathf.Max(0f, insideHalf - half.y);

        Vector2 center = centers[currentBox];

        SetPlayer(new Vector2(
            center.x + Mathf.Lerp(-rangeX, rangeX, u),
            center.y + Mathf.Lerp(-rangeY, rangeY, v)));
    }

    private void SetPlayer(Vector2 position)
    {
        context.Player.transform.position =
            new Vector3(position.x, position.y, 0f);
        context.Player.Body.position = position;
    }

    private static Transform CreateBox(
        Transform parent, Sprite square,
        Vector2 center, float thickness, int index)
    {
        var root = new GameObject("BD 10 추가 박스 " + index).transform;
        root.SetParent(parent, false);
        root.position = center;

        Vector2[] positions =
        {
            new Vector2(0f, 1f),
            new Vector2(0f, -1f),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        };

        for (int i = 0; i < 4; i++)
        {
            var wall = CreateVisual(root, square, "벽 " + i);
            wall.transform.localPosition = positions[i];
            wall.transform.localScale = i < 2
                ? new Vector3(BoxSize + thickness, thickness, 1f)
                : new Vector3(thickness, BoxSize + thickness, 1f);
        }

        return root;
    }

    private Laser CreateLaser(
        int number,
        BattleContext c,
        BattlePatternData data,
        Transform parent,
        Sprite square,
        float beamWidth)
    {
        int boxIndex;
        Vector2 direction;

        switch (number)
        {
            case 1: boxIndex = 0; direction = Vector2.down; break;
            case 2: boxIndex = 0; direction = Vector2.right; break;
            case 3: boxIndex = 1; direction = Vector2.right; break;
            case 4: boxIndex = 1; direction = Vector2.up; break;
            case 5: boxIndex = 2; direction = Vector2.up; break;
            case 6: boxIndex = 2; direction = Vector2.left; break;
            case 7: boxIndex = 3; direction = Vector2.left; break;
            default: boxIndex = 3; direction = Vector2.down; break;
        }

        float size = Mathf.Max(0.1f, data.bdQuadEmitterSize);
        float gap = Mathf.Max(0f, data.bdQuadEmitterGap);

        Vector2 target = centers[boxIndex] - direction * (
            BoxSize * 0.5f + c.Box.wallThickness * 0.5f
            + gap + size * 0.5f);

        Vector2 outside = target;

        if (direction.x > 0f) outside.x = screen.xMin - size;
        else if (direction.x < 0f) outside.x = screen.xMax + size;
        else if (direction.y > 0f) outside.y = screen.yMin - size;
        else outside.y = screen.yMax + size;

        var emitter = CreateVisual(
            parent, square, "BD 10 발사체 " + number);
        emitter.transform.localScale = new Vector3(size, size, 1f);
        emitter.transform.position = outside;

        Vector2 tip = target + direction * size * 0.5f;
        Rect rect;

        if (direction.x > 0f)
            rect = Rect.MinMaxRect(
                tip.x, tip.y - beamWidth * 0.5f,
                screen.xMax, tip.y + beamWidth * 0.5f);
        else if (direction.x < 0f)
            rect = Rect.MinMaxRect(
                screen.xMin, tip.y - beamWidth * 0.5f,
                tip.x, tip.y + beamWidth * 0.5f);
        else if (direction.y > 0f)
            rect = Rect.MinMaxRect(
                tip.x - beamWidth * 0.5f, tip.y,
                tip.x + beamWidth * 0.5f, screen.yMax);
        else
            rect = Rect.MinMaxRect(
                tip.x - beamWidth * 0.5f, screen.yMin,
                tip.x + beamWidth * 0.5f, tip.y);

        var beam = CreateVisual(
            parent, square, "BD 10 레이저 " + number);
        beam.transform.position = rect.center;
        beam.transform.localScale =
            new Vector3(rect.width, rect.height, 1f);
        beam.enabled = false;

        return new Laser
        {
            emitter = emitter,
            beam = beam,
            outside = outside,
            target = target,
            hitRect = rect
        };
    }

    private static void UpdateLaser(
        Laser laser, float time,
        float enter, float fireAt,
        float fadeAt, float exitAt, float exit)
    {
        if (time < enter)
            laser.emitter.transform.position = Vector2.Lerp(
                laser.outside, laser.target, time / enter);
        else if (time < exitAt)
            laser.emitter.transform.position = laser.target;
        else
            laser.emitter.transform.position = Vector2.Lerp(
                laser.target, laser.outside,
                Mathf.Clamp01((time - exitAt) / exit));

        laser.beam.enabled = time >= fireAt && time < exitAt;

        float alpha = time < fadeAt ? 1f
            : exitAt <= fadeAt ? 0f
            : 1f - Mathf.Clamp01((time - fadeAt) / (exitAt - fadeAt));

        laser.beam.color = new Color(1f, 1f, 1f, alpha);
    }

    private static SpriteRenderer CreateVisual(
        Transform parent, Sprite sprite, string name)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);

        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = 15;
        return renderer;
    }

    private static bool Touches(
        Rect rect, Vector2 point, float radius)
    {
        Vector2 nearest = new Vector2(
            Mathf.Clamp(point.x, rect.xMin, rect.xMax),
            Mathf.Clamp(point.y, rect.yMin, rect.yMax));

        return (point - nearest).sqrMagnitude <= radius * radius;
    }

    private static void DestroyLaser(Laser laser)
    {
        laser.emitter.gameObject.SetActive(false);
        laser.beam.gameObject.SetActive(false);
        Object.Destroy(laser.emitter.gameObject);
        Object.Destroy(laser.beam.gameObject);
    }
}