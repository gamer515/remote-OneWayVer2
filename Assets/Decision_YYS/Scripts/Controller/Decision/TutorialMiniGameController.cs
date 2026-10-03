using System;
using System.Collections;
using UnityEngine;

/// <summary>Initial 튜토리얼용 보드 오브젝트 생성과 금화 앞뒤 판정을 담당합니다.</summary>
public sealed class TutorialMiniGameController : MonoBehaviour
{
    [SerializeField] private Transform miniGameBoard;
    [SerializeField] private GameObject knightTargetPrefab;
    [SerializeField] private GameObject goldCoinPrefab;
    [SerializeField, Min(0.01f)]
    [Tooltip("기사 복제품 루트 Transform의 균일 Scale입니다. 0.6이면 X/Y/Z 모두 0.6입니다. Renderer 길이로 다시 정규화하지 않습니다. Play 중 변경도 생성 기사에 반영합니다.")]
    private float knightScale = 0.6f;
    [Tooltip("기사 생성 기준의 월드 위치/회전입니다. 씬에 배치한 KnightCharacter_Copy를 연결하면 현재 배치를 그대로 사용하고 Play 동안 원본을 숨깁니다. 비우면 보드 중앙에 생성합니다.")]
    [SerializeField] private Transform knightSpawnPose;
    [SerializeField, Min(0.05f)] private float coinSize = 0.45f;

    private Transform spawnedRoot;
    private Coroutine coinRoutine;
    private float duelBoardSurfaceY;
    private float appliedKnightScale;

    private void Awake()
    {
        // 제작용 원본은 남겨 두고 런타임 복제본만 표시합니다.
        if (knightSpawnPose != null) knightSpawnPose.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (CurrentKnightTarget != null && !Mathf.Approximately(appliedKnightScale, Mathf.Max(0.01f, knightScale)))
            ApplyKnightScale(CurrentKnightTarget);
    }

    public GameObject CurrentKnightTarget { get; private set; }
    /// <summary>손 생성 전 측정한 보드 윗면 높이. 대련 중 손/기사 Renderer 때문에 높이가 바뀌지 않습니다.</summary>
    public float BoardSurfaceY => miniGameBoard == null ? float.NegativeInfinity : CurrentKnightTarget != null ? duelBoardSurfaceY : GetBoardCenter().y;
    // 복제본의 생성과 초기 설정이 완료되었을 때 전달합니다.
    public event Action<GameObject> KnightSpawned;
    public bool IsReady => miniGameBoard != null && knightTargetPrefab != null &&
                           goldCoinPrefab != null;

    public void StartDuel(Action<bool> completed)
    {
        Cleanup();
        if (!IsReady)
        {
            completed?.Invoke(false);
            return;
        }

        EnsureRoot();
        Vector3 center = GetBoardCenter();
        duelBoardSurfaceY = center.y;
        CurrentKnightTarget = SpawnKnight(center);
        KnightSpawned?.Invoke(CurrentKnightTarget);
        completed?.Invoke(true);
    }

    public void StartCoinToss(bool choseHeads, Action<bool, bool> completed)
    {
        Cleanup();
        if (!IsReady)
        {
            completed?.Invoke(false, false);
            return;
        }

        EnsureRoot();
        CreateCoinBoardSurface();
        Vector3 spawn = GetBoardCenter() + miniGameBoard.up * 1.2f;
        GameObject coin = Instantiate(goldCoinPrefab, spawn, goldCoinPrefab.transform.rotation, spawnedRoot);
        FitLargestRenderer(coin, coinSize);
        AddConvexColliders(coin);
        Rigidbody body = coin.GetComponent<Rigidbody>();
        // UnityEngine.Object의 fake-null은 ??로 판별되지 않을 수 있으므로 명시적으로 검사한다.
        if (body == null)
            body = coin.AddComponent<Rigidbody>();

        if (body == null)
        {
            Debug.LogError("Gold_Coin에 Rigidbody를 추가하지 못했습니다.", coin);
            Destroy(coin);
            completed?.Invoke(false, false);
            return;
        }
        body.mass = 0.08f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        // Impulse는 질량 0.08에서 1.8 / 0.08 = 22.5m/s가 되어 보드 밖으로 날아갑니다.
        // 질량에 관계없는 작은 초기 속도를 주어 실제 보드 위에서 앞/뒤가 결정되게 합니다.
        body.AddForce(Vector3.up * 1.8f + UnityEngine.Random.insideUnitSphere * 0.35f,
            ForceMode.VelocityChange);
        body.AddTorque(new Vector3(10f, 4f, 8f), ForceMode.VelocityChange);
        coinRoutine = StartCoroutine(WaitForCoin(coin, body, choseHeads, completed));
    }

