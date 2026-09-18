using System;
using System.Collections;
using UnityEngine;

public static class BattleSteps
{
    public static IBattleStep Create(BattleStepKind kind)
    {
        switch (kind)
        {
            case BattleStepKind.Dialogue: return new DialogueStep();
            case BattleStepKind.Corridor: return new BoxTransitionStep();
            case BattleStepKind.Mist: return new MistStep();
            case BattleStepKind.EnemyEntrance: return new EnemyEntranceStep();
            case BattleStepKind.BuildBox: return new BuildBoxStep();
            case BattleStepKind.Pattern: return new PatternSequenceStep();
            case BattleStepKind.Slash: return new SlashSequenceStep();
            case BattleStepKind.ScreenBreak: return new ScreenBreakStep();
            case BattleStepKind.Exit: return new ExitStep();
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}

public sealed class DialogueStep : IBattleStep
{
    public IEnumerator Run(
        BattleContext c,
        BattleStepDefinition d,
        BattleStepScope scope)
    {
        // 한 글자가 나오는 간격.
        const float typingInterval = 0.1f;

        c.Player.Lock(true);
        c.Player.SetVisible(false);
        c.Box.SetWalls(false, false);

        int previousVisibleCharacters =
            c.Message.maxVisibleCharacters;

        // 도중에 전투가 종료돼도 표시 상태 복원.
        scope.OnDispose(() =>
        {
            c.Message.maxVisibleCharacters =
                previousVisibleCharacters;

            c.HideMessage();
        });

        c.Message.maxVisibleCharacters = 0;
        c.ShowMessage(d.text, true);
        c.Message.ForceMeshUpdate();

        int totalCharacters = c.Message.textInfo.characterCount;

        bool fullyShown = totalCharacters == 0;
        int visibleCharacters = 0;

        // 글자가 없는 사각형 대사창부터 화면에 표시.
        yield return null;

        // B1.asset의 이 대사 단계 Duration만큼 기다린 후 시작.
        // 현재 0.5라면 빈 대사창을 먼저 0.5초 보여줌.
        yield return new WaitForSecondsRealtime(
            Mathf.Max(0f, d.duration));

        float nextCharacterTime =
            Time.realtimeSinceStartup + typingInterval;

        while (true)
        {
            bool pressed =
                Input.GetMouseButtonDown(0) ||
                Input.GetKeyDown(KeyCode.Space) ||
                Input.GetKeyDown(KeyCode.Z);

            if (pressed)
            {
                if (!fullyShown)
                {
                    // 출력 중 입력: 전체 글자 표시.
                    c.Message.maxVisibleCharacters = totalCharacters;
                    fullyShown = true;
                }
                else
                {
                    // 다음 입력: 대사 종료.
                    yield return null;
                    break;
                }
            }

            if (!fullyShown && Time.realtimeSinceStartup >= nextCharacterTime)
            {
                // 프레임이 늦어져도 한 번에 한 글자만 추가.
                visibleCharacters++;

                c.Message.maxVisibleCharacters = visibleCharacters;
                fullyShown = visibleCharacters >= totalCharacters;

                // 밀린 시간을 따라잡지 않고 지금부터 다시 간격을 셈.
                nextCharacterTime =
                    Time.realtimeSinceStartup + typingInterval;
            }

            yield return null;
        }

        c.Message.maxVisibleCharacters = previousVisibleCharacters;
        c.HideMessage();

        c.Box.ChangeBox(
            c.Box.dialogueSize,
            c.Box.dialoguePos,
            0f);

        c.Box.HideUI();
    }
}
public sealed class BoxTransitionStep : IBattleStep
{
    public IEnumerator Run(BattleContext c, BattleStepDefinition d, BattleStepScope scope)
    {
        c.Arena.Mode = BattleArena.Boundary.Corridor;
        yield return c.Box.OpenCorridor(c.Arena.Bounds, Mathf.Max(.1f, d.duration));
    }
}
public sealed class MistStep : IBattleStep
{
    public IEnumerator Run(BattleContext c, BattleStepDefinition d, BattleStepScope scope)
    {
        for (float t = 0; t < d.duration; t += c.Clock.Delta)
        { c.LayoutMist(Mathf.Clamp01(t / d.duration)); yield return null; }
        c.LayoutMist(1);
        Rect r = c.Arena.ScreenRect;
        c.Player.transform.position = new Vector3(r.center.x, Mathf.Lerp(r.yMin, r.yMax, .18f), 0);
        c.Player.SetVisible(true);
    }
}
public sealed class EnemyEntranceStep : IBattleStep
{
    public IEnumerator Run(BattleContext c, BattleStepDefinition d, BattleStepScope scope)
    {
        c.Arena.Mode = BattleArena.Boundary.Screen;
        c.Player.transform.position = new Vector3(0, -3, 0);
        c.Player.SetVisible(true); c.Player.Lock(true);
        Rect r = c.Arena.ScreenRect;
        var enemies = new Transform[16];
        var destinations = new Vector3[16];
        for (int i = 0; i < enemies.Length; i++)
        {
            destinations[i] = new Vector3((i % 6 - 2.5f) * 1.8f, r.yMax - 1.8f - (i / 6) * 1.8f, 0);
            enemies[i] = c.Primitive("Enemy " + (i + 1), PrimitiveType.Cube, c.Enemies,
                destinations[i] + Vector3.up * 6, new Vector3(.65f, .95f, .2f)).transform;
        }
        float duration = Mathf.Max(.1f, d.duration);
        for (float t = 0; t < duration; t += c.Clock.Delta)
        {
            float p = Mathf.SmoothStep(0, 1, t / duration);
            for (int i = 0; i < enemies.Length; i++)
            {
                enemies[i].position = destinations[i] + Vector3.up * Mathf.Lerp(6, 0, p);
                enemies[i].localScale = new Vector3(.65f, .95f, .2f) * Mathf.Lerp(.25f, 1, p);
            }
            yield return null;
        }
        Vector3[] surrounds = { new Vector3(0, 1, 0), new Vector3(-4, -3, 0), new Vector3(0, -7, 0), new Vector3(4, -3, 0) };
        for (float t = 0; t < 1; t += c.Clock.Delta)
        {
            for (int i = 0; i < 4; i++) enemies[i].position = Vector3.Lerp(destinations[i], surrounds[i], Mathf.SmoothStep(0, 1, t));
            yield return null;
        }
        for (int i = 0; i < 4; i++) enemies[i].position = surrounds[i];
    }
}
public sealed class BuildBoxStep : IBattleStep
{
    public IEnumerator Run(
        BattleContext c,
        BattleStepDefinition d,
        BattleStepScope scope)
    {
        c.Player.Lock(true);

        c.Box.CancelAnimations();
        c.Box.HideUI();

        // 기존 벽은 숨기고 충돌도 끔.
        c.Box.SetWalls(false, false);

        // EnemyEntranceStep에서 만든 첫 네 적.
        // 순서: top, left, bottom, right
        var enemies = new Transform[4];

        for (int i = 0; i < 4; i++)
        {
            enemies[i] = c.Enemies.Find("Enemy " + (i + 1));

            if (enemies[i] == null)
                throw new InvalidOperationException(
                    "BattleBox로 변형할 Enemy " + (i + 1) + "이 없습니다.");
        }

        Vector2 center = new Vector2(0f, -3f);

        // 마주 보는 벽 중심 사이의 거리.
        float sideLength = 8f;
        float half = sideLength * 0.5f;
        float thickness = Mathf.Max(0.01f, c.Box.wallThickness);

        c.Box.transform.position =
            new Vector3(center.x, center.y, 0f);

        Vector3[] targetPositions =
        {
            new Vector3(0f, half, 0f),   // top
            new Vector3(-half, 0f, 0f),  // left
            new Vector3(0f, -half, 0f),  // bottom
            new Vector3(half, 0f, 0f)    // right
        };

        // 모서리에 틈이 생기지 않도록 두께만큼 길이 추가.
        float wallLength = sideLength + thickness;

        Vector3[] targetScales =
        {
            new Vector3(wallLength, thickness, 1f),
            new Vector3(thickness, wallLength, 1f),
            new Vector3(wallLength, thickness, 1f),
            new Vector3(thickness, wallLength, 1f)
        };

        var startPositions = new Vector3[4];
        var startScales = new Vector3[4];

        // 적을 BattleBox 아래로 이동시키되 현재 모습은 유지.
        for (int i = 0; i < 4; i++)
        {
            Transform enemy = enemies[i];

            enemy.SetParent(c.Box.transform, true);
            enemy.localRotation = Quaternion.identity;

            startPositions[i] = enemy.localPosition;
            startScales[i] = enemy.localScale;

            // 기존 벽과 같은 태그·레이어 사용.
            Transform oldWall = c.Box.Walls[i];
            enemy.gameObject.tag = oldWall.gameObject.tag;
            enemy.gameObject.layer = oldWall.gameObject.layer;

            // 적은 3D Cube이지만 플레이어의 충돌은 2D 사용.
            var collider = enemy.GetComponent<BoxCollider2D>();

            if (collider == null)
                collider = enemy.gameObject.AddComponent<BoxCollider2D>();

            collider.offset = Vector2.zero;
            collider.size = Vector2.one;
            collider.isTrigger = false;
            collider.enabled = false;
        }

        float duration = Mathf.Max(0.1f, d.duration);

        // top → left → bottom → right 순으로 변형.
        for (int i = 0; i < 4; i++)
        {
            for (float elapsed = 0f;
                 elapsed < duration;
                 elapsed += c.Clock.Delta)
            {
                float t = Mathf.SmoothStep(
                    0f, 1f, elapsed / duration);

                enemies[i].localPosition = Vector3.Lerp(
                    startPositions[i], targetPositions[i], t);

                enemies[i].localScale = Vector3.Lerp(
                    startScales[i], targetScales[i], t);

                yield return null;
            }

            // 마지막 프레임에서 정확한 위치와 크기로 고정.
            enemies[i].localPosition = targetPositions[i];
            enemies[i].localScale = targetScales[i];
        }

        // 변형된 적 자체를 실제 BattleBox 벽으로 등록.
        c.Box.topWall = enemies[0];
        c.Box.leftWall = enemies[1];
        c.Box.bottomWall = enemies[2];
        c.Box.rightWall = enemies[3];

        c.Box.SetWalls(true, true);

        c.Arena.Mode = BattleArena.Boundary.Box;
        c.Player.SetMovementMode(PlayerController.MovementMode.Free);
        c.Player.Lock(false);
    }
}
public sealed class PatternSequenceStep : IBattleStep
{
    public IEnumerator Run(
        BattleContext c,
        BattleStepDefinition d,
        BattleStepScope scope)
    {
        IBattlePattern pattern;

        switch (d.pattern.kind)
        {
            case BattlePatternKind.LegacyCase:
                pattern = new LegacyCasePattern();
                break;

            case BattlePatternKind.Debris:
                pattern = new B1DebrisPattern();
                break;

            case BattlePatternKind.B2:
                pattern = B2PatternFactory.Create(
                    d.pattern.b2PatternId);
                break;

            case BattlePatternKind.Radial:
            case BattlePatternKind.Rain:
            case BattlePatternKind.Aimed:
                pattern = new B3BulletPattern();
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        yield return pattern.Run(c, d.pattern, scope);
    }
}
public sealed class FlashTransitionStep
{
    public IEnumerator Run(BattleContext c)
    {
        for (int i = 0; i < 2; i++)
        {
            c.Overlay.color = Color.white;
            yield return new WaitForSecondsRealtime(.065f);
            c.Overlay.color = Color.black;
            yield return new WaitForSecondsRealtime(.065f);
        }
    }
}
public sealed class SlashSequenceStep : IBattleStep
{
    public IEnumerator Run(BattleContext c, BattleStepDefinition d, BattleStepScope scope)
    {
        var boxState = c.Box.Capture();
        Vector3 playerPosition = c.Player.transform.position;
        var movement = c.Player.currentMode;
        var boundary = c.Arena.Mode;
        bool orthographic = c.Camera.orthographic;
        float fov = c.Camera.fieldOfView;
        bool enemiesVisible = c.Enemies.gameObject.activeSelf;
        Action restore = () =>
        {
            c.Camera.orthographic = orthographic; c.Camera.fieldOfView = fov;
            c.Box.Restore(boxState);
            c.Enemies.gameObject.SetActive(enemiesVisible);
            c.Player.transform.position = playerPosition;
            c.Player.SetMovementMode(movement);
            c.Player.Lock(true);
            c.Arena.Mode = boundary;
            c.DrawSlash(Vector2.zero, Vector2.zero, false);
            c.Overlay.color = Color.clear;
        };
        scope.OnDispose(restore);
        c.Player.Lock(true);
        yield return new FlashTransitionStep().Run(c);
        c.Box.SetWalls(false, false);
        c.Enemies.gameObject.SetActive(false);
        c.Camera.orthographic = false; c.Camera.fieldOfView = 60;
        c.Arena.Mode = BattleArena.Boundary.None;
        c.Player.transform.position = new Vector3(0, -4.5f, 0);
        c.Player.SetVisible(true);
        c.Overlay.color = Color.clear;
        yield return new SlashTargetSpawner().Run(c, scope);
        yield return new FlashTransitionStep().Run(c);
        restore();
    }
}
public sealed class ScreenBreakStep : IBattleStep
{
    public IEnumerator Run(BattleContext c, BattleStepDefinition d, BattleStepScope scope)
    { yield return new ScreenBreakController().Run(c, scope, d.duration); }
}
public sealed class ExitStep : IBattleStep
{
    public IEnumerator Run(BattleContext c, BattleStepDefinition d, BattleStepScope scope)
    {
        c.Player.Lock(true);
        if (c.Stage.stageIndex != 1) yield break;
        c.LayoutMist(1f);
        yield return c.Clock.Wait(Mathf.Max(0f, d.duration));
        c.Arena.Mode = BattleArena.Boundary.None;
        float end = c.Arena.ScreenRect.yMax + c.Player.HalfSize.y + 1;
        while (c.Player.transform.position.y < end)
        { c.Player.transform.position += Vector3.up * (7f * c.Clock.Delta); yield return null; }
        c.Player.SetVisible(false);
    }
}
