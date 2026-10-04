using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 본 게임/테스트 공용 대련에서 생성된 3D 손을 클릭해 마우스로 직접 움직입니다.
/// 손을 잡는 동안 커서를 잠그고 숨기며, 대련 종료 또는 Escape에서 복원합니다.
/// [참조] DuelAuthoringReferences.SpawnedPlayerHand의 부모를 이동하며 자식 Animator 자세와 분리합니다.
/// 손을 클릭하는 BoxCollider는 선택용입니다. 칼날 접촉 판정은 Sword의 DuelBladeHitbox가 담당합니다.
/// [수정] 최초 생성 배치는 유지하고, 잡은 뒤에는 월드 Z=-2.5의 X/Y 평면에서 움직입니다.
/// </summary>
public sealed class DuelMouseHandController : MonoBehaviour
{
    [Header("Mouse Control")]
    [SerializeField] private Camera inputCamera;
    [SerializeField, Min(0.1f)] private float mouseSensitivity = 1f;
    [SerializeField] private bool constrainToAuthoringArea;
    [SerializeField, Min(0f)] private float screenMargin = 12f;

    private DuelAuthoringReferences authoringReferences;
    private Transform controlledHand;
    private Plane movementPlane;
    private Vector2 virtualScreenPosition;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;
    private Vector3 appliedRecoil, recoilDirection;
    private float recoilStarted, recoilDuration, recoilDistance;
    private bool readyPoseApplied;
    public float MovementSpeedMultiplier { get; private set; } = 1f;
    public void SetMovementSpeedMultiplier(float value) => MovementSpeedMultiplier = Mathf.Clamp01(value);

    /// <summary>마우스로 조작할 손 Transform이 연결되어 있는지 여부입니다.</summary>
    public bool IsActive => controlledHand != null;
    /// <summary>손 클릭으로 커서를 잡아 마우스 이동을 손 이동으로 보내고 있는지 여부입니다.</summary>
    public bool IsCaptured { get; private set; }
    /// <summary>애니메이션 모델이 아닌 위치 이동용 손 Transform입니다.</summary>
    public Transform ControlledHand => controlledHand;
    /// <summary>방어를 풀었을 때 돌아갈 부모 회전. 손을 잡으면 생성 자세 대신 준비 자세로 갱신됩니다.</summary>
    public Quaternion RestRotation { get; private set; }
    /// <summary>손 조작에 사용하는 가상 포인터의 화면 픽셀 좌표입니다. 숨겨진 실제 커서 좌표와 다를 수 있습니다.</summary>
    public Vector2 PointerScreenPosition => virtualScreenPosition;
    /// <summary>손 잡기를 시작한 프레임 번호입니다. 손을 잡는 클릭이 곧바로 공격 입력으로 처리되지 않게 사용합니다.</summary>
    public int CaptureFrame { get; private set; } = -1;

    public void Begin(DuelAuthoringReferences references)
    {
        StopControl();
        authoringReferences = references;
        GameObject handObject = references != null ? references.SpawnedPlayerHand : null;
        if (handObject == null)
        {
            Debug.LogWarning(
                "DuelMouseHandController: 플레이어 손 프리팹이 생성되지 않았습니다.",
                this);
            return;
        }

        if (inputCamera == null) inputCamera = Camera.main;
        if (inputCamera == null)
        {
            Debug.LogError("DuelMouseHandController: 입력 카메라가 없습니다.", this);
            return;
        }

        controlledHand = handObject.transform;
        readyPoseApplied = false;
        RestRotation = controlledHand.rotation;
        Collider clickCollider = EnsureClickableCollider(handObject);
        if (clickCollider != null)
        {
            DuelHandClickProxy proxy = clickCollider.GetComponent<DuelHandClickProxy>();
            if (proxy == null) proxy = clickCollider.gameObject.AddComponent<DuelHandClickProxy>();
            proxy.Initialize(this);
        }
        // 카메라가 기울어져 있어도 손 부모는 월드 X/Y에서만 이동합니다.
        movementPlane = new Plane(Vector3.forward, controlledHand.position);
        virtualScreenPosition = inputCamera.WorldToScreenPoint(controlledHand.position);
    }

