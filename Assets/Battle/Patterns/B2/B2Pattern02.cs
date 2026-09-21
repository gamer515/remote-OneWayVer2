using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class B2Pattern02 : IBattlePattern
{
    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        c.Arena.Mode = BattleArena.Boundary.Box;
        c.Player.SetMovementMode(PlayerController.MovementMode.Free);
        c.Player.SetVisible(true);
        c.Player.Lock(true);
        Color previousOverlay = c.Overlay.color;
        scope.OnDispose(() =>
        {
            // Do not erase the death message overlay during failure cleanup.
            if (c.Owner.Result == BattleResult.Running)
                c.Overlay.color = previousOverlay;
            c.Player.Lock(true);
        });

        
        if (c.Owner.Result != BattleResult.Running) yield break;

        Sprite sprite = data.swordSprite;
        if (sprite == null)
        {
            // Runtime equivalent of a 2D Square; no texture or sprite asset is saved.
            Texture2D white = Texture2D.whiteTexture;
            sprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height),
                new Vector2(0.5f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
            Sprite ownedSprite = sprite;
            scope.OnDispose(() => Object.Destroy(ownedSprite));
        }

        Vector2 size = new Vector2(Mathf.Max(0.02f, data.swordSize.x),
            Mathf.Max(0.02f, data.swordSize.y));
        Vector2 half = size * 0.5f;
        float radius = half.magnitude;
        Vector2 center = c.Arena.Bounds.center;
        Rect screen = c.Arena.ScreenRect;
        float top = Mathf.Max(screen.yMax, center.y) + radius + 1f;
        float bottom = Mathf.Min(screen.yMin, center.y) - radius - 1f;
        float width = Mathf.Max(0.1f, data.swordPathWidth);
        float duration = Mathf.Max(0.1f, data.swordFlightSeconds);
        float spin = data.swordRotationSpeed;
        var swords = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            swords[i] = CreateSword(sprite, size, scope.Root, i);
            swords[i].position = Path(center, top, bottom, width, i == 0 ? -1f : 1f, 0f);
        }
        c.Player.Lock(false);

        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;
        // Bound translation and tip rotation, sampling at <= 0.04 world units.
        // The matching 0.02 contact margin covers half the sampling gap.
        float pathSpeedBound = new Vector2(width * (2f + 1.3f * Mathf.PI),
            2f * Mathf.Max(top - center.y, center.y - bottom)).magnitude / duration;
        float tipSpeedBound = Mathf.Abs(spin) * Mathf.Deg2Rad * radius;
        while (elapsed < duration && c.Owner.Result == BattleResult.Running)
        {
            float dt = Mathf.Min(c.Clock.Delta, duration - elapsed);
            if (dt <= 0f) { yield return null; continue; }
            Vector2 currentPlayer = c.Player.transform.position;
            float relativeTravel = (pathSpeedBound + tipSpeedBound) * dt
                + Vector2.Distance(previousPlayer, currentPlayer);
            int samples = Mathf.Max(1, Mathf.CeilToInt(relativeTravel / 0.04f));
            bool contacted = false;
            for (int sample = 1; sample <= samples; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;
                float p = Mathf.Clamp01(time / duration);
                Vector2 playerPosition = Vector2.Lerp(previousPlayer, currentPlayer, blend);
                for (int i = 0; i < 2; i++)
                {
                    float side = i == 0 ? -1f : 1f;
                    Vector3 position = Path(center, top, bottom, width, side, p);
                    float angle = side * spin * time;
                    swords[i].SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
                    if (!contacted && TouchesSword(playerPosition, c.Player.HitRadius + 0.02f,
                        position, angle, half))
                    {
                        contacted = true;
                        c.Player.TakeDamage(1f);
                        if (c.Owner.Result != BattleResult.Running) yield break;
                    }
                }
            }
            elapsed += dt;
            previousPlayer = currentPlayer;
            yield return null;
        }

        // Both swords are entirely below the viewport before the final flash.
        foreach (Transform sword in swords)
        {
            sword.gameObject.SetActive(false);
            Object.Destroy(sword.gameObject);
        }
        c.Player.Lock(true);
        
    }

    private static Transform CreateSword(Sprite sprite, Vector2 size, Transform parent, int index)
    {
        var root = new GameObject("B2 Rotating Sword " + (index + 1)).transform;
        root.SetParent(parent, false);
        var visual = new GameObject("Square", typeof(SpriteRenderer));
        visual.transform.SetParent(root, false);
        Vector3 boundsSize = sprite.bounds.size;
        Vector3 scale = new Vector3(size.x / Mathf.Max(0.001f, boundsSize.x),
            size.y / Mathf.Max(0.001f, boundsSize.y), 1f);
        visual.transform.localScale = scale;
        visual.transform.localPosition = -Vector3.Scale(sprite.bounds.center, scale);
        var renderer = visual.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.sortingOrder = 15;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        // Manual rotated-rectangle collision below avoids physical wall blocking.
        return root;
    }

    private static Vector3 Path(Vector2 center, float top, float bottom,
        float width, float side, float p)
    {
        // Exact center at p=0.5; mirrored curves switch sides after crossing.
        float x = side * width * (1f - 2f * p + 0.65f * Mathf.Sin(2f * Mathf.PI * p));
        float y = p <= 0.5f ? Mathf.Lerp(top, center.y, p * 2f)
            : Mathf.Lerp(center.y, bottom, (p - 0.5f) * 2f);
        return new Vector3(center.x + x, y, 0f);
    }

    private static bool TouchesSword(Vector2 player, float playerRadius,
        Vector2 center, float angle, Vector2 half)
    {
        // Bring the player into the rotating sword's local coordinates.
        Vector2 delta = player - center;
        float radians = angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
        Vector2 local = new Vector2(cos * delta.x + sin * delta.y,
            -sin * delta.x + cos * delta.y);
        Vector2 closest = new Vector2(Mathf.Clamp(local.x, -half.x, half.x),
            Mathf.Clamp(local.y, -half.y, half.y));
        return (local - closest).sqrMagnitude <= playerRadius * playerRadius;
    }

    private static IEnumerator FlashOnce(BattleContext c, float seconds)
    {
        c.Overlay.color = Color.black;
        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, seconds));
        if (c.Owner.Result == BattleResult.Running)
            c.Overlay.color = Color.clear;
        // Allow the uncovered scene to render before continuing.
        yield return null;
    }
}