using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class BDPattern08 : IBattlePattern
{
    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);

        // 체력 UI를 찾아 기존 표시 상태를 저장한다.
        var playerUI = Object.FindFirstObjectByType<PlayerUIManager>(
            FindObjectsInactive.Include);

        if (playerUI != null)
        {
            GameObject hpUI = playerUI.gameObject;
            bool wasActive = hpUI.activeSelf;

            // BD 08 동안 체력바와 체력 숫자를 함께 숨긴다.
            hpUI.SetActive(false);

            // 정상 종료하거나 다른 패턴으로 이동하면 기존 상태로 복원한다.
            scope.OnDispose(() =>
            {
                if (hpUI != null)
                    hpUI.SetActive(wasActive);
            });
        }

        float originalJumpForce = c.Player.jumpForce;
        c.Player.jumpForce = Mathf.Max(0.1f, data.bdStairJumpForce);
        scope.OnDispose(() =>
        {
            c.Player.jumpForce = originalJumpForce;
            BattlePlayerPreparationRunner.EndSpecial(c);
        });

        Rect box = c.Arena.Bounds;
        Vector2 platformSize = new Vector2(
            Mathf.Clamp(data.bdStairPlatformSize.x, 0.1f, box.width * 0.45f),
            Mathf.Max(0.05f, data.bdStairPlatformSize.y));
        float[] heights = { data.bdStairPlatformHeights.x,
            data.bdStairPlatformHeights.y, data.bdStairPlatformHeights.z };
        for (int i = 0; i < heights.Length; i++)
        {
            if (heights[i] < platformSize.y ||
                heights[i] + c.Player.HalfSize.y * 2f >= box.height ||
                (i > 0 && heights[i] <= heights[i - 1]))
            {
                Debug.LogError("BD 08 발판 높이는 낮은 순서로 지정하고, 상단에 플레이어가 설 공간을 남겨 주세요.");
                yield break;
            }
        }

        Texture2D white = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(white, new Rect(0, 0, white.width, white.height),
            new Vector2(0.5f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
        scope.OnDispose(() => Object.Destroy(square));

        for (int i = 0; i < 3; i++)
        {
            bool left = i % 2 == 0;
            var platform = CreateVisual(scope.Root, square, "BD 08 발판 " + (i + 1));
            platform.transform.position = new Vector3(
                left ? box.xMin + platformSize.x * 0.5f : box.xMax - platformSize.x * 0.5f,
                box.yMin + heights[i] - platformSize.y * 0.5f, 0f);
            platform.transform.localScale = new Vector3(platformSize.x, platformSize.y, 1f);
            platform.sortingOrder = 8;
            var collider = platform.gameObject.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;
            collider.usedByEffector = true;
            // 아래에서 통과하고 위에서 착지하는 고정 발판이다.
            var effector = platform.gameObject.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.useOneWayGrouping = true;
            effector.surfaceArc = 160f;
            effector.useSideFriction = false;
            effector.useSideBounce = false;
        }

        float delay = Mathf.Max(0f, data.bdStairClawDelay);
        float interval = Mathf.Max(0.01f, data.bdStairClawInterval);
        float grow = Mathf.Max(0.01f, data.bdStairClawGrowSeconds);
        float total = delay + 3f * interval + grow + Mathf.Max(0f, data.bdStairClawHoldSeconds);
        float length = Mathf.Max(0.1f, data.bdStairClawLength);
        float width = Mathf.Max(0.02f, data.bdStairClawWidth);
        float spacing = Mathf.Max(0f, data.bdStairClawSpacing);
        float anchorRatio = Mathf.Clamp01(data.bdStairClawPlatformAnchorRatio);
        float angle = Mathf.Clamp(data.bdStairClawAngle, 10f, 80f) * Mathf.Deg2Rad;
        float damage = Mathf.Max(0f, data.bdStairClawDamage);
        // 첫 두 묶음은 동시에, 이후 세 묶음은 발판 순서대로 등장한다.
        bool[] groupRight = { true, false, false, true, false };
        float[] spawnTimes = { delay, delay, delay + interval, delay + 2f * interval, delay + 3f * interval };
        var claws = new SpriteRenderer[groupRight.Length * 3];
        var starts = new Vector2[claws.Length];
        var directions = new Vector2[claws.Length];

        for (int i = 0; i < claws.Length; i++)
        {
            int group = i / 3;
            bool right = groupRight[group];
            float targetHeight = group < 2 ? data.bdStairClawPlatformOffsetY
                : heights[group - 2] - platformSize.y * 0.5f + data.bdStairClawPlatformOffsetY;
            Vector2 direction = new Vector2(right ? -Mathf.Cos(angle) : Mathf.Cos(angle), -Mathf.Sin(angle));
            directions[i] = direction;
            // 좌우에 같은 설정을 대칭 적용한다.
            float side = right ? 1f : -1f;

            // 할퀴기 방향과 직각인 방향으로 세 줄을 나란히 배치한다.
            Vector2 spreadDirection = new Vector2(
                side * Mathf.Sin(angle),
                -Mathf.Cos(angle));

            // 기준 비율은 시작점 0부터 끝점 1까지이며, 기본 0.75 지점을 발판에 맞춘다.
            // 가로 보정은 양수일 때 상자 안쪽, 음수일 때 벽 바깥쪽으로 이동한다.
            // 첫 두 묶음은 발판 대신 바닥의 같은 가로 위치를 기준으로 삼는다.
            float wallX = right ? box.xMax : box.xMin;
            Vector2 target = new Vector2(
                wallX - side * (platformSize.x * 0.5f + data.bdStairClawPlatformOffsetX),
                box.yMin + targetHeight);
            Vector2 middleStart = target - direction * (length * anchorRatio);

            starts[i] = middleStart
                + spreadDirection * ((i % 3 - 1) * spacing);
            claws[i] = CreateVisual(scope.Root, square, "BD 08 할퀴기 " + (i + 1));
            claws[i].transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            claws[i].enabled = false;
        }

        // 첫 좌우 할퀴기가 완성되면 바닥 장애물이 동시에 자라난다.
        float floorSpawn = delay + grow;
        float floorGrow = Mathf.Max(0.01f, data.bdStairFloorGrowSeconds);
        float floorWidth = Mathf.Clamp(data.bdStairFloorSize.x, 0.02f, box.width);
        float floorHeight = Mathf.Max(0.02f, data.bdStairFloorSize.y);
        float floorGap = Mathf.Max(0f, data.bdStairFloorGap);
        float floorDamage = Mathf.Max(0f, data.bdStairFloorDamage);
        float pitch = floorWidth + floorGap;
        int floorCount = Mathf.Max(1, Mathf.CeilToInt(box.width / pitch));
        var floorSquares = new SpriteRenderer[floorCount];
        var floorStarts = new Vector2[floorCount];
        var floorWidths = new float[floorCount];
        for (int i = 0; i < floorCount; i++)
        {
            float left = box.xMin + i * pitch;
            floorWidths[i] = Mathf.Min(floorWidth, box.xMax - left);
            floorStarts[i] = new Vector2(left + floorWidths[i] * 0.5f, box.yMin);
            floorSquares[i] = CreateVisual(scope.Root, square, "BD 08 바닥 Square " + (i + 1));
            floorSquares[i].enabled = false;
        }

        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;
        while (elapsed < total && c.Owner.Result == BattleResult.Running)
        {
            float dt = Mathf.Min(c.Clock.Delta, total - elapsed);
            if (dt <= 0f)
            {
                previousPlayer = c.Player.transform.position;
                yield return null;
                continue;
            }

            float nextTime = elapsed + dt;
            Vector2 player = c.Player.transform.position;
            float maxGrowth = 0f;
            for (int group = 0; group < spawnTimes.Length; group++)
            {
                float spawn = spawnTimes[group];
                maxGrowth = Mathf.Max(maxGrowth, length *
                    (Mathf.Clamp01((nextTime - spawn) / grow) - Mathf.Clamp01((elapsed - spawn) / grow)));
            }
            maxGrowth = Mathf.Max(maxGrowth, floorHeight *
                (Mathf.Clamp01((nextTime - floorSpawn) / floorGrow) - Mathf.Clamp01((elapsed - floorSpawn) / floorGrow)));
            float sampleDistance = Mathf.Min(0.04f, Mathf.Min(width, floorWidth) * 0.5f);
            int samples = Mathf.Max(1, Mathf.CeilToInt(
                (maxGrowth + Vector2.Distance(previousPlayer, player)) / sampleDistance));
            bool hit = false;
            // 길어지는 끝점과 플레이어의 이동을 나누어 접촉 누락을 줄인다.
            for (int sample = 1; sample <= samples && !hit && (damage > 0f || floorDamage > 0f); sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;
                if (time >= total) break;
                Vector2 point = Vector2.Lerp(previousPlayer, player, blend);
                for (int i = 0; i < claws.Length; i++)
                {
                    float visibleLength = length * Mathf.Clamp01((time - spawnTimes[i / 3]) / grow);
                    if (damage <= 0f || !Touches(point, c.Player.HitRadius + sampleDistance * 0.5f,
                        starts[i], directions[i], visibleLength, width)) continue;
                    c.Player.TakeDamage(damage);
                    hit = true;
                    if (c.Owner.Result != BattleResult.Running) yield break;
                    break;
                }

                float currentFloorHeight = floorHeight * Mathf.Clamp01((time - floorSpawn) / floorGrow);
                for (int i = 0; i < floorCount && !hit && floorDamage > 0f; i++)
                {
                    if (!Touches(point, c.Player.HitRadius + sampleDistance * 0.5f,
                        floorStarts[i], Vector2.up, currentFloorHeight, floorWidths[i])) continue;
                    c.Player.TakeDamage(floorDamage);
                    hit = true;
                    if (c.Owner.Result != BattleResult.Running) yield break;
                }
            }

            // 중심을 높이의 절반만큼 올려 아래쪽 끝을 바닥에 고정한다.
            float visibleFloorHeight = floorHeight * Mathf.Clamp01((nextTime - floorSpawn) / floorGrow);
            for (int i = 0; i < floorCount; i++)
            {
                floorSquares[i].transform.position = floorStarts[i] + Vector2.up * (visibleFloorHeight * 0.5f);
                floorSquares[i].transform.localScale = new Vector3(floorWidths[i], visibleFloorHeight, 1f);
                floorSquares[i].enabled = visibleFloorHeight > 0f;
            }

            for (int i = 0; i < claws.Length; i++)
            {
                float visibleLength = length * Mathf.Clamp01((nextTime - spawnTimes[i / 3]) / grow);
                claws[i].transform.position = starts[i] + directions[i] * (visibleLength * 0.5f);
                claws[i].transform.localScale = new Vector3(visibleLength, width, 1f);
                claws[i].enabled = visibleLength > 0f;
            }
            elapsed = nextTime;
            previousPlayer = player;
            yield return null;
        }
        // 발판, 할퀴기, 바닥 장애물은 실행 범위가 정리될 때 함께 제거된다.
    }

    private static SpriteRenderer CreateVisual(Transform parent, Sprite sprite, string name)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = 15;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private static bool Touches(Vector2 player, float radius, Vector2 start,
        Vector2 direction, float length, float width)
    {
        if (length <= 0f) return false;
        Vector2 delta = player - start;
        Vector2 normal = new Vector2(-direction.y, direction.x);
        Vector2 local = new Vector2(Vector2.Dot(delta, direction), Vector2.Dot(delta, normal));
        Vector2 nearest = new Vector2(Mathf.Clamp(local.x, 0f, length),
            Mathf.Clamp(local.y, -width * 0.5f, width * 0.5f));
        return (local - nearest).sqrMagnitude <= radius * radius;
    }
}