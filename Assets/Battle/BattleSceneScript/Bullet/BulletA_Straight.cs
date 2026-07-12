using UnityEngine;

public class BulletA_Straight : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("총알의 이동 속도입니다.")]
    public float speed = 8f;

    [Tooltip("총알이 나아갈 방향입니다. (기본값: 아래쪽)")]
    public Vector2 direction = Vector2.down;

    [Header("Boundary Settings")]
    [Tooltip("중심점으로부터 이 거리 이상 멀어지면 자동으로 파괴됩니다.")]
    public float destroyDistance = 20f;

    private Rigidbody2D rb;
    private Vector2 startPosition;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        startPosition = transform.position;

        // 수정: 내가 직접 속도를 정하지 않고, 이미 누군가(매니저)가 속도를 줬다면 그걸 그대로 씁니다!
        if (rb != null)
        {
            // 만약 매니저가 아무 속도도 안 줬다면(0,0) 기본 방향으로 쏩니다.
            if (rb.linearVelocity == Vector2.zero)
            {
                rb.linearVelocity = direction.normalized * speed;
            }
        }
    }

    void Update()
    {
        // 만약 Rigidbody2D가 없거나 Kinematic 모드 등에서 수동 이동이 필요할 경우를 위한 방어 코드
        if (rb == null)
        {
            transform.Translate(direction.normalized * speed * Time.deltaTime, Space.World);
        }

        // 성능 최적화를 위해 시작 지점에서 너무 멀어지면 자동으로 메모리에서 삭제합니다.
        if (Vector2.Distance(startPosition, transform.position) > destroyDistance)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 부딪힌 대상이 Player 태그를 가지고 있다면
        if (collision.CompareTag("Player"))
        {
            // 플레이어의 스크립트를 가져옵니다. (이름이 다르면 꼭 수정하세요!)
            PlayerController player = collision.GetComponent<PlayerController>();

            if (player != null)
            {
                // 플레이어에게 데미지 1을 입힙니다. (함수 이름이 다르면 수정하세요!)
                player.TakeDamage(1);
            }

        }
    }
}
