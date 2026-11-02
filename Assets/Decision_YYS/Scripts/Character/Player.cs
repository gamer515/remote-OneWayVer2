using UnityEngine;

[DefaultExecutionOrder(-100)]
public class Player : MonoBehaviour
{
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    private Vector3 movementTarget;
    [SerializeField] private float moveSpeed = 3.0f; // 이동 속도 (원하는 느낌에 따라 조절 가능)
    private bool isInitialized = false;
    private Animator animator;
    private Vector3 worldPosition;
    private Quaternion worldRotation;
    [System.Serializable] public class NpcFraming
    {
        [Range(0, 0.8f)] public float cropBelow;
        [Range(0.3f, 0.95f)] public float viewportFill;
        [Range(0, 1)] public float headroom;
    }
    [Header("NPC Camera Framing")]
    [SerializeField] private NpcFraming dialogueFraming = new NpcFraming { cropBelow = 0.25f, viewportFill = 0.9f, headroom = 0.05f };
    [SerializeField] private NpcFraming duelFraming = new NpcFraming { cropBelow = 0f, viewportFill = 0.9f, headroom = 0.05f };
    private Camera viewCamera;
    private Transform framedNpc;
    private bool fullBody;
    private Vector3 cameraLocalPosition;
    private Quaternion cameraLocalRotation;
    private float originalFieldOfView;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        viewCamera = GetComponentInChildren<Camera>(true);
        if (viewCamera != null) { cameraLocalPosition = viewCamera.transform.localPosition; cameraLocalRotation = viewCamera.transform.localRotation; originalFieldOfView = viewCamera.fieldOfView; }
    }

    // 초기화 및 시작 위치 설정
    public void Initialize(Vector3 startPos)
    {
        worldPosition = startPos;
        worldRotation = transform.rotation;
        ApplyWorldPose();
        movementTarget = startPos;
        isInitialized = true;
        SetWalking(false);
    }

    // 새로운 목표 Z축 지점 설정
    public void SetTargetZ(float z)
    {
        SetDestination(new Vector3(worldPosition.x, worldPosition.y, z));
    }

    public void SetDestination(Vector3 destination)
    {
        movementTarget = destination;
        Vector3 direction = destination - worldPosition; direction.y = 0;
        if (direction.sqrMagnitude > 0.0001f) SetFacing(Quaternion.LookRotation(direction, Vector3.up));
    }

    public void StopAndLookAt(Vector3 worldPosition)
    {
        movementTarget = this.worldPosition;
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
        bool isWalking = (currentPos - movementTarget).sqrMagnitude > 0.000001f;
        SetWalking(isWalking);
        if (isWalking)
        {
            worldPosition = Vector3.MoveTowards(currentPos, movementTarget, moveSpeed * Time.deltaTime);
        }
        ApplyWorldPose();
    }

    private void LateUpdate()
    {
        if (isInitialized) ApplyWorldPose();
        if (framedNpc == null && viewCamera != null && isInitialized)
            viewCamera.transform.rotation = cameraLocalRotation;
        UpdateNpcFraming();
    }

    public void FocusNpc(Transform npc, bool showFullBody)
    {
        framedNpc = npc; fullBody = showFullBody;
        UpdateNpcFraming();
    }
    public void ResetNpcFraming()
    {
        framedNpc = null;
        FaceForward();
        if (viewCamera == null) return;
        viewCamera.transform.SetLocalPositionAndRotation(cameraLocalPosition, cameraLocalRotation);
        viewCamera.fieldOfView = originalFieldOfView;
    }
    private void UpdateNpcFraming()
    {
        if (framedNpc == null || viewCamera == null) return;
        var renderers = framedNpc.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        var profile = fullBody ? duelFraming : dialogueFraming;
        float bottom = bounds.min.y + bounds.size.y * profile.cropBelow;
        float top = bounds.max.y;
        Vector3 focus = new Vector3(framedNpc.position.x, (bottom + top) * 0.5f, framedNpc.position.z);
        viewCamera.transform.localPosition = cameraLocalPosition;
        Vector3 horizontal = focus - viewCamera.transform.position; horizontal.y = 0;
        float distance = Mathf.Max(0.1f, horizontal.magnitude);
        float pitch = Mathf.Atan2(focus.y - viewCamera.transform.position.y, distance);
        Vector3 forward = horizontal.normalized * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
        if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
        viewCamera.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        float tangent = 0;
        foreach (float x in new[] { bounds.min.x, bounds.max.x })
        foreach (float y in new[] { bottom, top })
        foreach (float z in new[] { bounds.min.z, bounds.max.z })
        {
            Vector3 local = viewCamera.transform.InverseTransformPoint(new Vector3(x, y, z));
            float depth = Mathf.Max(0.1f, local.z);
            tangent = Mathf.Max(tangent, Mathf.Abs(local.y) / depth, Mathf.Abs(local.x) / (depth * viewCamera.aspect));
        }
        viewCamera.fieldOfView = Mathf.Clamp(2 * Mathf.Atan(tangent * (1 + profile.headroom) / profile.viewportFill) * Mathf.Rad2Deg, 20, 120);
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
