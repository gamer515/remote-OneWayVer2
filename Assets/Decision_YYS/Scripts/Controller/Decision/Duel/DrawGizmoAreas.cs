using UnityEngine;

/// <summary>
/// [역할] 플레이어의 NPC 공격 제스처 영역과 기사 주변 제작용 영역을 화면 좌표로 제공하고 Gizmo로 보여줍니다.
/// 기사 영역 번호는 우상단 1 → 좌상단 2 → 좌하단 3 → 우하단 4입니다. NPC 세로 베기의 목표입니다.
/// 플레이어 기사 영역은 월드 X/Y 평면에 수직으로 세우며 카메라의 기울기를 따라가지 않습니다.
/// 영역은 오른클릭 방어 입력을 제한하지 않습니다. 피해/접촉 판정과는 별개입니다.
/// [참조] 복제 기사가 있으면 Knight_Center, 없으면 Knight Spawn Pose의 중심을 사용합니다.
/// Inspector의 Show Gizmos만 표시를 제어합니다. 대련 시작/종료가 이 값을 바꾸지 않습니다.
/// 영역 배치를 바꾸려면 UpdateAreaRects, 색/표시는 Gizmo 설정을 수정합니다.
/// </summary>
public class DrawGizmoAreas : MonoBehaviour
{
    [Header("Knight Reference")]
    [SerializeField] private TutorialMiniGameController miniGameController;
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

    [Header("Gizmo Visibility")]
    [InspectorName("Show Gizmos")]
    [Tooltip("켜 두면 오브젝트 선택/대련 실행 여부와 관계없이 NPC 영역과 고정 기사 위치의 기즈모를 계속 표시합니다. 화면 영역 계산은 꺼도 유지됩니다. Scene/Game 창의 Gizmos도 켜져 있어야 합니다. 영구 설정은 Edit에서 체크하고 씬을 저장하세요.")]
    [SerializeField] private bool drawGizmoOnSet = true;
    public bool ShowGizmos { get => drawGizmoOnSet; set => drawGizmoOnSet = value; }

    private int drawGizmoCount = 4;

    [Header("Screen Rects (pixels, bottom-left origin)")]
    [SerializeField] private Rect[] uiAreas = new Rect[4];
    [SerializeField] private Rect[] knightUiAreas = new Rect[4];

    public Rect GetUiArea(int index) => uiAreas[index];
    public Rect GetNpcUiArea(int index) => uiAreas[index];
    public Rect GetKnightUiArea(int index) => knightUiAreas[index];
    /// <summary>사용자 번호(1~4)로 기사 영역을 가져옵니다. 기존 GetKnightUiArea는 0부터 시작합니다.</summary>
    public Rect GetKnightRegion(int number) => number >= 1 && number <= 4 ? knightUiAreas[number - 1] : default;
    private static int KnightSignX(int index) => index == 0 || index == 3 ? 1 : -1;
    public void RefreshUiAreas() => UpdateAreaRects();
    public bool HasKnightUiAreas { get; private set; }
    /// <summary>월드 기준 수직인 네 기사 영역의 중심입니다.</summary>
    public Vector3 KnightWorldCenter => knightVector;
    private const float KnightAreaSize = 1.5f;
    // 플레이어 영역의 표시와 화면 목표 계산이 같은 회전을 사용해야 합니다.
    private static Quaternion KnightAreaRotation => Quaternion.identity;

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

        knightCenter = null;
        HasKnightUiAreas = false;
        System.Array.Clear(knightUiAreas, 0, knightUiAreas.Length);
    }

    private void HandleKnightSpawned(GameObject instance)
    {
        UpdateAreaRects();
    }

    private Transform ResolveKnightCenter()
    {
        if (miniGameController == null) return null;
        // 생성물 정리/도박 전환 뒤에는 숨겨진 제작 원본 배치를 계속 참조합니다.
        GameObject instance = miniGameController.CurrentKnightTarget;
        Transform root = instance != null ? instance.transform : miniGameController.KnightSpawnPose;
        if (root == null) return null;
        Transform center = root.Find("Knight_Center");
        return center != null ? center : root;
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
        knightCenter = ResolveKnightCenter();
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

            Vector3 offset = new Vector3(KnightSignX(i), signY, 0f) * KnightAreaSize * 0.5f;
            Vector3 halfSize = new Vector3(KnightAreaSize, KnightAreaSize, 0f) * 0.5f;
            Quaternion rotation = KnightAreaRotation;
            // 수직 월드 면은 기울어진 카메라에서 원근에 의해 사다리꼴로 보일 수 있습니다.
            // 대각선 두 점만 쓰면 나머지 모서리가 빠지므로 네 꼭짓점 전체의 화면 범위를 구합니다.
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 localCorner = offset + new Vector3(
                    (corner % 2 == 0 ? -1f : 1f) * halfSize.x,
                    (corner < 2 ? -1f : 1f) * halfSize.y, 0f);
                Vector2 screenCorner = cam.WorldToScreenPoint(knightVector + rotation * localCorner);
                min = Vector2.Min(min, screenCorner);
                max = Vector2.Max(max, screenCorner);
            }
            knightUiAreas[i] = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
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
                        Gizmos.color = new Color(1f, 0.35f, 0.65f, 0.7f); // 3: 좌하단, 분홍
                        break;
                    case 3:
                        Gizmos.color = new Color(0f, 0.85f, 0.9f, 0.7f); // 4: 우하단, 청록
                        break;
                }

                int signX = KnightSignX(i);
                int signY = (i < 2) ? 1 : -1;

                float size = KnightAreaSize;

                Matrix4x4 previousMatrix = Gizmos.matrix;

                Gizmos.matrix = Matrix4x4.TRS(
                    knightVector,
                    KnightAreaRotation,
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
