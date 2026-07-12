using UnityEngine;
using System.Collections;

// 탄막 A (플레이어 조준 유도탄)
public class BulletA_Homing : MonoBehaviour
{
    public float speed = 8f;
    private Vector2 direction;

    void Start()
    {
        GameObject player = GameObject.Find("Player"); // 플레이어 이름에 맞게 수정
        if (player != null)
        {
            direction = (player.transform.position - transform.position).normalized;
        }
    }

    void Update()
    {
        transform.Translate(direction * speed * Time.deltaTime);
    }
}