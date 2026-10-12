using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

public sealed class B2HammerMechanic
{
    // ───────── 직접 조절하는 설정 ─────────

    private const int HitCount = 6;

    // 방어 성공 시 밀리는 거리 배율.
    // 0 = 이동 없음, 0.2 = 원래 거리의 20%, 1 = 방어 효과 없음.
    private const float GuardPushMultiplier = 0.1f;

    // 타격 직전 이 시간 안에 클릭하면 방어 가능.
    private const float GuardWindow = 1.0f;

    // 왼쪽 벽에 붙었다고 인정하는 추가 여유.
    private const float WallTolerance = 0.12f;

    private const float WaitSeconds = 0.8f;
    private const float WindupSeconds = 0.6f;
    private const float StrikeSeconds = 0.25f;
    private const float PushSeconds = 1.4f;
    private const float FollowSeconds = 0.7f;

    private const float WindupAngle = 55f;

    private const float HammerHeadWidth = 2.6f;
    private const float HammerHeadHeight = 1.1f;
    private const float HammerHandleWidth = 0.45f;

    // 망치 회전 중심과 머리 중심 사이의 세로 거리.
    private const float HammerLength = 4f;

    private const float LeftSwordLength = 4f;
    private const float LeftSwordThickness = 0.12f;
    private const float LeftSwordSpacing = 0.38f;

    // ───────── 실행 중 상태 ─────────

    private enum Phase { Wait, Windup, Strike, Push, Follow, Done }

    private readonly BattleContext c;
    private readonly Transform pivot;

    private readonly Vector3 originalBoxPosition;
    private readonly float failurePushDistance;
    private readonly float minimumBoxX;

    private readonly List<Vector2> leftSwordCenters =
        new List<Vector2>();

    private readonly Vector2 leftSwordHalf =
        new Vector2(LeftSwordLength, LeftSwordThickness) * 0.5f;

    private Phase phase;
    private float phaseTime;
    private float clock;
    private float lastGuardClick = float.NegativeInfinity;
    private int hits;

    private Vector3 pushStart;
    private Vector3 pushTarget;
    private Vector3 followStart;
    private Vector3 followTarget;

    public bool Finished => phase == Phase.Done;

    public bool ReachedLeftSwords
    {
        get
        {
            float halfThickness = c.Box.wallThickness * 0.5f;

            // 왼쪽으로 이동하는 오른쪽 벽의 앞쪽 면.
            float wallLeft =
                c.Box.rightWall.position.x - halfThickness;

            float wallBottom =
                c.Box.bottomWall.position.y - halfThickness;

            float wallTop =
                c.Box.topWall.position.y + halfThickness;

            foreach (Vector2 center in leftSwordCenters)
            {
                float swordTip = center.x + leftSwordHalf.x;
                float swordBottom = center.y - leftSwordHalf.y;
                float swordTop = center.y + leftSwordHalf.y;

                bool overlapsY =
                    wallBottom <= swordTop &&
                    wallTop >= swordBottom;

                // 검 끝에 닿거나, 한 프레임 사이에 지나쳐도 인정.
                if (overlapsY && wallLeft <= swordTip)
                    return true;
            }

            return false;
        }
    }

    public B2HammerMechanic(
        BattleContext context,
        Sprite square,
        Transform parent)
    {
        c = context;
        c.Box.CancelAnimations();

        originalBoxPosition = c.Box.transform.position;

        Rect screen = c.Arena.ScreenRect;

        
        float rightOuter =
            c.Box.rightWall.position.x + c.Box.wallThickness * 0.5f;

        float totalDistance =
            Mathf.Max(0f, rightOuter - screen.xMin);

        // 방어 실패 두 번으로 끝 지점 도달.
        failurePushDistance = totalDistance * 0.5f;
        minimumBoxX = originalBoxPosition.x - totalDistance;

        pivot = new GameObject("Hammer Pivot").transform;
        pivot.SetParent(parent, false);
        pivot.position = HammerPosition();

        Color blue = new Color(0.3f, 0.5f, 1f);

        // 회전 중심에서 아래쪽으로 내려오는 손잡이.
        CreateSquare(
            "Hammer Handle", square, pivot,
            new Vector2(0f, -HammerLength * 0.5f),
            new Vector2(HammerHandleWidth, HammerLength),
            blue, 5
        );

        // 손잡이 아래쪽의 가로 망치 머리.
        CreateSquare(
            "Hammer Head", square, pivot,
            new Vector2(0f, -HammerLength),
            new Vector2(HammerHeadWidth, HammerHeadHeight),
            blue, 5
        );

        // 화면 왼쪽에서 오른쪽으로 길이 4만큼 나온 고정 검.
        for (float y = screen.yMin + LeftSwordSpacing * 0.5f;
            y <= screen.yMax - LeftSwordThickness * 0.5f;
            y += LeftSwordSpacing)
        {
            Vector2 center = new Vector2(
                screen.xMin + LeftSwordLength * 0.5f, y
            );

            Transform sword = CreateSquare(
                "Left Fixed Sword", square, parent,
                Vector2.zero,
                new Vector2(LeftSwordLength, LeftSwordThickness),
                Color.white, 15
            );

            sword.position = center;
            leftSwordCenters.Add(center);
        }
    }

