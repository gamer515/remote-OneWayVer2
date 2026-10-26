using UnityEngine;

[DefaultExecutionOrder(-100)]
public class Player : MonoBehaviour
{
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    private float targetZ;
    [SerializeField] private float moveSpeed = 3.0f; // 이동 속도 (원하는 느낌에 따라 조절 가능)
    private bool isInitialized = false;
    private Animator animator;
    private Vector3 worldPosition;
    private Quaternion worldRotation;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
    }

    // 초기화 및 시작 위치 설정
    public void Initialize(Vector3 startPos)
    {
        worldPosition = startPos;
        worldRotation = transform.rotation;
        ApplyWorldPose();
        targetZ = startPos.z;
        isInitialized = true;
        SetWalking(false);
    }

    // 새로운 목표 Z축 지점 설정
    public void SetTargetZ(float z)
    {
        targetZ = z;
    }

    public void StopAndLookAt(Vector3 worldPosition)
    {
        targetZ = this.worldPosition.z;
        SetWalking(false);
        Vector3 direction = worldPosition - this.worldPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            SetFacing(Quaternion.LookRotation(direction.normalized, Vector3.up));
    }

    public void FaceForward()
    {
        SetFacing(Quaternion.LookRotation(Vector3.forward, Vector3.up));
    }

    public void SetFacing(Quaternion rotation)
    {
        worldRotation = rotation;
        transform.rotation = rotation;
    }

    private void Update()
    {
        if (!isInitialized) return;

        // 현재 위치에서 목표 위치로 부드럽게 이동
        Vector3 currentPos = worldPosition;
        bool isWalking = Mathf.Abs(currentPos.z - targetZ) > 0.001f;
        SetWalking(isWalking);
        if (isWalking)
        {
            float nextZ = Mathf.MoveTowards(currentPos.z, targetZ, moveSpeed * Time.deltaTime);
            worldPosition = new Vector3(currentPos.x, currentPos.y, nextZ);
        }
        ApplyWorldPose();
    }

    private void LateUpdate()
    {
        if (isInitialized) ApplyWorldPose();
    }

    // The shared Animator contains root Transform bindings (including StandUP).
    // Write Defaults can overwrite the root even with Apply Root Motion disabled.
    // Keep locomotion authoritative, after animation and before camera LateUpdate.
    private void ApplyWorldPose() => transform.SetPositionAndRotation(worldPosition, worldRotation);

    private void SetWalking(bool walking)
    {
        if (animator != null)
            animator.SetBool(IsWalkingHash, walking);
    }
}
