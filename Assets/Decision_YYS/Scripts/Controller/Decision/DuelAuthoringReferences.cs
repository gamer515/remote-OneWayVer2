using UnityEngine;

/// <summary>
/// 대련 제작 시 교체/조절할 손 프리팹, 월드 앵커와 화면 판정 영역을 한곳에 제공합니다.
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

    public void Prepare(GameObject knightTarget)
    {
        CleanupSpawnedHands();
        KnightTarget = knightTarget;
        SetGuidesVisible(showGuidesDuringDuel);
        spawnedPlayerHand = SpawnOptional(playerHandPrefab, playerHandSpawnPoint, "DuelPlayerHand");

        if (spawnedPlayerHand != null)
            spawnedPlayerHand.transform.localRotation = Quaternion.Euler(22.4f, 167f, -57f);

        spawnedNpcHand = SpawnOptional(npcHandPrefab, npcHandSpawnPoint, "DuelNpcHand");
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
        if (spawnedPlayerHand != null) Destroy(spawnedPlayerHand);
        if (spawnedNpcHand != null) Destroy(spawnedNpcHand);
        spawnedPlayerHand = null;
        spawnedNpcHand = null;
    }

    private void OnDrawGizmosSelected()
    {
        DrawAnchor(playerHandSpawnPoint, Color.cyan, 0.14f);
        DrawAnchor(npcHandSpawnPoint, new Color(1f, 0.35f, 0.2f), 0.14f);
    }

    private static void DrawAnchor(Transform anchor, Color color, float radius)
    {
        if (anchor == null) return;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(anchor.position, radius);
        Gizmos.DrawLine(anchor.position, anchor.position + anchor.forward * radius * 2f);
    }
}
