using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class B2Pattern04 : IBattlePattern
{
    private sealed class Sword
    {
        public Transform transform;
        public Vector2 origin;
        public Vector2 velocity;
        public float spawnTime;
    }

    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);

        // 패턴이 끝나거나 중단되면 플레이어 입력을 잠근다.
        scope.OnDispose(() => c.Player.Lock(true));

        var group = new GameObject("B2 Falling Sword Group", typeof(SortingGroup));
        group.transform.SetParent(scope.Root, false);
        group.GetComponent<SortingGroup>().sortingOrder = 15;
        Texture2D white = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(white, new Rect(0, 0, white.width, white.height),
            new Vector2(0.5f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
        scope.OnDispose(() => Object.Destroy(square));
        var maskObject = new GameObject("BattleBox Interior Mask", typeof(SpriteMask));
        maskObject.transform.SetParent(group.transform, false);
        var mask = maskObject.GetComponent<SpriteMask>();
        mask.sprite = square;
        mask.alphaCutoff = 0.5f;
        mask.isCustomRangeActive = true;
        mask.frontSortingOrder = 1;
        mask.backSortingOrder = -1;

        int count = Mathf.Clamp(data.count, 1, 40);
        float interval = Mathf.Max(0.05f, data.interval);
        float fallSpeed = Mathf.Max(0.1f, data.speed);
        float driftSpeed = Mathf.Max(0f, data.fallingSwordDriftSpeed);
        Vector2 size = new Vector2(Mathf.Max(0.02f, data.swordSize.x),
            Mathf.Max(0.1f, data.swordSize.y));
        Vector2 half = size * 0.5f;
        var swords = new List<Sword>(count);
        int spawned = 0;
        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;
        float motionBound = new Vector2(driftSpeed, fallSpeed).magnitude;

        while ((spawned < count || swords.Count > 0) && c.Owner.Result == BattleResult.Running)
        {
            Rect bounds = c.Arena.Bounds;
            maskObject.transform.position = new Vector3(bounds.center.x, bounds.center.y, 0f);
            maskObject.transform.localScale = new Vector3(bounds.width, bounds.height, 1f);
            float dt = c.Clock.Delta;
            if (dt <= 0f) { yield return null; continue; }
            float nextTime = elapsed + dt;
            while (spawned < count && spawned * interval <= nextTime)
            {
                float margin = Mathf.Min(half.x, bounds.width * 0.5f);
                var origin = new Vector2(Random.Range(bounds.xMin + margin, bounds.xMax - margin),
                    bounds.yMax + half.y);
                var go = new GameObject("B2 Falling Sword " + (spawned + 1), typeof(SpriteRenderer));
                go.transform.SetParent(group.transform, false);
                go.transform.localScale = new Vector3(size.x, size.y, 1f);
                go.transform.position = new Vector3(origin.x, origin.y, 0f);
                var renderer = go.GetComponent<SpriteRenderer>();
                renderer.sprite = square;
                renderer.color = Color.white;
                renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                renderer.sortingOrder = 0;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                go.SetActive(false);
                swords.Add(new Sword
                {
                    transform = go.transform,
                    origin = origin,
                    velocity = new Vector2((Random.value < 0.5f ? -1f : 1f) * driftSpeed, -fallSpeed),
                    spawnTime = spawned * interval
                });
                spawned++;
            }

            Vector2 player = c.Player.transform.position;
            int samples = Mathf.Max(1, Mathf.CeilToInt((motionBound * dt
                + Vector2.Distance(previousPlayer, player)) / 0.04f));
            bool contacted = false;
            for (int sample = 1; sample <= samples; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;
                Vector2 playerAtTime = Vector2.Lerp(previousPlayer, player, blend);
                for (int i = swords.Count - 1; i >= 0; i--)
                {
                    Sword sword = swords[i];
                    float age = time - sword.spawnTime;
                    if (age < 0f) continue;
                    Vector2 position = sword.origin + sword.velocity * age;
                    sword.transform.gameObject.SetActive(true);
                    sword.transform.position = new Vector3(position.x, position.y, 0f);
                    if (position.y + half.y < bounds.yMin ||
                        (sword.velocity.x < 0f && position.x + half.x < bounds.xMin) ||
                        (sword.velocity.x > 0f && position.x - half.x > bounds.xMax))
                    {
                        sword.transform.gameObject.SetActive(false);
                        Object.Destroy(sword.transform.gameObject);
                        swords.RemoveAt(i);
                        continue;
                    }
                    if (!contacted && HitsVisiblePart(playerAtTime, c.Player.HitRadius, position, half, bounds))
                    {
                        contacted = true;
                        c.Player.TakeDamage(1f);
                        if (c.Owner.Result != BattleResult.Running) yield break;
                    }
                }
            }
            elapsed = nextTime;
            previousPlayer = player;
            yield return null;
        }
    }

    private static bool HitsVisiblePart(Vector2 player, float radius, Vector2 center, Vector2 half, Rect bounds)
    {
        // Collision uses only the blade rectangle's intersection with the visible mask.
        float left = Mathf.Max(bounds.xMin, center.x - half.x);
        float right = Mathf.Min(bounds.xMax, center.x + half.x);
        float bottom = Mathf.Max(bounds.yMin, center.y - half.y);
        float top = Mathf.Min(bounds.yMax, center.y + half.y);
        if (left >= right || bottom >= top) return false;
        Vector2 nearest = new Vector2(Mathf.Clamp(player.x, left, right), Mathf.Clamp(player.y, bottom, top));
        return (player - nearest).sqrMagnitude <= radius * radius;
    }
}