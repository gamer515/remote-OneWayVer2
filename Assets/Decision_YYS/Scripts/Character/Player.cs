using UnityEngine;

public class Player : MonoBehaviour
{
    private float targetZ;
    [SerializeField] private float moveSpeed = 3.0f; // 이동 속도 (원하는 느낌에 따라 조절 가능)
    private bool isInitialized = false;

    // 초기화 및 시작 위치 설정
    public void Initialize(Vector3 startPos)
    {
        transform.position = startPos;
        targetZ = startPos.z;
        isInitialized = true;
    }

    // 새로운 목표 Z축 지점 설정
    public void SetTargetZ(float z)
    {
        targetZ = z;
    }

    private void Update()
    {
        if (!isInitialized) return;

        // 현재 위치에서 목표 위치로 부드럽게 이동
        Vector3 currentPos = transform.position;
        if (Mathf.Abs(currentPos.z - targetZ) > 0.001f)
        {
            float nextZ = Mathf.MoveTowards(currentPos.z, targetZ, moveSpeed * Time.deltaTime);
            transform.position = new Vector3(currentPos.x, currentPos.y, nextZ);
        }
    }
}
