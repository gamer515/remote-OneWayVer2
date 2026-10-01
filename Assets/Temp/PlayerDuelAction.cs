using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerDuelAction : MonoBehaviour
{
    [SerializeField] private DrawGizmoAreas gizmoAreas;
    [SerializeField] private DuelMiniGameBridge duelBridge;
    [SerializeField] private DuelMouseHandController mouseHandController;

    private void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (gizmoAreas == null)
            gizmoAreas = GetComponent<DrawGizmoAreas>();
        if (duelBridge == null)
            duelBridge = gizmoAreas != null
                ? gizmoAreas.GetComponent<DuelMiniGameBridge>()
                : GetComponent<DuelMiniGameBridge>();
        if (mouseHandController == null)
            mouseHandController = gizmoAreas != null
                ? gizmoAreas.GetComponent<DuelMouseHandController>()
                : GetComponent<DuelMouseHandController>();
    }

    private void LateUpdate()
    {
        // 실행 중 스크립트 재컴파일 후에도 비어 있는 참조를 복구합니다.
        ResolveReferences();
        if (gizmoAreas == null || !gizmoAreas.isActiveAndEnabled) return;
        if (duelBridge == null || !duelBridge.IsTestRunning) return;
        if (mouseHandController == null || !mouseHandController.isActiveAndEnabled ||
            !mouseHandController.IsCaptured) return;
        // 손을 잡은 첫 클릭은 공격이나 방어로 처리하지 않습니다.
        if (mouseHandController.CaptureFrame == Time.frameCount) return;

        #if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        #else
        if (!Input.GetMouseButtonDown(0)) return;
        #endif

        // 커서는 중앙에 잠겨 있으므로 손 이동에 쓰는 가상 화면 좌표로 판정합니다.
        Vector2 mousePosition = mouseHandController.PointerScreenPosition;

        // 클릭 시점의 좌표로 판정하도록 화면 영역을 갱신합니다.
        gizmoAreas.RefreshUiAreas();

        // 영역이 겹치면 나이트의 방어를 우선하며 로그는 한 번만 출력합니다.
        if (gizmoAreas.HasKnightUiAreas)
        {
            for (int i = 0; i < 4; i++)
            {
                if (!gizmoAreas.GetKnightUiArea(i).Contains(mousePosition)) continue;
                Debug.Log("방어", this);
                return;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (!gizmoAreas.GetNpcUiArea(i).Contains(mousePosition)) continue;
            Debug.Log("공격", this);
            return;
        }
    }
}
