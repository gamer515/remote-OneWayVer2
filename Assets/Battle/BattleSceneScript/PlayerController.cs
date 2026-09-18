using System;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public enum MovementMode { Free, Gravity, External }
    public float maxHp = 3f;
    public float currentHp;
    public float moveSpeed = 5f;
    public float jumpForce = 12f;

    [Header("피격 무적")]
    [Min(0f)]
    public float invincibilityDuration = 2f;

    [Min(0.02f)]
    public float blinkInterval = 0.12f;

    private float blinkElapsed;

    [Header("B2 마우스 이동")]
    [Min(0f)]
    public float b2MouseMoveSpeed = 8f;

    private bool IsB2 =>
        context != null &&
        context.Stage != null &&
        context.Stage.stageIndex == 2;

    public MovementMode currentMode;
    public bool isControlLocked;
    public event Action Died;
    public Rigidbody2D Body { get; private set; }
    public Vector2 HalfSize => spriteRenderer != null ? (Vector2)spriteRenderer.bounds.extents : Vector2.one * .25f;
    public float HitRadius => Mathf.Min(HalfSize.x, HalfSize.y) * .8f;
    SpriteRenderer spriteRenderer;
    BattleContext context;
    bool grounded, paused, visible = true;
    float invincibleTime;
    Color originalColor;
    Vector2 savedVelocity;
    bool savedSimulated;

    void Awake()
    {
        Body = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalColor = spriteRenderer.color;
        currentHp = maxHp;
    }
    public void Configure(BattleContext battle, Sprite heart)
    {
        context = battle;
        spriteRenderer.sprite = heart;
        spriteRenderer.color = originalColor = Color.white;
        spriteRenderer.sortingOrder = 10;
        float scale = .8f / Mathf.Max(heart.bounds.size.x, heart.bounds.size.y);
        transform.localScale = Vector3.one * scale;
        var collider = GetComponent<CircleCollider2D>();
        if (collider != null) { collider.offset = heart.bounds.center; collider.radius = Mathf.Min(heart.bounds.extents.x, heart.bounds.extents.y) * .8f; }
    }
    void Update()
    {
        float dt = context != null ? context.Clock.Delta : Time.deltaTime;
        if (!paused && invincibleTime > 0f)
        {
            invincibleTime = Mathf.Max(0f, invincibleTime - dt);
            blinkElapsed += dt;
        }

        RefreshPlayerAppearance();

        if (paused || isControlLocked || currentHp <= 0) return;
        bool jumpPressed = IsB2 
            ? Input.GetMouseButtonDown(0)
            : Input.GetKeyDown(KeyCode.W) ||
            Input.GetKeyDown(KeyCode.UpArrow);

        if (currentMode == MovementMode.Gravity &&
            grounded &&
            jumpPressed)
        {
            Body.linearVelocity =
                new Vector2(Body.linearVelocity.x, jumpForce);

            grounded = false;
        }
    }
    void FixedUpdate()
    {
        if (paused ||
            isControlLocked ||
            currentHp <= 0 ||
            currentMode == MovementMode.External)
            return;

        if (IsB2)
        {
            MoveB2WithMouse();
            return;
        }

        // 다른 스테이지의 기존 이동.
        float h = Input.GetAxisRaw("Horizontal");

        if (currentMode == MovementMode.Free)
        {
            Body.gravityScale = 0;

            Body.linearVelocity = new Vector2(
                h,
                Input.GetAxisRaw("Vertical")).normalized * moveSpeed;
        }
        else
        {
            Body.gravityScale = 3;

            Body.linearVelocity = new Vector2(
                h * moveSpeed,
                Body.linearVelocity.y);
        }
    }
    void LateUpdate()
    {
        if (context == null || paused || isControlLocked) return;
        Vector2 clamped = context.Arena.Clamp(transform.position, HalfSize);
        if (currentMode == MovementMode.Gravity && context.Arena.Mode != BattleArena.Boundary.None)
            grounded = transform.position.y <= context.Arena.Bounds.yMin + HalfSize.y + .03f && Body.linearVelocity.y <= .01f;
        transform.position = new Vector3(clamped.x, clamped.y, 0);
    }
    public void SetMovementMode(MovementMode mode)
    {
        currentMode = mode;
        Body.linearVelocity = Vector2.zero;
        Body.gravityScale = mode == MovementMode.Gravity ? 3 : 0;
        grounded = false;
    }
    public void Lock(bool locked)
    {
        isControlLocked = locked;
        if (Body != null && locked) { Body.linearVelocity = Vector2.zero; Body.gravityScale = 0; }
    }
    public void SetPaused(bool value)
    {
        if (paused == value || Body == null) return;
        paused = value;
        if (value) { savedVelocity = Body.linearVelocity; savedSimulated = Body.simulated; Body.simulated = false; }
        else { Body.simulated = savedSimulated; Body.linearVelocity = savedVelocity; }
    }
    public void TakeDamage(float amount) => ApplyDamage(amount, false);
    // Each uncut target deals damage independently of bullet invulnerability.
    public void TakeArrivalDamage(float amount) => ApplyDamage(amount, true);
    void ApplyDamage(float amount, bool ignoreInvulnerability)
    {
        if (paused || !visible || currentHp <= 0 || amount <= 0 || (!ignoreInvulnerability && invincibleTime > 0)) return;
        currentHp = Mathf.Max(0, currentHp - amount);
        if (currentHp <= 0)
        {
            Lock(true);
            SetVisible(false);
            if (Died != null) Died.Invoke();
            else gameObject.SetActive(false);
        }
        else
        {
            invincibleTime = Mathf.Max(0f, invincibilityDuration);
            blinkElapsed = 0f;
            RefreshPlayerAppearance();
        }
    }
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Bullet")) TakeDamage(1);
    }
    void OnCollisionStay2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("BattleBox")) return;
        foreach (var contact in collision.contacts) if (contact.normal.y > .5f) grounded = true;
    }
    void OnCollisionExit2D(Collision2D collision) { if (collision.gameObject.CompareTag("BattleBox")) grounded = false; }
    public void SetVisible(bool value)
    {
        visible = value;

        RefreshPlayerAppearance();

        var col = GetComponent<Collider2D>();

        if (col != null)
            col.enabled = value;
    }

    private void RefreshPlayerAppearance()
    {
        if (spriteRenderer == null)
            return;

        bool blinkVisible = true;

        if (invincibleTime > 0f)
        {
            float interval = Mathf.Max(0.02f, blinkInterval);
            int phase = Mathf.FloorToInt(blinkElapsed / interval);

            blinkVisible = phase % 2 == 0;
        }

        // 기존 반투명 효과 제거.
        spriteRenderer.color = originalColor;

        // 사망·연출로 숨긴 상태는 깜빡임이 덮어쓰지 않음.
        spriteRenderer.enabled = visible && blinkVisible;
    }

    private void MoveB2WithMouse()
    {
        // 마우스 화면 좌표를 플레이어가 있는 Z 평면에 투영.
        Ray ray = context.Camera.ScreenPointToRay(Input.mousePosition);

        Plane playerPlane = new Plane(
            Vector3.forward,
            new Vector3(0f, 0f, transform.position.z));

        if (!playerPlane.Raycast(ray, out float distance))
            return;

        Vector2 target = ray.GetPoint(distance);

        // 마우스가 상자 밖에 있어도 플레이어는 상자 안에 머묾.
        target = context.Arena.Clamp(target, HalfSize);

        Vector2 current = Body.position;
        float dt = Time.fixedDeltaTime;
        float maxDistance = Mathf.Max(0f, b2MouseMoveSpeed) * dt;

        if (currentMode == MovementMode.Free)
        {
            Body.gravityScale = 0f;

            Vector2 next = Vector2.MoveTowards(
                current,
                target,
                maxDistance);

            Body.linearVelocity = (next - current) / dt;
        }
        else if (currentMode == MovementMode.Gravity)
        {
            Body.gravityScale = 3f;

            // 점프 패턴에서는 마우스 Y를 따라가지 않음.
            float nextX = Mathf.MoveTowards(
                current.x,
                target.x,
                maxDistance);

            Body.linearVelocity = new Vector2(
                (nextX - current.x) / dt,
                Body.linearVelocity.y);
        }
    }
}
