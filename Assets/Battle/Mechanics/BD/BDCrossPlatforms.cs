using System.Collections.Generic;
using UnityEngine;

public sealed class BDCrossPlatforms : MonoBehaviour
{
    private sealed class Platform
    {
        public Rigidbody2D body;
        public SpriteRenderer visual;
        public BoxCollider2D collider;
        public float speed;
        public float x;
        public float y;
    }

    private readonly List<Platform> platforms =
        new List<Platform>();

    private BattleContext context;
    private Rect box;
    private float halfWidth;
    private float period;
    private bool initialized;

    public void Initialize(
        BattleContext battle,
        BattlePatternData data,
        Rect bounds,
        Sprite square)
    {
        context = battle;
        box = bounds;

        float width = Mathf.Max(0.2f, data.bdCrossPlatformSize.x);
        float thickness = Mathf.Max(0.05f, data.bdCrossPlatformSize.y);
        float spacing = Mathf.Max(
            width + 0.2f, data.bdCrossPlatformSpacing);

        float speed = Mathf.Max(0.1f, data.bdCrossPlatformSpeed);

        halfWidth = width * 0.5f;

        // 상자 안의 발판과 순환에 필요한 여분 발판을 함께 준비한다.
        int count = Mathf.CeilToInt(
            (box.width + width) / spacing) + 1;

        period = count * spacing;

        for (int row = 0; row < 2; row++)
        {
            float height = row == 0
                ? data.bdCrossLowerHeight
                : data.bdCrossUpperHeight;

            float rowSpeed = row == 0 ? speed : -speed;

            for (int i = 0; i < count; i++)
            {
                // 두 줄의 발판을 조금 어긋나게 배치한다.
                float x = box.xMin - halfWidth
                    + i * spacing
                    + (row == 0 ? 0f : spacing * 0.5f);

                float y = box.yMin + height - thickness * 0.5f;

                var go = new GameObject(
                    row == 0 ? "하단 이동 발판" : "상단 이동 발판");

                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(x, y, 0f);
                go.transform.localScale =
                    new Vector3(width, thickness, 1f);

                var visual = go.AddComponent<SpriteRenderer>();
                visual.sprite = square;
                visual.color = Color.white;
                visual.sortingOrder = 8;

                var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;

                var collider = go.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one;
                collider.usedByEffector = true;

                var effector = go.AddComponent<PlatformEffector2D>();
                effector.useOneWay = true;
                effector.useOneWayGrouping = true;
                effector.surfaceArc = 160f;
                effector.useSideFriction = false;
                effector.useSideBounce = false;

                var platform = new Platform
                {
                    body = body,
                    visual = visual,
                    collider = collider,
                    speed = rowSpeed,
                    x = x,
                    y = y
                };

                platforms.Add(platform);
                SetVisible(platform);
            }
        }

        initialized = true;
    }

    private void FixedUpdate()
    {
        if (!initialized)
            return;

        if (context.Clock.Paused ||
            context.Owner.Result != BattleResult.Running)
        {
            foreach (var platform in platforms)
                platform.body.linearVelocity = Vector2.zero;

            return;
        }

        foreach (var platform in platforms)
        {
            platform.x += platform.speed * Time.fixedDeltaTime;

            bool wrapped = false;

            if (platform.speed > 0f &&
                platform.x - halfWidth > box.xMax)
            {
                platform.x -= period;
                wrapped = true;
            }
            else if (platform.speed < 0f &&
                     platform.x + halfWidth < box.xMin)
            {
                platform.x += period;
                wrapped = true;
            }

            Vector2 next = new Vector2(platform.x, platform.y);

            if (wrapped)
            {
                // 상자를 가로질러 물리 이동하지 않고 바깥에서 재배치한다.
                platform.body.linearVelocity = Vector2.zero;
                platform.body.position = next;
            }
            else
            {
                platform.body.MovePosition(next);
            }

            SetVisible(platform);
        }
    }

    private void SetVisible(Platform platform)
    {
        bool overlaps =
            platform.x + halfWidth > box.xMin &&
            platform.x - halfWidth < box.xMax;

        platform.visual.enabled = overlaps;
        platform.collider.enabled = overlaps;
    }
}