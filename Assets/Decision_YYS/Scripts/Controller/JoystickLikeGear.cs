using UnityEngine;
using UnityEngine.UI;

public class JoystickLikeGear : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RectTransform joystick_Button;
    [SerializeField] private RectTransform pivot;

    [Header("3D Object References")]
    [SerializeField] private Transform gear3D;
    [Tooltip("기어가 최대로 기울어질 각도 (단위: 도)")]
    [SerializeField] private float maxTiltAngle = 30f;
    [SerializeField] private Camera mainCamera;

    [Header("Interaction Area Settings (Screen)")]
    [Tooltip("체크하면 아래 지정된 화면 영역을 기준으로 동작합니다.")]
    [SerializeField] private bool useScreenArea = true;
    [Tooltip("기준 해상도(1920x1080) 기준 인식 영역의 좌하단 시작점 (1440, 0 권장)")]
    [SerializeField] private Vector2 origin = new Vector2(1440f, 0f); 
    [Tooltip("마우스 인식 영역 크기 (480, 270 권장)")]
    [SerializeField] private Vector2 areaSize = new Vector2(480f, 270f);
    [Tooltip("기준 해상도")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

    [Header("Debug")]
    [Tooltip("체크하면 Scene 뷰와 Game 뷰에 마우스 인식 영역이 빨간색 선으로 표시됩니다.")]
    [SerializeField] private bool showDebugArea = true;

    [Header("Follow Settings")]
    [SerializeField] private float horizontalRange = 200f;
    [SerializeField] private float verticalRange = 150f;
    [SerializeField] private float slotWidth = 60f;
    [SerializeField] private float smoothTime = 0.08f;     

    //이벤트 함수로 decisionManager에 있는 함수를 넘겨 줄까?
    [SerializeField] DecisionManager decisionManager;
    [SerializeField] private DynamicFaceController faceController;

    private Vector2 targetPosition;
    private Vector2 currentVelocity;
    private int currentGearSlot = 0; 

    //public int CurrentGear => currentGearSlot;
    private void Update()
    {
        if (joystick_Button == null || pivot == null) return;

        HandleSelectionInput();
        CheckGearState();
        HandleMouseProximity();
    }

    private void LateUpdate()
    {
        if (gear3D != null)
        {
            // 매 프레임 UI 위치에 맞춰 3D 기어의 회전값을 업데이트합니다.
            Update3DGearRotation();
        }
    }

    private void FixedUpdate()
    {
        MoveGearSmoothly();
    }

    public int GetCurrentGearDirectly()
    {
        int gear = 0;
        Vector2 pos = joystick_Button.anchoredPosition;

        float xThreshold = horizontalRange * 0.75f;
        float yThreshold = verticalRange * 0.75f;

        if (pos.x < -xThreshold && pos.y > yThreshold) gear = 1;
        else if (pos.x < -xThreshold && pos.y < -yThreshold) gear = 2;
        else if (pos.x > xThreshold && pos.y > yThreshold) gear = 3;
        else if (pos.x > xThreshold && pos.y < -yThreshold) gear = 4;

        return gear;
    }

    // 3D 기어의 초기 회전값 저장
    private Quaternion gear3DOriginRot;

    private void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (gear3D != null)
        {
            // 시작할 때의 로컬 회전 상태를 저장해둡니다.
            gear3DOriginRot = gear3D.localRotation;
        }
    }



    private void OnDrawGizmos()
    {
        if (!useScreenArea || !showDebugArea) return;

        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null) return;

        Rect scaledRect = GetScaledInteractionRect();
        float distance = 1f; 

        Vector3 bottomLeft = cam.ScreenToWorldPoint(new Vector3(scaledRect.xMin, scaledRect.yMin, distance));
        Vector3 topLeft = cam.ScreenToWorldPoint(new Vector3(scaledRect.xMin, scaledRect.yMax, distance));
        Vector3 topRight = cam.ScreenToWorldPoint(new Vector3(scaledRect.xMax, scaledRect.yMax, distance));
        Vector3 bottomRight = cam.ScreenToWorldPoint(new Vector3(scaledRect.xMax, scaledRect.yMin, distance));

        Gizmos.color = new Color(1f, 0f, 0f, 0.8f);
        Gizmos.DrawLine(bottomLeft, topLeft);
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
        
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawLine(bottomLeft, topRight);
        Gizmos.DrawLine(topLeft, bottomRight);
    }


    private void HandleMouseProximity()
    {
        Rect scaledRect;
        if (!IsMouseValid(out scaledRect))
        {
            targetPosition = Vector2.zero;
            return;
        }

        // [핵심] 인식 영역의 중심으로부터 마우스가 얼마나 떨어져 있는지 계산 (상대 좌표 조작)
        Vector2 areaCenterScreen = scaledRect.center;
        Vector2 mouseDelta = (Vector2)Input.mousePosition - areaCenterScreen;

        // 화면 픽셀 단위를 UI 단위로 환산
        float uiScaleX = referenceResolution.x / Screen.width;
        float uiScaleY = referenceResolution.y / Screen.height;
        Vector2 uiDelta = new Vector2(mouseDelta.x * uiScaleX, mouseDelta.y * uiScaleY);

        // 조이스틱 이동 한계치 내로 제한 및 기어 슬롯 로직 적용
        float tx = Mathf.Clamp(uiDelta.x, -horizontalRange, horizontalRange);
        float ty = 0;

        float edgeThreshold = horizontalRange - slotWidth;
        
        if (Mathf.Abs(tx) > edgeThreshold)
        {
            ty = Mathf.Clamp(uiDelta.y, -verticalRange, verticalRange);
            if (Mathf.Abs(ty) > verticalRange * 0.4f)
            {
                tx = (tx > 0) ? horizontalRange : -horizontalRange;
            }
        }
        else
        {
            ty = 0;
        }

        targetPosition = new Vector2(tx, ty);
    }

    private void MoveGearSmoothly()
    {
        joystick_Button.anchoredPosition = Vector2.SmoothDamp(
            joystick_Button.anchoredPosition, 
            targetPosition, 
            ref currentVelocity, 
            smoothTime
        );

        // [추가] 얼굴 표정 컨트롤러에 현재 기어 위치 비율 전달
        if (faceController != null)
        {
            float xRatio = joystick_Button.anchoredPosition.x / horizontalRange;
            float yRatio = joystick_Button.anchoredPosition.y / verticalRange;
            faceController.SetGearRatio(xRatio, yRatio);
        }
    }

    private void Update3DGearRotation()
    {
        // UI 조이스틱 위치 비율(-1 ~ 1) 계산
        float xRatio = joystick_Button.anchoredPosition.x / horizontalRange;
        float yRatio = joystick_Button.anchoredPosition.y / verticalRange;

        // [수정] 좌우 회전 방향 반전: 마우스 이동 방향과 기어 기울기 방향을 일치시킵니다.
        float rotX = yRatio * maxTiltAngle; 
        float rotZ = -xRatio * maxTiltAngle; 

        // 초기 회전값에 계산된 회전 오프셋을 쿼터니언 곱셈으로 적용
        gear3D.localRotation = gear3DOriginRot * Quaternion.Euler(rotX, 0, rotZ);
    }

    private void CheckGearState()
    {
        int gear = 0;
        Vector2 pos = joystick_Button.anchoredPosition;

        float xThreshold = horizontalRange * 0.75f;
        float yThreshold = verticalRange * 0.75f;

        if (pos.x < -xThreshold && pos.y > yThreshold) gear = 1;
        else if (pos.x < -xThreshold && pos.y < -yThreshold) gear = 2;
        else if (pos.x > xThreshold && pos.y > yThreshold) gear = 3;
        else if (pos.x > xThreshold && pos.y < -yThreshold) gear = 4;

        if (gear != currentGearSlot)
        {
            currentGearSlot = gear;
            // 플레이어 시점이 아닐 때만 텍스트를 업데이트합니다.
            if (decisionManager != null && !decisionManager.IsPlayerViewActive)
            {
                decisionManager.ShowOptionText(gear);
            }
        }
    }

    private void HandleSelectionInput()
    {
        // [수정] 스페이스바 대신 마우스 왼쪽 클릭(0) 감지
        // 마우스가 조이스틱 인식 영역 내에 있을 때만 동작하도록 제한
        Rect scaledRect;
        if (Input.GetMouseButtonDown(0) && IsMouseValid(out scaledRect))
        {
            if (currentGearSlot != 0)
            {
                // 이벤트로 해당 메서드 가져오기?
                decisionManager.ConfirmChoice(currentGearSlot);
            }
            else
            {
                decisionManager.OnScreenClicked();
            }
        }
    }

    private bool IsMouseValid(out Rect scaledRect)
    {
        scaledRect = GetScaledInteractionRect();
        if (useScreenArea)
        {
           return scaledRect.Contains(Input.mousePosition);
        }
        return false;
    }

    // 현재 화면 비율에 맞춰 조정된 실제 인식 영역(Rect)을 계산합니다.
    private Rect GetScaledInteractionRect()
    {
        float scaleX = (float)Screen.width / referenceResolution.x;
        float scaleY = (float)Screen.height / referenceResolution.y;

        return new Rect(
            origin.x * scaleX,
            origin.y * scaleY,
            areaSize.x * scaleX,
            areaSize.y * scaleY
        );
    }
}
