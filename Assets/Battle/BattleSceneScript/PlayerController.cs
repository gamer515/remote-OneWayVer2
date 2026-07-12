using UnityEngine;
using System.Collections;

public class PlayerController : MonoBehaviour
{

    public enum MovementMode { Free, Gravity }

    [Header("Status")]
    public float maxHp = 3f; // 3대 맞으면 사망
    public float currentHp;

    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float jumpForce = 12f;
    public MovementMode currentMode = MovementMode.Free;

    private Rigidbody2D rb;
    private bool isGrounded;
    private bool isInvincible = false; // 무적 상태 확인
    private SpriteRenderer spriteRenderer;

    public bool isControlLocked = false;


    private Color originalColor; // 원래 하트 색상을 저장할 변수
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        currentHp = maxHp;

        originalColor = spriteRenderer.color;
    }

    void FixedUpdate()
    {
        if (isControlLocked) return;

        if (currentMode == MovementMode.Free) MoveFree();
        else MoveGravity();
    }

    void Update()
    {
        if (isControlLocked) return;

        if (currentMode == MovementMode.Gravity && isGrounded)
        {
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                isGrounded = false;
            }
        }
    }

    private void MoveFree()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector2 inputDir = new Vector2(h, v);

        rb.gravityScale = 0;
        rb.linearVelocity = inputDir.normalized * moveSpeed;
    }

    private void MoveGravity()
    {
        float h = Input.GetAxisRaw("Horizontal");
        rb.gravityScale = 3f;
        rb.linearVelocity = new Vector2(h * moveSpeed, rb.linearVelocity.y);
    }

    public void SetMovementMode(MovementMode mode)
    {
        currentMode = mode;
        if (mode == MovementMode.Free)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale = 0;
        }
    }

    // 총알(Trigger)에 닿았을 때 피격 처리
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Bullet"))
        {
            TakeDamage(1f); // 총알에 닿으면 1 데미지
        }
    }

    public void TakeDamage(float amount)
    {
        if (isInvincible) return; // 무적 상태면 데미지 무시

        currentHp -= amount;
        Debug.Log($"플레이어 피격! 남은 HP: {currentHp}");

        if (currentHp <= 0)
        {
            Debug.Log("플레이어 사망 (게임 오버)");
            gameObject.SetActive(false); // 플레이어 하트 숨기기
            // 필요하다면 여기서 BattleStateMachine의 Game Over 상태를 호출할 수 있습니다.
        }
        else
        {
            StartCoroutine(InvincibilityRoutine());
        }
    }

    // 1초 동안 반투명해지며 데미지를 입지 않는 무적 코루틴
    private IEnumerator InvincibilityRoutine()
    {
        isInvincible = true;

        // 언더테일 느낌을 살려서 빨간색 반투명으로 바꿉니다.
        spriteRenderer.color = new Color(1f, 0f, 0f, 0.5f);

        // 1초 동안 무적 상태 유지
        yield return new WaitForSeconds(1f);

        // 수정: 저장해둔 원래 색상으로 완벽하게 복구합니다!
        spriteRenderer.color = originalColor;
        isInvincible = false; // 무적 상태 해제
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("BattleBox")) isGrounded = true;
    }

    //대화 상태일 때 플레이어를 숨기고/보이게 하는 함수
    public void SetVisible(bool isVisible)
    {
        // 1. 이미지(스프라이트) 끄고 켜기
        if (spriteRenderer != null)
            spriteRenderer.enabled = isVisible;

        // 2. 충돌체(콜라이더) 끄고 켜기 (대화 중에 투명한 상태로 맞는 버그 방지!)
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
            col.enabled = isVisible;
    }
}