    public void Cleanup()
    {
        if (coinRoutine != null) StopCoroutine(coinRoutine);
        coinRoutine = null;
        if (spawnedRoot != null)
        {
            // Destroy는 프레임 끝에 실행됩니다. 그 전까지 보드 Renderer Bounds에
            // 지난 기사/손이 섞이지 않도록 생성물 루트를 먼저 분리합니다.
            spawnedRoot.SetParent(null, true);
            Destroy(spawnedRoot.gameObject);
        }
        spawnedRoot = null;
        CurrentKnightTarget = null;
    }

    private IEnumerator WaitForCoin(
        GameObject coin,
        Rigidbody body,
        bool choseHeads,
        Action<bool, bool> completed)
    {
        float elapsed = 0f;
        while (coin != null && elapsed < 6f)
        {
            elapsed += Time.deltaTime;
            if (elapsed > 1.5f && body.linearVelocity.sqrMagnitude < 0.0025f &&
                body.angularVelocity.sqrMagnitude < 0.01f)
                break;
            yield return null;
        }

        bool heads = coin != null && Vector3.Dot(coin.transform.up, Vector3.up) >= 0f;
        bool won = choseHeads == heads;
        coinRoutine = null;
        completed?.Invoke(won, heads);
    }

    private void EnsureRoot()
    {
        spawnedRoot = new GameObject("TutorialMiniGameObjects").transform;
        spawnedRoot.SetParent(miniGameBoard, true);
    }

    /// <summary>보드의 시각적 윗면에 동전용 바닥을 둡니다. 원본 보드 콜라이더/프리팹은 변경하지 않습니다.</summary>
    private void CreateCoinBoardSurface()
    {
        Bounds bounds = GetBoardBounds();
        var surface = new GameObject("CoinBoardSurface");
        surface.transform.SetParent(spawnedRoot, true);
        const float thickness = 0.05f;
        surface.transform.position = new Vector3(bounds.center.x, bounds.max.y - thickness * 0.5f, bounds.center.z);
        var collider = surface.AddComponent<BoxCollider>();
        collider.size = new Vector3(Mathf.Max(0.1f, bounds.size.x), thickness, Mathf.Max(0.1f, bounds.size.z));
    }

    private Vector3 GetBoardCenter()
    {
        Bounds bounds = GetBoardBounds();
        Vector3 center = bounds.center;
        center.y = bounds.max.y;
        return center;
    }

    private Bounds GetBoardBounds()
    {
        Renderer[] renderers = miniGameBoard.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(miniGameBoard.position, Vector3.zero);
        bool hasBoardRenderer = false;
        foreach (Renderer renderer in renderers)
        {
            if (spawnedRoot != null && renderer.transform.IsChildOf(spawnedRoot)) continue;
            if (knightSpawnPose != null && renderer.transform.IsChildOf(knightSpawnPose)) continue;
            if (!hasBoardRenderer)
            {
                bounds = renderer.bounds;
                hasBoardRenderer = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    private GameObject SpawnKnight(Vector3 boardCenter)
    {
        Vector3 position = knightSpawnPose != null ? knightSpawnPose.position : boardCenter;
        Quaternion rotation = knightSpawnPose != null ? knightSpawnPose.rotation : knightTargetPrefab.transform.rotation;
        GameObject piece = Instantiate(knightTargetPrefab, position, rotation, spawnedRoot);
        PrepareKnightTarget(piece);
        // Animator를 먼저 멈춘 뒤 복제 루트의 크기를 설정합니다.
        ApplyKnightScale(piece);

        // 수동 배치가 있으면 바닥 자동 보정으로 위치를 덮어쓰지 않습니다.
        if (knightSpawnPose != null) return piece;

        Renderer[] renderers = piece.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            float boardSurfaceY = GetBoardCenter().y;
            piece.transform.position += Vector3.up * (boardSurfaceY + 0.04f - bounds.min.y);
        }

        return piece;
    }

    private void ApplyKnightScale(GameObject knight)
    {
        appliedKnightScale = Mathf.Max(0.01f, knightScale);
        knight.transform.localScale = Vector3.one * appliedKnightScale;
    }

    private static void PrepareKnightTarget(GameObject knight)
    {
        if (knight == null) return;
        knight.name = "TutorialKnightTarget";
        foreach (Camera camera in knight.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (AudioListener listener in knight.GetComponentsInChildren<AudioListener>(true))
            listener.enabled = false;
        Player player = knight.GetComponent<Player>();
        if (player != null) player.enabled = false;
        foreach (Animator animator in knight.GetComponentsInChildren<Animator>(true))
        {
            animator.applyRootMotion = false;
            animator.enabled = false;
        }
    }

    private static void FitLargestRenderer(GameObject instance, float targetSize)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largest > 0.0001f) instance.transform.localScale *= targetSize / largest;
    }

    private static void AddConvexColliders(GameObject instance)
    {
        foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = true;
        }
    }

    private void OnDisable() => Cleanup();
}
