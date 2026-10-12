using UnityEngine;

public sealed class BDMovingPlatform : MonoBehaviour
{
    public bool Finished { get; private set; }

    private BattleContext context;
    private Rigidbody2D body;
    private Collider2D platformCollider;
    private SpriteRenderer visual;

    private float speed;
    private float halfWidth;
    private float endX;
    private bool initialized;

    public void Initialize(
        BattleContext battle,
        float moveSpeed,
        float width,
        float rightEdge)
    {
        context = battle;
        body = GetComponent<Rigidbody2D>();
        platformCollider = GetComponent<Collider2D>();
        visual = GetComponent<SpriteRenderer>();

        speed = moveSpeed;
        halfWidth = width * 0.5f;
        endX = rightEdge;
        initialized = true;
    }

    private void FixedUpdate()
    {
        if (!initialized || Finished)
            return;

        if (context.Clock.Paused ||
            context.Owner.Result != BattleResult.Running)
        {
            body.linearVelocity = Vector2.zero;
            return;
        }

        if (body.position.x - halfWidth >= endX)
        {
            Finished = true;
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
            platformCollider.enabled = false;
            visual.enabled = false;
            return;
        }

        
        Vector2 next = body.position
            + Vector2.right * speed * Time.fixedDeltaTime;

        body.MovePosition(next);
    }
}