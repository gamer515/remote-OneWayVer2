using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class B1DebrisPattern : IBattlePattern
{
    private sealed class Debris
    {
        public Transform visual;
        public Vector2 velocity;
        public float rotationSpeed;

        public float size;
        public float hitRadius;

        public bool isFragment;
        public bool canSplit;
        public float splitTimer;

        public Vector2 bounceStart;
        public float bounceElapsed;
        public float bounceDuration;
        public float bounceWidth;
        public float bounceSide;
        public float bounceSlowdown;

        public bool wallReflected;
    }

    public IEnumerator Run(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope)
    {
        if (c.Owner.b1Rock1 == null ||
            c.Owner.b1Rock2 == null ||
            c.Owner.b1BrokenRockFragment == null)
        {
            Debug.LogError(
                "BattleSceneController에 B1 돌 이미지 3개를 연결해주세요.");
            yield break;
        }

        c.Arena.Mode = BattleArena.Boundary.Corridor;
        c.Player.SetMovementMode(PlayerController.MovementMode.External);
        c.Player.Lock(false);
        c.Player.SetVisible(true);

        var movement = new B1ScrollMovement();
        var debrisList = new List<Debris>();

        float elapsed = 0f;
        float spawnTimer = 0f;

        while (elapsed < data.duration &&
               c.Owner.Result == BattleResult.Running)
        {
            if (movement.Tick(c))
            {
                yield return new B1RecoveryController().Run(c, movement);
            }

            if (c.Owner.Result != BattleResult.Running)
                yield break;

            float dt = c.Clock.Delta;

            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            elapsed += dt;
            spawnTimer -= dt;

            Rect bounds = c.Arena.Bounds;

            // 처음 생성되는 돌
            if (spawnTimer <= 0f)
            {
                spawnTimer = Mathf.Max(0.05f, data.interval);

                Vector2 range = c.Owner.b1DebrisSizeRange;
                float minSize = Mathf.Max(
                    0.01f, Mathf.Min(range.x, range.y));
                float maxSize = Mathf.Max(
                    minSize, Mathf.Max(range.x, range.y));

                float size = Random.Range(minSize, maxSize);

                float margin = Mathf.Min(
                    size * 0.5f, bounds.width * 0.5f);

                Vector2 position = new Vector2(
                    Random.Range(
                        bounds.xMin + margin,
                        bounds.xMax - margin),
                    bounds.yMax);

                SpawnDebris(
                    c,
                    scope,
                    debrisList,
                    position,
                    size,
                    Vector2.down * Mathf.Max(0.1f, data.speed),
                    false);
            }

            // 뒤에서부터 순회하여 제거 및 파편 추가를 처리합니다.
            for (int i = debrisList.Count - 1; i >= 0; i--)
            {
                Debris debris = debrisList[i];

                bool hit = MoveAndCheckHit(c, debris, dt);

                debris.visual.Rotate(
                    0f, 0f, debris.rotationSpeed * dt, Space.Self);

                Vector2 after = debris.visual.position;

                // 좌우는 반사시키고, 화면 아래로 나가면 제거.
                bool outside = after.y < bounds.yMin - debris.size;

                if (hit || outside)
                {
                    Object.Destroy(debris.visual.gameObject);
                    debrisList.RemoveAt(i);

                    if (hit)
                    {
                        c.Player.TakeDamage(1f);

                        if (c.Owner.Result != BattleResult.Running)
                            yield break;
                    }

                    continue;
                }

                // 큰 원본 돌만 분열합니다.
                if (debris.canSplit)
                {
                    debris.splitTimer -= dt;

                    if (debris.splitTimer <= 0f)
                    {
                        SplitDebris(c, data, scope, debrisList, debris);

                        Object.Destroy(debris.visual.gameObject);
                        debrisList.RemoveAt(i);
                    }
                }
            }

            yield return null;
        }

        c.Player.Lock(true);
    }

    private void SpawnDebris(
        BattleContext c,
        BattleStepScope scope,
        List<Debris> debrisList,
        Vector2 position,
        float size,
        Vector2 velocity,
        bool isFragment)
    {
        size = Mathf.Max(0.01f, size);

        // 분열된 파편 여부를 크기보다 우선해서 확인합니다.
        bool useRockImage =
            !isFragment &&
            size >= c.Owner.b1LargeRockThreshold;

        Sprite sprite = useRockImage
            ? (Random.value < 0.5f
                ? c.Owner.b1Rock1
                : c.Owner.b1Rock2)
            : c.Owner.b1BrokenRockFragment;

        var go = new GameObject(
            "Debris",
            typeof(SpriteRenderer));

        go.transform.SetParent(scope.Root, false);
        go.transform.position = new Vector3(
            position.x, position.y, 0f);

        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 5;

        // 긴 쪽의 월드 길이를 size에 맞춥니다.
        float longestSide = Mathf.Max(
            sprite.bounds.size.x,
            sprite.bounds.size.y);

        float scale = size / Mathf.Max(0.001f, longestSide);

        go.transform.localScale = Vector3.one * scale;
        go.transform.rotation = Quaternion.Euler(
            0f, 0f, Random.Range(0f, 360f));

        // 이미지의 짧은 쪽에 맞춘 작은 원형 판정
        float radius = Mathf.Min(
            sprite.bounds.extents.x,
            sprite.bounds.extents.y) * scale;

        var debris = new Debris
        {
            visual = go.transform,
            velocity = velocity,

            rotationSpeed = Random.Range(40f, 180f)
        * (Random.value < 0.5f ? -1f : 1f),

            size = size,
            hitRadius = radius,

            isFragment = isFragment,
            canSplit = useRockImage
        && Random.value < c.Owner.b1SplitChance,

            splitTimer = Mathf.Max(0.05f, c.Owner.b1SplitDelay),

            bounceStart = position
        };

        if (!isFragment)
            BeginBounce(c, debris);

        debrisList.Add(debris);
    }

    private void SplitDebris(
        BattleContext c,
        BattlePatternData data,
        BattleStepScope scope,
        List<Debris> debrisList,
        Debris parent)
    {
        // 정수 Random.Range의 끝값은 포함하지 않습니다.
        int fragmentCount = Random.Range(3, 6);

        Vector2 position = parent.visual.position;

        // 아래쪽을 기준으로 좌우 65도 범위에 나눠 발사합니다.
        const float spreadAngle = 65f;
        float sectorWidth = spreadAngle * 2f / fragmentCount;

        for (int i = 0; i < fragmentCount; i++)
        {
            // 각 파편에 서로 다른 각도 구간을 배정합니다.
            float angle =
                -spreadAngle +
                sectorWidth * (i + 0.5f);

            angle += Random.Range(
                -sectorWidth * 0.2f,
                sectorWidth * 0.2f);

            float radians = angle * Mathf.Deg2Rad;

            Vector2 direction = new Vector2(
                Mathf.Sin(radians),
                -Mathf.Cos(radians));

            float speed =
                Mathf.Max(0.1f, data.speed) *
                Random.Range(0.8f, 1.3f);

            float fragmentSize =
                parent.size * Random.Range(0.65f, 0.85f);

            SpawnDebris(
                c,
                scope,
                debrisList,
                position,
                fragmentSize,
                direction * speed,
                true); // 크기와 무관하게 Fragment 이미지 사용
        }
    }

    // 새로운 곡선 한 구간을 시작.
    private void BeginBounce(BattleContext c, Debris debris)
    {
        debris.bounceElapsed = 0f;

        // 매번 독립적으로 좌우 50% 추첨.
        debris.bounceSide = Random.value < 0.5f ? -1f : 1f;

        debris.bounceDuration = RandomRange(
            c.Owner.b1RockBounceDurationRange, 0.1f);

        debris.bounceSlowdown = Mathf.Clamp(
            c.Owner.b1RockBounceSlowdown, 0f, 0.95f);

        float desiredWidth = RandomRange(
            c.Owner.b1RockBounceWidthRange, 0f);

        debris.bounceWidth = desiredWidth;
    }

    private void MoveDebris(BattleContext c, Debris debris, float dt)
    {
        // 분열 파편은 기존의 아래쪽 부채꼴 이동 유지.
        if (debris.isFragment || debris.wallReflected)
        {
            debris.visual.position += (Vector3)(debris.velocity * dt);
            return;
        }

        debris.bounceElapsed += dt;

        // 한 프레임이 구간 끝을 넘어도 남은 시간을 보존.
        while (debris.bounceElapsed >= debris.bounceDuration)
        {
            float remaining =
                debris.bounceElapsed - debris.bounceDuration;

            // 다음 파란 점의 위치.
            debris.bounceStart +=
                debris.velocity * debris.bounceDuration;

            BeginBounce(c, debris);
            debris.bounceElapsed = remaining;
        }

        float t = debris.bounceElapsed / debris.bounceDuration;

        // 진행 속도:
        // 처음 빠름 → 중간 느림 → 끝에서 다시 빠름.
        // p는 항상 0에서 1로 증가하므로 뒤로 올라가지 않음.
        float p = t + debris.bounceSlowdown
            * Mathf.Sin(2f * Mathf.PI * t)
            / (2f * Mathf.PI);

        // 좌우로 볼록하게 휘었다가 다음 파란 점에서 원위치.
        float curve = 4f * p * (1f - p);

        Vector2 position =
            debris.bounceStart
            + debris.velocity * (debris.bounceDuration * p);

        position.x +=
            debris.bounceSide * debris.bounceWidth * curve;

        debris.visual.position =
            new Vector3(position.x, position.y, 0f);
    }

    private static float RandomRange(Vector2 range, float minimum)
    {
        float min = Mathf.Max(minimum, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));

        return Random.Range(min, max);
    }

    private bool MoveAndCheckHit(
    BattleContext c, Debris debris, float dt)
    {
        Rect bounds = c.Arena.Bounds;

        // 기존의 원형 충돌 반지름을 벽 판정에도 사용.
        float left = bounds.xMin + debris.hitRadius;
        float right = bounds.xMax - debris.hitRadius;

        if (left >= right)
            return false;

        // 곡선을 짧은 선분으로 나눠 이동 방향을 근사.
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
        float stepDt = dt / steps;

        for (int i = 0; i < steps; i++)
        {
            Vector2 start = debris.visual.position;

            MoveDebris(c, debris, stepDt);

            Vector2 target = debris.visual.position;
            Vector2 incomingVelocity = (target - start) / stepDt;

            // 한 이동 구간에서 양쪽 벽을 연달아 만나는 경우도 처리.
            while (true)
            {
                Vector2 delta = target - start;

                bool hitLeft =
                    delta.x < 0f && target.x <= left;

                bool hitRight =
                    delta.x > 0f && target.x >= right;

                if (!hitLeft && !hitRight)
                {
                    debris.visual.position =
                        new Vector3(target.x, target.y, 0f);

                    if (HitsPlayer(c, debris, start, target))
                        return true;

                    break;
                }

                float wallX = hitLeft ? left : right;

                float fraction = Mathf.Clamp01(
                    (wallX - start.x) / delta.x);

                Vector2 contact = start + delta * fraction;
                contact.x = wallX;

                
                if (HitsPlayer(c, debris, start, contact))
                {
                    debris.visual.position =
                        new Vector3(contact.x, contact.y, 0f);

                    return true;
                }

                // 수직 벽 반사: X만 반전, Y와 속력은 유지.
                incomingVelocity.x = -incomingVelocity.x;

                debris.velocity = incomingVelocity;
                debris.wallReflected = true;

                // 벽에 도착하고 남은 이동 거리도 반사.
                Vector2 remaining = target - contact;
                remaining.x = -remaining.x;

                start = contact;
                target = contact + remaining;
            }
        }

        return false;
    }

    private bool HitsPlayer(
        BattleContext c,
        Debris debris,
        Vector2 from,
        Vector2 to)
    {
        return SlashResolver.DistanceToSegment(
            c.Player.transform.position,
            from,
            to)
            <= c.Player.HitRadius + debris.hitRadius;
    }
}