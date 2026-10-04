using UnityEngine;

/// <summary>
/// 대련 제작 시 교체/조절할 손 프리팹, 월드 앵커와 화면 판정 영역을 한곳에 제공합니다.
/// [역할] Prepare로 손을 생성하고 CleanupDuelObjects로 정리하는 참조/생성 전용 스크립트입니다.
/// [수정] 손 교체는 Hand Prefabs, 생성 위치는 World Anchors, 기본 방향은 Hand Base Rotation.
/// 공격 모션/충돌 검사는 하지 않습니다. 생성된 두 손은 다른 컨트롤러가 받아서 사용합니다.
/// </summary>
public sealed class DuelAuthoringReferences : MonoBehaviour
{
    [Header("Optional Hand Prefabs")]
    [Tooltip("비워 두면 자동 생성하지 않습니다. 나중에 만든 플레이어 손 프리팹을 넣으세요.")]
    [SerializeField] private GameObject playerHandPrefab;
    [Tooltip("비워 두면 자동 생성하지 않습니다. 나중에 만든 NPC 손 프리팹을 넣으세요.")]
    [SerializeField] private GameObject npcHandPrefab;

    [Header("World Anchors")]
    [SerializeField] private Transform playerHandSpawnPoint;
    [SerializeField] private Transform npcHandSpawnPoint;

    [Header("Hand Base Rotation (relative to spawn anchor)")]
    [Tooltip("손 전체의 기본 방향. 애니메이션은 이 방향 기준의 상대 자세만 변경합니다.")]
    [SerializeField] private Vector3 playerHandBaseRotation = new Vector3(0f, -74f, -90f);
    [SerializeField] private Vector3 npcHandBaseRotation = new Vector3(0f, -74f, -90f);

    [Header("Scene Start Poses (optional)")]
    [Tooltip("씬에 직접 배치한 플레이어 HandRoot입니다. 연결하면 생성 손의 시작 위치/회전/월드 크기를 이 배치에서 가져옵니다. Play에서는 원본을 숨겨 중복을 막습니다.")]
    [SerializeField] private Transform playerHandStartPose;
    [Tooltip("씬에 직접 배치한 NPC NpcHandRoot입니다. 연결하면 시작 자세를 그대로 복사합니다. 손을 잡기 전에는 기존 NPC Z 보정/궤적 이동보다 이 자세를 우선합니다.")]
    [SerializeField] private Transform npcHandStartPose;

    [Header("NPC Patrol Pose (optional)")]
    [Tooltip("화면 궤적의 깊이(월드 Z)와 이동 중 부모 회전입니다. 화면 궤적 X/Y는 NpcDuelStateMachine의 Screen Path에서 조절합니다. 직접 지정한 월드 Path Points를 사용할 때만 이 위치에 최저점을 맞춥니다. 연결 시 Path Back Offset Z와 자동 방향 보정 대신 사용하며 생성 대기 자세/자식 애니메이션은 변경하지 않습니다.")]
    [SerializeField] private Transform npcHandPatrolPose;

    [Header("Player Grab Pose (optional)")]
    [Tooltip("처음 손을 잡았을 때 적용할 월드 위치/크기입니다. 비우면 현재 위치/크기를 유지합니다. 잡은 뒤 부모 기본 회전은 (0, -74, -90)으로 적용하며 자식 Animator와 클립은 변경하지 않습니다.")]
    [SerializeField] private Transform playerHandReadyPose;

    [Header("Screen-space Authoring Areas")]
    [SerializeField] private RectTransform authoringOverlay;
    [SerializeField] private RectTransform playerHandMoveArea;
    [SerializeField] private RectTransform npcHitArea;
    [SerializeField] private RectTransform guardArea;
    [SerializeField] private bool showGuidesDuringDuel = true;

    private GameObject spawnedPlayerHand;
    private GameObject spawnedNpcHand;

    public GameObject KnightTarget { get; private set; }
    public Transform PlayerHandSpawnPoint => playerHandSpawnPoint;
    public Transform NpcHandSpawnPoint => npcHandSpawnPoint;
    public RectTransform PlayerHandMoveArea => playerHandMoveArea;
    public RectTransform NpcHitArea => npcHitArea;
    public RectTransform GuardArea => guardArea;
    public GameObject SpawnedPlayerHand => spawnedPlayerHand;
    public GameObject SpawnedNpcHand => spawnedNpcHand;
    public bool HasNpcStartPose => npcHandStartPose != null;
    public Transform NpcHandPatrolPose => npcHandPatrolPose;

    private void Awake() => HideScenePreviews();

