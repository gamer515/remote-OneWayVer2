using UnityEngine;

/// <summary>
/// [역할] 플레이어의 NPC 공격 제스처 영역과 기사 주변 제작용 영역을 화면 좌표로 제공하고 Gizmo로 보여줍니다.
/// 영역은 오른클릭 방어 입력을 제한하지 않습니다. 현재 NPC 자동 공격/접촉 판정은 없으며 제작용 표시는 유지합니다.
/// [참조] TutorialMiniGameController.KnightSpawned → 복제 기사의 Knight_Center를 기준점으로 사용합니다.
/// 영역 배치를 바꾸려면 UpdateAreaRects, 색/표시는 Gizmo 설정을 수정합니다.
/// </summary>
public class DrawGizmoAreas : MonoBehaviour
{
    [Header("Knight Reference")]
    [SerializeField] private TutorialMiniGameController miniGameController;
    private GameObject knightInstance;
    private Transform knightCenter;
    Vector3 knightVector;

    [Header("Gizmo Colors")]
    [SerializeField] private Color gizmoGColor = Color.green;
    [SerializeField] private Color gizmoRColor = Color.red;
    [SerializeField] private Color gizmoBColor = Color.blue;
    [SerializeField] private Color gizmoYColor = Color.yellow;

    [Header("Gizmo Areas")]
    [SerializeField] private Vector2 npcGizmoPoint = new Vector2(-1, -1);
    [SerializeField] private float pointOffset = 100f;
    [SerializeField] private float pointSize = 200f;

    [Header("OnSet")]
    [SerializeField] private bool drawGizmoOnSet = true;

    private int drawGizmoCount = 4;

    [Header("Screen Rects (pixels, bottom-left origin)")]
    [SerializeField] private Rect[] uiAreas = new Rect[4];
    [SerializeField] private Rect[] knightUiAreas = new Rect[4];

    public Rect GetUiArea(int index) => uiAreas[index];
    public Rect GetNpcUiArea(int index) => uiAreas[index];
    public Rect GetKnightUiArea(int index) => knightUiAreas[index];
    public void RefreshUiAreas() => UpdateAreaRects();
    public bool HasKnightUiAreas { get; private set; }
    private const float KnightAreaSize = 1.5f;

    private void OnEnable()
    {
        if (miniGameController == null) return;

        miniGameController.KnightSpawned += HandleKnightSpawned;
        HandleKnightSpawned(miniGameController.CurrentKnightTarget);
    }

    private void OnDisable()
    {
        if (miniGameController != null)
            miniGameController.KnightSpawned -= HandleKnightSpawned;

        knightInstance = null;
        knightCenter = null;
        System.Array.Clear(knightUiAreas, 0, knightUiAreas.Length);
    }

    private void HandleKnightSpawned(GameObject instance)
    {
        knightInstance = instance;
        knightCenter = instance != null
            ? instance.transform.Find("Knight_Center")
            : null;
        UpdateAreaRects();
    }

    private void LateUpdate()
    {
        // Gizmos 표시를 꺼도 게임 중 판정에 사용할 화면 영역을 갱신합니다.
        UpdateAreaRects();
    }

