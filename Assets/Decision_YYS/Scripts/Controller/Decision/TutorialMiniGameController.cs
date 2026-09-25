using System;
using System.Collections;
using UnityEngine;

/// <summary>Initial 튜토리얼용 보드 오브젝트 생성과 금화 앞뒤 판정을 담당합니다.</summary>
public sealed class TutorialMiniGameController : MonoBehaviour
{
    [SerializeField] private Transform miniGameBoard;
    [SerializeField] private GameObject knightTargetPrefab;
    [SerializeField] private GameObject goldCoinPrefab;
    [SerializeField, Min(0.05f)] private float coinSize = 0.45f;

    private Transform spawnedRoot;
    private Coroutine coinRoutine;

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
        GameObject knightTarget = SpawnBoardPiece(
            knightTargetPrefab, center, 0f, 1.65f);
        PrepareKnightTarget(knightTarget);
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
        body.AddForce(Vector3.up * 1.8f + UnityEngine.Random.insideUnitSphere * 0.35f,
            ForceMode.Impulse);
        body.AddTorque(new Vector3(10f, 4f, 8f), ForceMode.Impulse);
        coinRoutine = StartCoroutine(WaitForCoin(coin, body, choseHeads, completed));
    }

    public void Cleanup()
    {
        if (coinRoutine != null) StopCoroutine(coinRoutine);
        coinRoutine = null;
        if (spawnedRoot != null) Destroy(spawnedRoot.gameObject);
        spawnedRoot = null;
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

    private Vector3 GetBoardCenter()
    {
        Bounds bounds = GetBoardBounds();
        Vector3 center = bounds.center;
        center.y = bounds.max.y + 0.08f;
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
            if (!hasBoardRenderer)
            {
                bounds = renderer.bounds;
                hasBoardRenderer = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    private GameObject SpawnBoardPiece(
        GameObject prefab,
        Vector3 position,
        float yaw,
        float targetSize)
    {
        GameObject piece = Instantiate(prefab, position, prefab.transform.rotation, spawnedRoot);
        piece.transform.Rotate(miniGameBoard.up, yaw, Space.World);
        FitLargestRenderer(piece, targetSize);

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