    private void HideScenePreviews()
    {
        if (!Application.isPlaying) return;
        // 원본을 삭제하거나 프리팹을 수정하지 않습니다. Play 종료 시 씬의 활성 상태도 복원됩니다.
        if (playerHandStartPose != null) playerHandStartPose.gameObject.SetActive(false);
        if (npcHandStartPose != null) npcHandStartPose.gameObject.SetActive(false);
        if (playerHandReadyPose != null) playerHandReadyPose.gameObject.SetActive(false);
    }

    public void Prepare(GameObject knightTarget)
    {
        CleanupSpawnedHands();
        HideScenePreviews();
        KnightTarget = knightTarget;
        SetGuidesVisible(showGuidesDuringDuel);
        spawnedPlayerHand = SpawnOptional(playerHandPrefab, playerHandSpawnPoint, "DuelPlayerHand");

        if (spawnedPlayerHand != null)
            spawnedPlayerHand.transform.localRotation = Quaternion.Euler(playerHandBaseRotation);

        spawnedNpcHand = SpawnOptional(npcHandPrefab, npcHandSpawnPoint, "DuelNpcHand");
        if (spawnedNpcHand != null)
            spawnedNpcHand.transform.localRotation = Quaternion.Euler(npcHandBaseRotation);

        ApplyStartPose(spawnedPlayerHand, playerHandStartPose);
        ApplyStartPose(spawnedNpcHand, npcHandStartPose);
    }

    /// <summary>처음 손을 잡을 때 부모 기본 회전을 적용합니다. 선택적인 배치 참조가 없어도 회전은 적용하며 생성/자식 애니메이션은 건드리지 않습니다.</summary>
    public void ApplyPlayerReadyPose()
    {
        if (spawnedPlayerHand == null) return;
        ApplyStartPose(spawnedPlayerHand, playerHandReadyPose);
        // 초기 생성은 눕힌 자세를 유지합니다. 잡기 회전은 선택 참조/자식 클립에 의존하지 않습니다.
        spawnedPlayerHand.transform.rotation = Quaternion.Euler(0f, -74f, -90f);
    }

    private void ApplyStartPose(GameObject instance, Transform pose)
    {
        if (instance == null || pose == null) return;
        Transform root = instance.transform;
        // 보드 앵커의 비균일 스케일 아래에서 회전하면 손/검이 찌그러집니다.
        // 시작 자세를 쓰는 손은 대련 시스템 아래에 두고 월드 배치를 복사합니다.
        root.SetParent(transform, true);
        root.SetPositionAndRotation(pose.position, pose.rotation);
        Vector3 currentScale = root.lossyScale;
        Vector3 size = pose.lossyScale;
        root.localScale = Vector3.Scale(root.localScale, new Vector3(
            Mathf.Abs(currentScale.x) > 0.0001f ? size.x / currentScale.x : 1f,
            Mathf.Abs(currentScale.y) > 0.0001f ? size.y / currentScale.y : 1f,
            Mathf.Abs(currentScale.z) > 0.0001f ? size.z / currentScale.z : 1f));
    }

    public void SetGuidesVisible(bool visible)
    {
        if (authoringOverlay != null) authoringOverlay.gameObject.SetActive(visible);
    }

    public void CleanupDuelObjects()
    {
        CleanupSpawnedHands();
        KnightTarget = null;
        SetGuidesVisible(false);
    }

    private static GameObject SpawnOptional(GameObject prefab, Transform anchor, string instanceName)
    {
        if (prefab == null || anchor == null) return null;
        GameObject instance = Instantiate(prefab, anchor);
        instance.name = instanceName;
        instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        return instance;
    }

    private void CleanupSpawnedHands()
    {
        // Destroy는 프레임 끝에 실행되므로, 재시작 직후 보드 Bounds에 이전 손
        // Renderer가 포함되지 않도록 앵커에서 먼저 분리합니다.
        if (spawnedPlayerHand != null)
        {
            spawnedPlayerHand.transform.SetParent(null, true);
            Destroy(spawnedPlayerHand);
        }
        if (spawnedNpcHand != null)
        {
            spawnedNpcHand.transform.SetParent(null, true);
            Destroy(spawnedNpcHand);
        }
        spawnedPlayerHand = null;
        spawnedNpcHand = null;
    }

    private void OnDrawGizmosSelected()
    {
        DrawAnchor(playerHandSpawnPoint, Color.cyan, 0.14f);
        DrawAnchor(npcHandSpawnPoint, new Color(1f, 0.35f, 0.2f), 0.14f);
        DrawAnchor(npcHandPatrolPose, Color.yellow, 0.1f);
    }

    private static void DrawAnchor(Transform anchor, Color color, float radius)
    {
        if (anchor == null) return;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(anchor.position, radius);
        Gizmos.DrawLine(anchor.position, anchor.position + anchor.forward * radius * 2f);
    }
}