    private void UpdateAreaRects()
    {
        if (uiAreas == null || uiAreas.Length != 4) uiAreas = new Rect[4];
        if (knightUiAreas == null || knightUiAreas.Length != 4) knightUiAreas = new Rect[4];
        npcGizmoPoint = new Vector2(628f, 675f);
        Camera cam = Camera.main;
        HasKnightUiAreas = cam != null && knightCenter != null &&
            cam.WorldToScreenPoint(knightCenter.position).z > cam.nearClipPlane;
        if (knightCenter != null) knightVector = knightCenter.position;

        for (int i = 0; i < drawGizmoCount; i++)
        {
            int signX = i % 2 == 0 ? 1 : -1;
            int signY = i < 2 ? 1 : -1;
            Vector2 center = npcGizmoPoint + new Vector2(signX, signY) * pointOffset;
            uiAreas[i] = CreateScreenRect(center, Vector2.one * pointSize);

            knightUiAreas[i] = default;
            if (!HasKnightUiAreas) continue;

            Vector3 offset = new Vector3(signX, signY, 0f) * KnightAreaSize * 0.5f;
            Vector3 halfSize = new Vector3(KnightAreaSize, KnightAreaSize, 0f) * 0.5f;
            Quaternion rotation = cam.transform.rotation;
            Vector3 min = cam.WorldToScreenPoint(knightVector + rotation * (offset - halfSize));
            Vector3 max = cam.WorldToScreenPoint(knightVector + rotation * (offset + halfSize));
            Vector2 knightScreenCenter = new Vector2(
                (min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
            Vector2 knightScreenSize = new Vector2(
                Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
            knightUiAreas[i] = CreateScreenRect(knightScreenCenter, knightScreenSize);
        }
    }

    // 두 영역 모두 화면 픽셀의 중심과 크기로 동일한 Rect 형식을 만듭니다.
    private static Rect CreateScreenRect(Vector2 center, Vector2 size)
    {
        return new Rect(center - size * 0.5f, size);
    }


    private void OnDrawGizmos()
    {
        UpdateAreaRects();
        if (!drawGizmoOnSet) return;
        if (Camera.main == null) return;

        npcGizmoPoint = new Vector2(628f, 675f);

        for (int i = 0; i < drawGizmoCount; i++)
        {
            switch (i)
            {
                case 0:
                    Gizmos.color = gizmoGColor;
                    break;
                case 1:
                    Gizmos.color = gizmoRColor;
                    break;
                case 2:
                    Gizmos.color = gizmoBColor;
                    break;
                case 3:
                    Gizmos.color = gizmoYColor;
                    break;
            }

            int signX = (i % 2 == 0) ? 1 : -1;
            int signY = (i < 2) ? 1 : -1;

            Vector3 position = new Vector3(
                            npcGizmoPoint.x + signX * pointOffset,
                            npcGizmoPoint.y + signY * pointOffset,
                            10f);

            Vector3 worldPoint = Camera.main.ScreenToWorldPoint(position);

            Vector3 rightPoint = Camera.main.ScreenToWorldPoint(
                position + new Vector3(pointSize, 0f, 0f));

            Vector3 upPoint = Camera.main.ScreenToWorldPoint(
                position + new Vector3(0f, pointSize, 0f));

            float width = Vector3.Distance(worldPoint, rightPoint);
            float height = Vector3.Distance(worldPoint, upPoint);

            Matrix4x4 previousMatrix = Gizmos.matrix;

            Gizmos.matrix = Matrix4x4.TRS(
                worldPoint,
                Camera.main.transform.rotation,
                Vector3.one);

            Gizmos.DrawCube(Vector3.zero, new Vector3(width, height, 0.01f));

            Gizmos.matrix = previousMatrix;
        }

        if (HasKnightUiAreas)
        {
            for (int i = 0; i < drawGizmoCount; i++)
            {
                switch (i)
                {
                    case 0:
                        Gizmos.color = new Color(1f, 0.45f, 0f, 0.7f); // 주황
                        break;
                    case 1:
                        Gizmos.color = new Color(0.65f, 0.2f, 1f, 0.7f); // 보라
                        break;
                    case 2:
                        Gizmos.color = new Color(0f, 0.85f, 0.9f, 0.7f); // 청록
                        break;
                    case 3:
                        Gizmos.color = new Color(1f, 0.35f, 0.65f, 0.7f); // 분홍
                        break;
                }

                int signX = (i % 2 == 0) ? 1 : -1;
                int signY = (i < 2) ? 1 : -1;

                float size = KnightAreaSize;

                Matrix4x4 previousMatrix = Gizmos.matrix;

                Gizmos.matrix = Matrix4x4.TRS(
                    knightVector,
                    Camera.main.transform.rotation,
                    Vector3.one);

                Vector3 offset = new Vector3(
                    signX * size * 0.5f,
                    signY * size * 0.5f,
                    0f);

                Gizmos.DrawCube(offset, new Vector3(size, size, 0.01f));

                Gizmos.matrix = previousMatrix;
            }
        }
    }
}