    public void StopControl()
    {
        ReleaseHand();
        MovementSpeedMultiplier = 1f;
        controlledHand = null;
        authoringReferences = null;
    }

    public void ReleaseHand()
    {
        ClearRecoil();
        if (!IsCaptured) return;
        IsCaptured = false;
        Cursor.visible = previousCursorVisible;
        Cursor.lockState = previousCursorLockMode;
    }

    public bool TryCaptureHand()
    {
        if (controlledHand == null || IsCaptured) return false;
        CaptureHand();
        return IsCaptured;
    }

    private void Update()
    {
        if (controlledHand != null) controlledHand.position -= appliedRecoil;
        appliedRecoil = Vector3.zero;
        if (controlledHand == null || inputCamera == null) return;

        if (!IsCaptured)
        {
            if (ReadLeftButtonDown(out Vector2 pointerPosition) &&
                IsPointerOverControlledHand(pointerPosition))
            {
                CaptureHand();
            }
            return;
        }

        if (ReadEscapeDown())
        {
            ReleaseHand();
            return;
        }

        Vector2 delta = ReadPointerDelta() * mouseSensitivity * MovementSpeedMultiplier;
        if (delta.sqrMagnitude <= 0f) return;

        virtualScreenPosition += delta;
        virtualScreenPosition = ClampScreenPosition(virtualScreenPosition);
        Ray ray = inputCamera.ScreenPointToRay(virtualScreenPosition);
        if (movementPlane.Raycast(ray, out float enter))
            controlledHand.position = ray.GetPoint(enter);
    }

    public void StartRecoil(Vector3 direction, float distance, float duration)
    {
        recoilDirection = Vector3.ProjectOnPlane(direction, Vector3.forward).normalized;
        if (recoilDirection.sqrMagnitude < 0.01f) recoilDirection = Vector3.down;
        recoilStarted = Time.time; recoilDistance = distance; recoilDuration = Mathf.Max(0.01f,duration);
    }
    // 마우스의 논리 위치를 바꾸지 않는 일시적인 부모 반동입니다.
    public void ApplyRecoil()
    {
        if (controlledHand == null || !IsCaptured) return;
        float t = Mathf.Clamp01((Time.time-recoilStarted)/Mathf.Max(0.01f,recoilDuration));
        appliedRecoil = recoilDirection * (Mathf.Sin(t*Mathf.PI)*recoilDistance);
        controlledHand.position += appliedRecoil;
    }
    private void ClearRecoil()
    {
        if (controlledHand != null) controlledHand.position -= appliedRecoil;
        appliedRecoil = Vector3.zero; recoilDistance = 0f;
    }

