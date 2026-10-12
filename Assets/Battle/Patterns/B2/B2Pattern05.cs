using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class B2Pattern05 : IBattlePattern
{
    private sealed class Fragment
    {
        public Transform visual;
        public Vector2 origin, velocity;
        public float birth, angle, spin;
    }

    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        // 이 패턴에 필요한 이동 방식과 시작 위치를 적용한다.
        BattlePlayerPreparationRunner.Apply(c, data.playerPreparation);

        // 다음 단계가 준비될 때까지 이동을 잠근다.
        scope.OnDispose(() => c.Player.Lock(true));

        Texture2D texture = Texture2D.whiteTexture;
        Sprite square = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            Vector2.one * 0.5f, texture.width, 0, SpriteMeshType.FullRect);
        scope.OnDispose(() => Object.Destroy(square));

        Rect box = c.Arena.Bounds;
        float speed = Mathf.Max(0.1f, data.speed);
        float spacing = Mathf.Max(0.1f, data.burstSwordSpacing);
        Vector2 size = new Vector2(Mathf.Max(0.02f, data.swordSize.x), Mathf.Max(0.1f, data.swordSize.y));
        Vector2 half = size * 0.5f;
        float rotation = data.swordRotationSpeed;
        float fragmentSize = Mathf.Max(0.02f, data.burstFragmentSize);
        float fragmentSpeed = Mathf.Max(0.1f, data.burstFragmentSpeed);
        float lifetime = Mathf.Max(0.1f, data.burstFragmentLifetime);
        int fragmentCount = Mathf.Clamp(data.burstFragmentCount, 3, 40);
        // 순서: 아래쪽 → 위쪽 → 가운데 높이
        float[] explosionPositions =
        {
         Mathf.Clamp(data.burstViewportX, 0.05f, 0.95f),
         0.45f,
         0.70f
        };
        float startX = Mathf.Max(c.Arena.ScreenRect.xMax, box.xMax) + half.magnitude + 0.5f;
        float[] lanes = { 0.25f, 0.75f, 0.5f }; // front bottom, then top, then middle
        var origins = new Vector2[3];
        var times = new float[3];
        var swords = new Transform[3];
        for (int i = 0; i < 3; i++)
        {
            origins[i] = new Vector2(
                startX + i * spacing,
                Mathf.Lerp(box.yMin, box.yMax, lanes[i])
                );

            float explosionX = Mathf.Lerp(
                box.xMin,
                box.xMax,
                explosionPositions[i]
            );

            times[i] = (origins[i].x - explosionX) / speed;
            swords[i] = CreateSquare("Burst Sword " + (i + 1), square, scope.Root, size);
            swords[i].position = origins[i];
        }
        var fragments = new List<Fragment>(fragmentCount * 3);
        int exploded = 0;
        float elapsed = 0f;
        Vector2 previousPlayer = c.Player.transform.position;
        float fragmentSpin = Mathf.Max(90f, Mathf.Abs(rotation));
        float travelBound = Mathf.Max(speed + Mathf.Abs(rotation) * Mathf.Deg2Rad * half.magnitude,
            fragmentSpeed * 1.15f + fragmentSpin * Mathf.Deg2Rad * fragmentSize);
        while ((exploded < 3 || fragments.Count > 0) && c.Owner.Result == BattleResult.Running)
        {
            float dt = c.Clock.Delta;
            if (dt <= 0f) { yield return null; continue; }
            Vector2 player = c.Player.transform.position;
            int samples = Mathf.Max(1, Mathf.CeilToInt((travelBound * dt + Vector2.Distance(previousPlayer, player)) / 0.04f));
            bool contacted = false;
            for (int sample = 1; sample <= samples; sample++)
            {
                float blend = (float)sample / samples;
                float time = elapsed + dt * blend;
                Vector2 playerAtTime = Vector2.Lerp(previousPlayer, player, blend);
                for (int i = 0; i < 3; i++)
                {
                    if (swords[i] == null) continue;
                    float swordTime = Mathf.Min(time, times[i]);
                    Vector2 position = origins[i] + Vector2.left * (speed * swordTime);
                    float angle = rotation * swordTime;
                    swords[i].SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
                    if (time >= times[i])
                    {
                        Burst(square, scope.Root, position, angle, times[i], half, fragmentCount,
                            fragmentSize, fragmentSpeed, fragmentSpin, fragments);
                        swords[i].gameObject.SetActive(false);
                        Object.Destroy(swords[i].gameObject);
                        swords[i] = null;
                        exploded++;
                    }
                    else if (!contacted && Touches(playerAtTime, c.Player.HitRadius + 0.02f, position, angle, half))
                    {
                        contacted = true;
                        c.Player.TakeDamage(1f);
                        if (c.Owner.Result != BattleResult.Running) yield break;
                    }
                }
                for (int i = fragments.Count - 1; i >= 0; i--)
                {
                    Fragment fragment = fragments[i];
                    float age = time - fragment.birth;
                    if (age >= lifetime)
                    {
                        fragment.visual.gameObject.SetActive(false);
                        Object.Destroy(fragment.visual.gameObject);
                        fragments.RemoveAt(i);
                        continue;
                    }
                    Vector2 position = fragment.origin + fragment.velocity * age;
                    float angle = fragment.angle + fragment.spin * age;
                    fragment.visual.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
                    if (!contacted && Touches(playerAtTime, c.Player.HitRadius + 0.02f, position,
                        angle, Vector2.one * (fragmentSize * 0.5f)))
                    {
                        contacted = true;
                        c.Player.TakeDamage(1f);
                        if (c.Owner.Result != BattleResult.Running) yield break;
                    }
                }
            }
            elapsed += dt;
            previousPlayer = player;
            yield return null;
        }
    }

    private static void Burst(Sprite square, Transform parent, Vector2 position, float angle, float time,
        Vector2 half, int count, float size, float speed, float spin, List<Fragment> fragments)
    {
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
        float phase = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            // Pieces start along the actual rotated blade, rather than a separate explosion sprite.
            Vector2 local = new Vector2(Random.Range(-half.x, half.x), Mathf.Lerp(-half.y, half.y, (i + 0.5f) / count));
            Vector2 origin = position + (Vector2)(rotation * (Vector3)local);
            float direction = (phase + 360f * (i + Random.Range(-0.2f, 0.2f)) / count) * Mathf.Deg2Rad;
            Transform visual = CreateSquare("Sword Fragment", square, parent, Vector2.one * size);
            visual.SetPositionAndRotation(origin, rotation);
            fragments.Add(new Fragment
            {
                visual = visual, origin = origin, birth = time, angle = angle,
                velocity = new Vector2(Mathf.Cos(direction), Mathf.Sin(direction)) * speed * Random.Range(0.85f, 1.15f),
                spin = spin * (Random.value < 0.5f ? -1f : 1f)
            });
        }
    }

    private static Transform CreateSquare(string name, Sprite square, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sprite = square;
        renderer.color = Color.white;
        renderer.sortingOrder = 15;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go.transform;
    }

    private static bool Touches(Vector2 player, float radius, Vector2 center, float angle, Vector2 half)
    {
        Vector2 delta = player - center;
        float radians = angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
        Vector2 local = new Vector2(cos * delta.x + sin * delta.y, -sin * delta.x + cos * delta.y);
        Vector2 nearest = new Vector2(Mathf.Clamp(local.x, -half.x, half.x), Mathf.Clamp(local.y, -half.y, half.y));
        return (local - nearest).sqrMagnitude <= radius * radius;
    }
}