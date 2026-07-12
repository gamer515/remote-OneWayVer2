using UnityEngine;

public class BulletC_StaticSpike : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 부딪힌 대상이 플레이어라면
        if (collision.CompareTag("Player"))
        {
            PlayerController player = collision.GetComponent<PlayerController>();

            if (player != null)
            {
                // 데미지 1을 준다
                player.TakeDamage(1);
            }

            // 플레이어와 닿았으니 가시 파괴!
            //Destroy(gameObject);
        }
    }
}