using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 테스트 대련에서 생성된 3D 손을 클릭해 마우스로 직접 움직입니다.
/// 손을 잡는 동안 커서를 잠그고 숨기며, 대련 종료 또는 Escape에서 복원합니다.
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

    public bool IsActive => controlledHand != null;
    public bool IsCaptured { get; private set; }
    public Transform ControlledHand => controlledHand;
    public Vector2 PointerScreenPosition => virtualScreenPosition;
    public int CaptureFrame { get; private set; } = -1;

    public void Begin(DuelAuthoringReferences references)
    {
        StopControl();
        authoringReferences = references;
        GameObject handObject = references != null ? references.SpawnedPlayerHand : null;
        if (handObject == null)
        {
            Debug.LogWarning(
                "DuelMouseHandController: 테스트용 플레이어 손 프리팹이 생성되지 않았습니다.",
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
        Collider clickCollider = EnsureClickableCollider(handObject);
        if (clickCollider != null)
        {
            DuelHandClickProxy proxy = clickCollider.GetComponent<DuelHandClickProxy>();
            if (proxy == null) proxy = clickCollider.gameObject.AddComponent<DuelHandClickProxy>();
            proxy.Initialize(this);
        }
        movementPlane = new Plane(inputCamera.transform.forward, controlledHand.position);
        virtualScreenPosition = inputCamera.WorldToScreenPoint(controlledHand.position);
    }

    public void StopControl()
    {
        ReleaseHand();
        controlledHand = null;
        authoringReferences = null;
    }

    public void ReleaseHand()
    {
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

        Vector2 delta = ReadPointerDelta() * mouseSensitivity;
        if (delta.sqrMagnitude <= 0f) return;

        virtualScreenPosition += delta;
        virtualScreenPosition = ClampScreenPosition(virtualScreenPosition);
        Ray ray = inputCamera.ScreenPointToRay(virtualScreenPosition);
        if (movementPlane.Raycast(ray, out float enter))
            controlledHand.position = ray.GetPoint(enter);
    }

    private void CaptureHand()
    {
        if (controlledHand == null || IsCaptured) return;
        virtualScreenPosition = inputCamera.WorldToScreenPoint(controlledHand.position);
        previousCursorVisible = Cursor.visible;
        previousCursorLockMode = Cursor.lockState;
        IsCaptured = true;
        CaptureFrame = Time.frameCount;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
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
        Collider existing = handObject.GetComponentInChildren<Collider>(true);
        if (existing != null) return existing;

        Renderer[] renderers = handObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;

        Transform root = handObject.transform;
        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            worldBounds.Encapsulate(renderers[i].bounds);

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
