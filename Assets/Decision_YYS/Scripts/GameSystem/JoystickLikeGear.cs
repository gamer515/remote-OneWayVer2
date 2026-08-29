using System;
using UnityEngine;

/// <summary>
/// 3D 기어 손잡이를 드래그하여 네 종류의 코인 중 하나를 선택합니다.
/// 이야기 진행과 선택지 확정은 담당하지 않습니다.
/// </summary>
public class JoystickLikeGear : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RectTransform joystick_Button;
    [SerializeField] private RectTransform pivot;

    [Header("3D Gear References")]
    [SerializeField] private Transform gear3D;
    [Tooltip("마우스로 눌러야 기어가 움직이는 가장 위쪽 Sphere의 Collider입니다.")]
    [SerializeField] private Collider gearHandleCollider;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float maxTiltAngle = 30f;

    [Header("Drag Mapping Area")]
    [SerializeField] private bool useScreenArea = true;
    [SerializeField] private Vector2 origin = new Vector2(1440f, 0f);
    [SerializeField] private Vector2 areaSize = new Vector2(480f, 270f);
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

    [Header("Gear Movement")]
    [SerializeField] private float horizontalRange = 200f;
    [SerializeField] private float verticalRange = 150f;
    [SerializeField] private float smoothTime = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool showDebugArea = true;
    [SerializeField] private DynamicFaceController faceController;

    /// <summary>0~3 범위의 코인 종류 인덱스를 전달합니다.</summary>
    public event Action<int> OnCoinTypeChanged;

    public int SelectedCoinIndex => currentGearSlot - 1;

    private Vector2 targetPosition;
    private Vector2 currentVelocity;
    private Quaternion gear3DOriginRot;
    private int currentGearSlot;
    private bool isDragging;

    private void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (gear3D != null)
        {
            gear3DOriginRot = gear3D.localRotation;
            ResolveHandleCollider();
        }
    }

    private void Update()
    {
        if (joystick_Button == null || pivot == null)
            return;

        HandleDragInput();
    }

    private void FixedUpdate()
    {
        if (joystick_Button == null)
            return;

        joystick_Button.anchoredPosition = Vector2.SmoothDamp(
            joystick_Button.anchoredPosition,
            targetPosition,
            ref currentVelocity,
            smoothTime);

        if (faceController != null)
        {
            float xRatio = joystick_Button.anchoredPosition.x / horizontalRange;
            float yRatio = joystick_Button.anchoredPosition.y / verticalRange;
            faceController.SetGearRatio(xRatio, yRatio);
        }
    }

    private void LateUpdate()
    {
        if (gear3D == null || joystick_Button == null)
            return;

        float xRatio = joystick_Button.anchoredPosition.x / horizontalRange;
        float yRatio = joystick_Button.anchoredPosition.y / verticalRange;
        gear3D.localRotation = gear3DOriginRot * Quaternion.Euler(
            yRatio * maxTiltAngle,
            0f,
            -xRatio * maxTiltAngle);
    }

    private void HandleDragInput()
    {
        if (Input.GetMouseButtonDown(0) && IsHandleClicked())
            isDragging = true;

        // 손잡이에서 드래그를 시작했다면 인식 영역 밖으로 나가도 조작을 계속합니다.
        if (isDragging && Input.GetMouseButton(0))
            targetPosition = CalculateDragPosition(Input.mousePosition);

        if (isDragging && Input.GetMouseButtonUp(0))
        {
            isDragging = false;
            SnapToNearestSlot();
        }
    }

    private bool IsHandleClicked()
    {
        if (mainCamera == null || gearHandleCollider == null)
            return false;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == gearHandleCollider)
                return true;
        }

        return false;
    }

    private Vector2 CalculateDragPosition(Vector2 mousePosition)
    {
        Rect interactionRect = GetScaledInteractionRect();
        Vector2 mouseDelta = mousePosition - interactionRect.center;

        float uiScaleX = referenceResolution.x / Screen.width;
        float uiScaleY = referenceResolution.y / Screen.height;
        float x = Mathf.Clamp(mouseDelta.x * uiScaleX, -horizontalRange, horizontalRange);
        float y = Mathf.Clamp(mouseDelta.y * uiScaleY, -verticalRange, verticalRange);

        return new Vector2(x, y);
    }

    private void SnapToNearestSlot()
    {
        bool isRight = targetPosition.x > 0f;
        bool isTop = targetPosition.y >= 0f;

        if (!isRight && isTop)
            SetSlot(1, new Vector2(-horizontalRange, verticalRange));
        else if (!isRight)
            SetSlot(2, new Vector2(-horizontalRange, -verticalRange));
        else if (isTop)
            SetSlot(3, new Vector2(horizontalRange, verticalRange));
        else
            SetSlot(4, new Vector2(horizontalRange, -verticalRange));
    }

    private void SetSlot(int slot, Vector2 snappedPosition)
    {
        targetPosition = snappedPosition;

        if (currentGearSlot == slot)
            return;

        currentGearSlot = slot;
        OnCoinTypeChanged?.Invoke(SelectedCoinIndex);
    }

    private void ResolveHandleCollider()
    {
        if (gearHandleCollider != null)
            return;

        // Inspector 연결이 없어도 기어 최상단의 자식 없는 Sphere를 손잡이로 자동 선택합니다.
        SphereCollider[] candidates = gear3D.GetComponentsInChildren<SphereCollider>(true);
        float farthestDistance = -1f;

        for (int i = 0; i < candidates.Length; i++)
        {
            SphereCollider candidate = candidates[i];
            if (candidate.transform.childCount != 0)
                continue;

            float distance = (candidate.transform.position - gear3D.position).sqrMagnitude;
            if (distance <= farthestDistance)
                continue;

            farthestDistance = distance;
            gearHandleCollider = candidate;
        }

        if (gearHandleCollider == null)
            Debug.LogError("기어 손잡이 SphereCollider를 찾지 못했습니다.", this);
    }

    private Rect GetScaledInteractionRect()
    {
        if (!useScreenArea)
            return new Rect(0f, 0f, Screen.width, Screen.height);

        float scaleX = (float)Screen.width / referenceResolution.x;
        float scaleY = (float)Screen.height / referenceResolution.y;
        return new Rect(
            origin.x * scaleX,
            origin.y * scaleY,
            areaSize.x * scaleX,
            areaSize.y * scaleY);
    }

    private void OnDrawGizmos()
    {
        if (!useScreenArea || !showDebugArea)
            return;

        Camera cameraToUse = mainCamera != null ? mainCamera : Camera.main;
        if (cameraToUse == null)
            return;

        Rect rect = GetScaledInteractionRect();
        const float distance = 1f;
        Vector3 bottomLeft = cameraToUse.ScreenToWorldPoint(new Vector3(rect.xMin, rect.yMin, distance));
        Vector3 topLeft = cameraToUse.ScreenToWorldPoint(new Vector3(rect.xMin, rect.yMax, distance));
        Vector3 topRight = cameraToUse.ScreenToWorldPoint(new Vector3(rect.xMax, rect.yMax, distance));
        Vector3 bottomRight = cameraToUse.ScreenToWorldPoint(new Vector3(rect.xMax, rect.yMin, distance));

        Gizmos.color = new Color(1f, 0f, 0f, 0.8f);
        Gizmos.DrawLine(bottomLeft, topLeft);
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
    }
}
