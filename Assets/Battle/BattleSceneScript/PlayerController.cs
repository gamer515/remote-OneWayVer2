using System;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public enum MovementMode { Free, Gravity, External }
    public float maxHp = 3f;
    public float currentHp;
    public float moveSpeed = 5f;
    public float jumpForce = 12f;

    [Header("중력 모드 공통 점프")]
    [Min(0.1f)] public float riseGravity = 3f;
    [Min(0.1f)] public float heldGravity = 1.8f;
    [Min(0f)] public float maxJumpHoldTime = 0.2f;

    [Range(0.1f, 1f)]
    public float jumpReleaseMultiplier = 0.55f;

    [Header("점프 고점")]
    [Min(0f)] public float apexSpeedThreshold = 2f;
    [Min(0.1f)] public float apexGravity = 0.6f;
    [Min(0f)] public float apexHangTime = 0.15f;

    [Header("낙하")]
    [Min(0.1f)] public float fallGravity = 4f;
    [Min(0.1f)] public float maxFallSpeed = 14f;

    private bool jumpRequested;
    private bool jumpHeld;
    private bool jumpReleased;

    private bool jumpInProgress;
    private bool jumpWasCut;
    private float jumpHoldElapsed;
    private float apexElapsed;

    [Header("피격 무적")]
    [Min(0f)]
    public float invincibilityDuration = 2f;

    [Min(0.02f)]
    public float blinkInterval = 0.12f;

    private float blinkElapsed;

    [Header("B2 마우스 이동")]
    [Min(0f)]
    public float b2MouseMoveSpeed = 8f;

    

    // 이 기능을 켠 패턴에서만 중앙 도달 후 가로 입력을 배경 이동으로 전달한다.
    private bool gravityScrollEnabled;
    private float gravityScrollCenter;
    public bool GravityScrollLocked { get; private set; }
    public float GravityScrollDistance { get; private set; }

    public void EnableGravityScroll(float centerX)
    {
        ClearGravityScroll();
        gravityScrollEnabled = true;
        gravityScrollCenter = centerX;
    }

    public void ClearGravityScroll()
    {
        gravityScrollEnabled = false;
        GravityScrollLocked = false;
        GravityScrollDistance = 0f;
    }
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
        float dt = context != null
            ? context.Clock.Delta
            : Time.deltaTime;

        if (!paused && invincibleTime > 0f)
        {
            invincibleTime = Mathf.Max(0f, invincibleTime - dt);
            blinkElapsed += dt;
        }

        RefreshPlayerAppearance();

        bool battlePaused =
            context != null && context.Clock.Paused;

        if (paused ||
            battlePaused ||
            isControlLocked ||
            currentHp <= 0 ||
            currentMode != MovementMode.Gravity)
        {
            ClearJumpInput();
            return;
        }

        bool pointerOverUI =
            UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current
                .IsPointerOverGameObject();

        // 버튼을 누른 순간에만 새 점프를 요청한다.
        if (Input.GetMouseButtonDown(0) && !pointerOverUI)
            jumpRequested = true;

        jumpHeld = Input.GetMouseButton(0);

        // 다음 물리 프레임 전에 다시 눌러도 놓았던 사실을 유지한다.
        if (Input.GetMouseButtonUp(0))
            jumpReleased = true;
    }
    void FixedUpdate()
    {
        if (context == null ||
            context.Clock.Paused ||
            paused ||
            isControlLocked ||
            currentHp <= 0 ||
            currentMode == MovementMode.External)
        {
            return;
        }

        MoveWithMouse();

        if (currentMode == MovementMode.Gravity)
            UpdateGravityJump();
    }
    void LateUpdate()
    {
        if (context == null || paused || isControlLocked)
            return;

        Vector2 position = transform.position;
        Vector2 clamped = context.Arena.Clamp(position, HalfSize);
        if (currentMode == MovementMode.Gravity && GravityScrollLocked)
            clamped.x = gravityScrollCenter;

        if (currentMode == MovementMode.Gravity &&
            clamped.y < position.y &&
            Body.linearVelocity.y > 0f)
        {
            // 천장에 닿으면 상승과 고점 효과를 끝낸다.
            Body.linearVelocity =
                new Vector2(Body.linearVelocity.x, 0f);

            ResetJumpMotion();
        }

        transform.position =
            new Vector3(clamped.x, clamped.y, 0f);

        Body.position = clamped;
    }
    public void SetMovementMode(MovementMode mode)
    {
        ClearGravityScroll();
        currentMode = mode;

        ResetJumpState();
        grounded = false;

        Body.linearVelocity = Vector2.zero;

        Body.gravityScale = mode == MovementMode.Gravity
            ? Mathf.Max(0.1f, riseGravity)
            : 0f;
    }
    public void Lock(bool locked)
    {
        isControlLocked = locked;

        if (!locked)
            return;

        ClearGravityScroll();

        ResetJumpState();
        grounded = false;

        if (Body != null)
        {
            Body.linearVelocity = Vector2.zero;
            Body.gravityScale = 0f;
        }
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

    private void MoveWithMouse()
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
            

            // 점프 패턴에서는 마우스 Y를 따라가지 않음.
            float nextX = Mathf.MoveTowards(
                current.x,
                target.x,
                maxDistance);

            if (gravityScrollEnabled)
            {
                if (!GravityScrollLocked &&
                    (current.x >= gravityScrollCenter || nextX >= gravityScrollCenter))
                {
                    // 중앙선을 넘는 물리 프레임에서 바로 가로 위치를 고정한다.
                    GravityScrollLocked = true;
                }

                if (GravityScrollLocked)
                {
                    float offset = target.x - gravityScrollCenter;
                    float scroll = Mathf.Abs(offset) <= 0.05f ? 0f
                        : -Mathf.Sign(offset) * maxDistance;
                    GravityScrollDistance += scroll;
                    Body.position = new Vector2(gravityScrollCenter, Body.position.y);
                    Body.linearVelocity = new Vector2(0f, Body.linearVelocity.y);
                    return;
                }
            }

            Body.linearVelocity = new Vector2(
                (nextX - current.x) / dt,
                Body.linearVelocity.y);
        }
    }

    private readonly ContactPoint2D[] groundContacts =
    new ContactPoint2D[16];

    private void RefreshGrounded()
    {
        grounded = false;

        if (currentMode != MovementMode.Gravity ||
            Body == null ||
            Body.linearVelocity.y > 0.1f)
        {
            return;
        }

        // BattleBox 바닥에 의해 위치가 보정되는 경우도 착지로 취급한다.
        if (context != null &&
            context.Arena.Mode == BattleArena.Boundary.Box)
        {
            float floor = context.Arena.Bounds.yMin;

            if (Body.position.y <= floor + HalfSize.y + 0.03f)
            {
                grounded = true;
                return;
            }
        }

        // 발판이나 벽의 윗면에 접촉했는지 확인한다.
        int count = Body.GetContacts(groundContacts);

        for (int i = 0; i < count; i++)
        {
            if (groundContacts[i].normal.y > 0.5f)
            {
                grounded = true;
                return;
            }
        }
    }

    private void UpdateGravityJump()
    {
        float dt = Time.fixedDeltaTime;

        RefreshGrounded();

        Vector2 velocity = Body.linearVelocity;

        if (grounded)
        {
            ResetJumpMotion();

            // 바닥 보정으로 착지한 경우에도 낙하 속도를 제거한다.
            if (velocity.y < 0f)
                velocity.y = 0f;
        }

        if (jumpRequested && grounded)
        {
            velocity.y = Mathf.Max(0f, jumpForce);

            grounded = false;
            jumpInProgress = true;
            jumpWasCut = false;
            jumpHoldElapsed = 0f;
            apexElapsed = 0f;
        }

        // 공중에서 누른 입력을 착지할 때까지 저장하지 않는다.
        jumpRequested = false;

        if (jumpInProgress &&
            !jumpWasCut &&
            velocity.y > 0f &&
            (jumpReleased || !jumpHeld))
        {
            // 버튼을 일찍 놓으면 상승 속도를 한 번만 줄인다.
            velocity.y *= Mathf.Clamp(
                jumpReleaseMultiplier, 0.1f, 1f);

            jumpWasCut = true;
        }

        jumpReleased = false;

        bool holdingRise =
            jumpInProgress &&
            !jumpWasCut &&
            jumpHeld &&
            velocity.y > 0f &&
            jumpHoldElapsed < maxJumpHoldTime;

        bool nearApex =
            jumpInProgress &&
            !jumpWasCut &&
            !grounded &&
            Mathf.Abs(velocity.y) <= apexSpeedThreshold &&
            apexElapsed < apexHangTime;

        float gravity;

        if (grounded)
        {
            gravity = riseGravity;
        }
        else if (nearApex)
        {
            // 고점 부근에서만 제한된 시간 동안 천천히 움직인다.
            gravity = apexGravity;
            apexElapsed += dt;
        }
        else if (velocity.y > 0f)
        {
            gravity = holdingRise ? heldGravity : riseGravity;
        }
        else
        {
            gravity = fallGravity;
        }

        if (jumpInProgress)
            jumpHoldElapsed += dt;

        float gravityScale = Mathf.Max(0.1f, gravity);
        float fallLimit = Mathf.Max(0.1f, maxFallSpeed);

        // 이번 물리 계산에서 적용될 중력까지 고려해 낙하 속도를 제한한다.
        float gravityStep =
            Physics2D.gravity.y * gravityScale * dt;

        if (velocity.y + gravityStep < -fallLimit)
            velocity.y = -fallLimit - gravityStep;

        Body.gravityScale = gravityScale;
        Body.linearVelocity = velocity;
    }

    private void ClearJumpInput()
    {
        jumpRequested = false;
        jumpHeld = false;
        jumpReleased = false;
    }

    private void ResetJumpMotion()
    {
        jumpInProgress = false;
        jumpWasCut = false;
        jumpHoldElapsed = 0f;
        apexElapsed = 0f;
    }

    private void ResetJumpState()
    {
        ClearJumpInput();
        ResetJumpMotion();
    }
}
