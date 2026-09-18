using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class B3BulletPattern : IBattlePattern
{
    struct Bullet { public Transform transform; public Vector2 velocity; }
    public IEnumerator Run(BattleContext c, BattlePatternData data, BattleStepScope scope)
    {
        c.Box.SetWalls(false, false);
        c.Arena.Mode = BattleArena.Boundary.Screen;
        c.Player.SetMovementMode(PlayerController.MovementMode.Free);
        c.Player.Lock(false); c.Player.SetVisible(true);
        var bullets = new List<Bullet>();
        float elapsed = 0, spawn = 0, angle = 0;
        while (elapsed < data.duration && c.Owner.Result == BattleResult.Running)
        {
            float dt = c.Clock.Delta;
            elapsed += dt; spawn -= dt;
            Rect r = c.Arena.ScreenRect;
            if (spawn <= 0)
            {
                spawn = Mathf.Max(.05f, data.interval);
                int count = Mathf.Clamp(data.count, 1, 40);
                int gap = Random.Range(0, count);
                for (int i = 0; i < count; i++)
                {
                    Vector2 position, velocity;
                    if (data.kind == BattlePatternKind.Rain)
                    {
                        if (i == gap || i == (gap + 1) % count) continue;
                        position = new Vector2(Mathf.Lerp(r.xMin + .6f, r.xMax - .6f, (i + .5f) / count), r.yMax);
                        velocity = Vector2.down * data.speed;
                    }
                    else
                    {
                        position = new Vector2(r.center.x, Mathf.Lerp(r.yMin, r.yMax, .78f));
                        float degrees = data.kind == BattlePatternKind.Aimed
                            ? Mathf.Atan2(c.Player.transform.position.y - position.y, c.Player.transform.position.x - position.x) * Mathf.Rad2Deg + (i - (count - 1) / 2f) * 12
                            : angle + i * 360f / count;
                        velocity = new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * data.speed;
                    }
                    var go = c.Primitive("B3 Bullet", PrimitiveType.Sphere, scope.Root, position, Vector3.one * .22f);
                    bullets.Add(new Bullet { transform = go.transform, velocity = velocity });
                }
                angle += data.angleStep;
            }
            for (int i = bullets.Count - 1; i >= 0; i--)
            {
                var bullet = bullets[i];
                Vector2 before = bullet.transform.position;
                bullet.transform.position += (Vector3)(bullet.velocity * dt);
                bool hit = SlashResolver.DistanceToSegment(c.Player.transform.position, before, bullet.transform.position) < c.Player.HitRadius + .11f;
                Vector2 p = bullet.transform.position;
                if (hit || p.x < r.xMin - 2 || p.x > r.xMax + 2 || p.y < r.yMin - 2 || p.y > r.yMax + 2)
                {
                    Object.Destroy(bullet.transform.gameObject); bullets.RemoveAt(i);
                    if (hit) c.Player.TakeDamage(1);
                    if (c.Owner.Result != BattleResult.Running) yield break;
                }
            }
            yield return null;
        }
        c.Player.Lock(true);
    }
}