    private void CaptureHand()
    {
        if (controlledHand == null || IsCaptured) return;
        if (!readyPoseApplied)
        {
            authoringReferences?.ApplyPlayerReadyPose();
            PlaceHeldHandOnCombatPlane();
            RestRotation = controlledHand.rotation;
            readyPoseApplied = true;
        }
        // 들어 올린 자세의 Z 평면과 화면 좌표를 함께 갱신해야 첫 이동에서 손이 튀지 않습니다.
        movementPlane = new Plane(Vector3.forward, controlledHand.position);
        virtualScreenPosition = inputCamera.WorldToScreenPoint(controlledHand.position);
        previousCursorVisible = Cursor.visible;
        previousCursorLockMode = Cursor.lockState;
        IsCaptured = true;
        CaptureFrame = Time.frameCount;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void PlaceHeldHandOnCombatPlane()
    {
        if (inputCamera == null) return;
        // 최초 생성은 그대로 두고, 처음 잡은 뒤에만 요청한 월드 깊이를 적용합니다.
        const float heldZ = -2.5f;
        Vector2 screen = inputCamera.WorldToScreenPoint(controlledHand.position);
        Ray ray = inputCamera.ScreenPointToRay(screen);
        var heldPlane = new Plane(Vector3.forward, new Vector3(0f, 0f, heldZ));
        // 카메라가 기울어져 있어도 화면 X/Y는 유지하고 깊이만 새 평면에 맞춥니다.
        if (heldPlane.Raycast(ray, out float distance)) controlledHand.position = ray.GetPoint(distance);
    }

    private bool IsPointerOverControlledHand(Vector2 screenPosition)
    {
        Ray ray = inputCamera.ScreenPointToRay(screenPosition);
        foreach (RaycastHit hit in Physics.RaycastAll(ray, 1000f, inputCamera.cullingMask))
        {
            if (hit.transform == controlledHand || hit.transform.IsChildOf(controlledHand))
                return true;
        }
        return false;
    }

    private Vector2 ClampScreenPosition(Vector2 position)
    {
        RectTransform moveArea = authoringReferences != null
            ? authoringReferences.PlayerHandMoveArea
            : null;
        if (constrainToAuthoringArea && moveArea != null)
        {
            Vector3[] corners = new Vector3[4];
            moveArea.GetWorldCorners(corners);
            return new Vector2(
                Mathf.Clamp(position.x, corners[0].x, corners[2].x),
                Mathf.Clamp(position.y, corners[0].y, corners[2].y));
        }

        return new Vector2(
            Mathf.Clamp(position.x, screenMargin, Screen.width - screenMargin),
            Mathf.Clamp(position.y, screenMargin, Screen.height - screenMargin));
    }

    private static Collider EnsureClickableCollider(GameObject handObject)
    {
        // 검 판정 Trigger는 손 클릭용 박스와 분리합니다.
        Collider existing = handObject.GetComponent<Collider>();
        if (existing != null) return existing;

        Renderer[] renderers = handObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;

        Transform root = handObject.transform;
        Bounds worldBounds = default;
        bool hasMesh = false;
        foreach (var renderer in renderers)
        {
            // 잔상/이펙트의 빈 Bounds가 손 클릭 박스를 원점까지 늘리지 않도록 제외합니다.
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var meshFilter) &&
                (meshFilter.sharedMesh == null || meshFilter.sharedMesh.vertexCount == 0)) continue;
            if (!hasMesh) { worldBounds = renderer.bounds; hasMesh = true; }
            else worldBounds.Encapsulate(renderer.bounds);
        }
        if (!hasMesh) return null;

        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        Bounds localBounds = new Bounds(root.InverseTransformPoint(min), Vector3.zero);
        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
        {
            Vector3 corner = new Vector3(
                x == 0 ? min.x : max.x,
                y == 0 ? min.y : max.y,
                z == 0 ? min.z : max.z);
            localBounds.Encapsulate(root.InverseTransformPoint(corner));
        }

        BoxCollider collider = handObject.AddComponent<BoxCollider>();
        collider.center = localBounds.center;
        collider.size = localBounds.size;
        return collider;
    }

    private static bool ReadLeftButtonDown(out Vector2 position)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
        {
            position = Vector2.zero;
            return false;
        }
        position = Mouse.current.position.ReadValue();
        return Mouse.current.leftButton.wasPressedThisFrame;
#else
        position = Input.mousePosition;
        return Input.GetMouseButtonDown(0);
#endif
    }

    private static Vector2 ReadPointerDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
    }

    private static bool ReadEscapeDown()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private void OnDisable() => StopControl();
}

internal sealed class DuelHandClickProxy : MonoBehaviour
{
    private DuelMouseHandController owner;

    public void Initialize(DuelMouseHandController controller) => owner = controller;

    private void OnMouseDown()
    {
        owner?.TryCaptureHand();
    }
}
