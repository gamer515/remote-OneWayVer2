using UnityEngine;

/// <summary>
/// Road View 카메라의 초기 위치와 플레이 중 Z축 추적을 관리합니다.
/// </summary>
public class RoadViewCameraController : MonoBehaviour
{
    [Header("Initial Position")]
    [SerializeField] private Vector3 initialCameraPosition =
        new Vector3(-200f, 18f, 10.5f);
    [SerializeField] private float playerFollowOffsetZ = 0.5f;

    [Header("Follow Settings")]
    [SerializeField, Min(0f)] private float maxDistance = 20f;
    [SerializeField, Min(0.01f)] private float smoothTime = 2f;

    private Transform playerTransform;
    private float followVelocity;

    private void Awake()
    {
        // 씬에 저장된 과거 카메라 좌표가 첫 프레임에 노출되지 않도록 항상 기준 위치에서 시작합니다.
        transform.position = initialCameraPosition;
    }

    public void Initialize(Transform playerTransform, bool useInitialPosition)
    {
        if (playerTransform == null)
        {
            Debug.LogError("Road View 카메라에 연결할 Player Transform이 없습니다.", this);
            return;
        }

        this.playerTransform = playerTransform;
        followVelocity = 0f;

        if (useInitialPosition)
        {
            transform.position = initialCameraPosition;
            return;
        }

        // 저장 위치에서 복원할 때는 추적 이동을 보여주지 않고 플레이어 옆으로 즉시 배치합니다.
        SnapToPlayer();
    }

    private void LateUpdate()
    {
        if (playerTransform == null)
            return;

        // X와 Y 거리는 Road View 구도 자체이므로 추적 판단에는 Z축 거리만 사용합니다.
        float signedDistanceZ = playerTransform.position.z - transform.position.z;
        if (Mathf.Abs(signedDistanceZ) <= maxDistance)
            return;

        float targetZ = playerTransform.position.z -
            Mathf.Sign(signedDistanceZ) * maxDistance;

        float nextZ = Mathf.SmoothDamp(
            transform.position.z,
            targetZ,
            ref followVelocity,
            smoothTime);

        transform.position = new Vector3(
            transform.position.x,
            transform.position.y,
            nextZ);
    }

    private void SnapToPlayer()
    {
        transform.position = new Vector3(
            initialCameraPosition.x,
            initialCameraPosition.y,
            playerTransform.position.z + playerFollowOffsetZ);
    }
}