    // Pattern06에서 매 프레임 한 번 호출.
    public void Tick(float dt)
    {
        if (Finished || dt <= 0f)
            return;

        clock += dt;
        phaseTime += dt;

        bool pointerOverUI =
            EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject();

        if (!pointerOverUI &&
            Input.GetMouseButtonDown(0) &&
            TouchingRightWall())
        {
            lastGuardClick = clock;
        }

        switch (phase)
        {
            case Phase.Wait:
                if (phaseTime >= WaitSeconds)
                    Enter(Phase.Windup);
                break;

            case Phase.Windup:
                SetAngle(Mathf.Lerp(
                    0f, WindupAngle,
                    Progress(WindupSeconds)
                ));

                if (phaseTime >= WindupSeconds)
                    Enter(Phase.Strike);
                break;

            case Phase.Strike:
                SetAngle(Mathf.Lerp(
                    WindupAngle, 0f,
                    Progress(StrikeSeconds)
                ));

                if (phaseTime >= StrikeSeconds)
                {
                    SetAngle(0f);

                    bool guarded =
                        TouchingRightWall() &&
                        clock - lastGuardClick <= GuardWindow;

                    // 한 번의 클릭을 다음 공격에 재사용하지 않음.
                    lastGuardClick = float.NegativeInfinity;

                    float multiplier = guarded
                        ? Mathf.Clamp01(GuardPushMultiplier)
                        : 1f;

                    pushStart = c.Box.transform.position;
                    pushTarget = pushStart;

                    pushTarget.x = Mathf.Max(
                        minimumBoxX,
                        pushStart.x - failurePushDistance * multiplier
                    );

                    hits++;
                    Enter(Phase.Push);
                }
                break;

            case Phase.Push:
                // 이 동안 망치는 제자리에 있고 상자만 이동.
                MoveBox(Vector3.Lerp(
                    pushStart,
                    pushTarget,
                    Progress(PushSeconds)
                ));

                if (phaseTime >= PushSeconds)
                {
                    MoveBox(pushTarget);

                    followStart = pivot.position;
                    followTarget = HammerPosition();
                    Enter(Phase.Follow);
                }
                break;

            case Phase.Follow:
                // 상자 이동이 끝난 뒤 망치가 뒤따라옴.
                pivot.position = Vector3.Lerp(
                    followStart,
                    followTarget,
                    Progress(FollowSeconds)
                );

                if (phaseTime >= FollowSeconds)
                {
                    pivot.position = followTarget;
                    Enter(hits >= HitCount ? Phase.Done : Phase.Wait);
                }
                break;
        }
    }

    public bool HitsLeftSwords(Vector2 player, float radius)
    {
        foreach (Vector2 center in leftSwordCenters)
        {
            Vector2 nearest = new Vector2(
                Mathf.Clamp(
                    player.x,
                    center.x - leftSwordHalf.x,
                    center.x + leftSwordHalf.x),
                Mathf.Clamp(
                    player.y,
                    center.y - leftSwordHalf.y,
                    center.y + leftSwordHalf.y)
            );

            if ((player - nearest).sqrMagnitude <= radius * radius)
                return true;
        }

        return false;
    }

    private bool TouchingRightWall()
    {
        Rect box = c.Arena.Bounds;
        Vector2 player = c.Player.transform.position;

        float rightLimit = box.xMax -
            Mathf.Min(c.Player.HalfSize.x, box.width * 0.5f);

        return Mathf.Abs(player.x - rightLimit) <= WallTolerance;
    }

    private Vector3 HammerPosition()
    {
        Rect box = c.Arena.Bounds;

        float rightOuter =
            c.Box.rightWall.position.x + c.Box.wallThickness * 0.5f;

        // 각도 0일 때 머리의 왼쪽 끝이 오른쪽 벽에 닿음.
        return new Vector3(
            rightOuter + HammerHeadWidth * 0.5f,
            box.center.y + HammerLength,
            0f
        );
    }

    private void MoveBox(Vector3 target)
    {
        Vector3 delta = target - c.Box.transform.position;
        c.Box.transform.position = target;

        // 상자에 타고 있는 것처럼 플레이어도 같은 양만큼 이동.
        // Rigidbody2D와 Transform 위치를 함께 맞춘다.
        if (c.Player != null)
        {
            Vector3 position = c.Player.transform.position + delta;
            c.Player.transform.position = position;

            if (c.Player.Body != null)
                c.Player.Body.position = new Vector2(position.x, position.y);
        }
    }

    private float Progress(float duration)
    {
        return Mathf.SmoothStep(
            0f, 1f,
            Mathf.Clamp01(phaseTime / Mathf.Max(0.01f, duration))
        );
    }

    private void Enter(Phase next)
    {
        phase = next;
        phaseTime = 0f;
    }

    private void SetAngle(float angle)
    {
        pivot.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private static Transform CreateSquare(
        string name,
        Sprite sprite,
        Transform parent,
        Vector2 localPosition,
        Vector2 size,
        Color color,
        int order)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);

        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return go.transform;
    }
}