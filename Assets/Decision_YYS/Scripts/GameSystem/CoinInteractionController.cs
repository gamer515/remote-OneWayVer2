using UnityEngine;

/// <summary>
/// 코인을 마우스로 누르는 동안 직접 옮기고, 버튼을 놓으면 다시 물리에 맡깁니다.
/// </summary>
public sealed class CoinInteractionController : MonoBehaviour
{
    [SerializeField] private Camera inputCamera;
    [SerializeField] private CoinDropController coinDropController;

    [Header("Coin Drag")]
    [Tooltip("클릭 당시 코인 높이에 더하는 값입니다. 0이면 들어 올리지 않습니다.")]
    [SerializeField] private float liftOffset = 0.15f;
    [SerializeField] private float horizontalFollowSpeed = 20f;
    [SerializeField] private float verticalFollowSpeed = 60f;
    [SerializeField] private float maxRayDistance = 100f;

    [Header("Surface & Height")]
    [Tooltip("현재 X/Z 위치 아래에서 표면을 찾기 위해 Ray를 시작하는 높이입니다.")]
    [SerializeField] private float surfaceRayHeight = 10f;
    [Tooltip("중앙뿐 아니라 코인 가장자리에서도 아래 표면을 미리 찾는 반경입니다.")]
    [SerializeField] private float surfaceDetectionRadius = 0.45f;
    [SerializeField] private LayerMask surfaceMask = ~0;
    [SerializeField] private float wheelHeightSpeed = 0.3f;
    [SerializeField] private float minimumHeightOffset = 0f;
    [SerializeField] private float maximumHeightOffset = 3f;

    private BettingCoin grabbedCoin;
    private Rigidbody grabbedBody;
    private Plane dragPlane;
    private Vector3 grabOffset;
    private Vector3 targetPosition;
    private bool hasTargetPosition;
    private float grabbedHalfHeight;
    private float fallbackHeight;
    private float manualHeightOffset;
    private readonly RaycastHit[] surfaceHits = new RaycastHit[32];

    private void Awake()
    {
        if (inputCamera == null)
            inputCamera = Camera.main;
        if (coinDropController == null)
            coinDropController = GetComponent<CoinDropController>();
    }

    private void OnEnable()
    {
        if (coinDropController != null)
            coinDropController.BettingCollectionStarted += ReleaseCoin;
    }

    private void OnDisable()
    {
        if (coinDropController != null)
            coinDropController.BettingCollectionStarted -= ReleaseCoin;

        ReleaseCoin();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
            TryGrabCoin();

        if (grabbedCoin != null && Input.GetMouseButton(0))
            UpdateTargetPosition();

        if (grabbedCoin != null && Input.GetMouseButtonUp(0))
            ReleaseCoin();
    }

    private void FixedUpdate()
    {
        if (grabbedBody == null || !hasTargetPosition)
            return;

        Vector3 currentPosition = grabbedBody.position;
        Vector3 nextPosition = new Vector3(
            Mathf.Lerp(
                currentPosition.x,
                targetPosition.x,
                horizontalFollowSpeed * Time.fixedDeltaTime),
            Mathf.MoveTowards(
                currentPosition.y,
                targetPosition.y,
                verticalFollowSpeed * Time.fixedDeltaTime),
            Mathf.Lerp(
                currentPosition.z,
                targetPosition.z,
                horizontalFollowSpeed * Time.fixedDeltaTime));
        grabbedBody.MovePosition(nextPosition);
    }

    private void TryGrabCoin()
    {
        if (inputCamera == null || grabbedCoin != null)
            return;

        Ray ray = inputCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance);
        BettingCoin nearestCoin = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            BettingCoin candidate = hits[i].collider.GetComponentInParent<BettingCoin>();
            if (candidate == null || hits[i].distance >= nearestDistance)
                continue;

            nearestCoin = candidate;
            nearestDistance = hits[i].distance;
        }

        if (nearestCoin == null)
            return;

        Rigidbody body = nearestCoin.GetComponent<Rigidbody>();
        if (body == null)
            return;

        grabbedCoin = nearestCoin;
        grabbedBody = body;
        grabbedHalfHeight = GetCoinHalfHeight(nearestCoin);
        fallbackHeight = grabbedCoin.transform.position.y + liftOffset;
        manualHeightOffset = 0f;

        // 수평 이동 계산용 평면이며 실제 Y 높이는 아래쪽 표면 Raycast가 결정합니다.
        dragPlane = new Plane(Vector3.up, grabbedCoin.transform.position);

        if (dragPlane.Raycast(ray, out float enter))
        {
            Vector3 planePoint = ray.GetPoint(enter);
            grabOffset = grabbedCoin.transform.position - planePoint;
            grabOffset.y = 0f;
            targetPosition = planePoint + grabOffset;
            targetPosition.y = CalculateSurfaceHeight(targetPosition);
            hasTargetPosition = true;
        }

        grabbedBody.linearVelocity = Vector3.zero;
        grabbedBody.angularVelocity = Vector3.zero;
        grabbedBody.useGravity = false;
        grabbedBody.isKinematic = true;
    }

    private void UpdateTargetPosition()
    {
        float wheelInput = Input.mouseScrollDelta.y;
        if (!Mathf.Approximately(wheelInput, 0f))
        {
            manualHeightOffset = Mathf.Clamp(
                manualHeightOffset + wheelInput * wheelHeightSpeed,
                minimumHeightOffset,
                maximumHeightOffset);
        }

        Ray ray = inputCamera.ScreenPointToRay(Input.mousePosition);
        if (!dragPlane.Raycast(ray, out float enter))
            return;

        targetPosition = ray.GetPoint(enter) + grabOffset;
        targetPosition.y = CalculateSurfaceHeight(targetPosition);
        hasTargetPosition = true;
    }

    private float CalculateSurfaceHeight(Vector3 horizontalPosition)
    {
        float highestSurface = float.NegativeInfinity;

        // 코인 중심과 네 가장자리를 검사하여 옆면에 닿기 전에 높은 표면을 감지합니다.
        SampleHighestSurface(horizontalPosition, ref highestSurface);
        SampleHighestSurface(
            horizontalPosition + Vector3.right * surfaceDetectionRadius,
            ref highestSurface);
        SampleHighestSurface(
            horizontalPosition + Vector3.left * surfaceDetectionRadius,
            ref highestSurface);
        SampleHighestSurface(
            horizontalPosition + Vector3.forward * surfaceDetectionRadius,
            ref highestSurface);
        SampleHighestSurface(
            horizontalPosition + Vector3.back * surfaceDetectionRadius,
            ref highestSurface);

        if (float.IsNegativeInfinity(highestSurface))
            return fallbackHeight + manualHeightOffset;

        return highestSurface + grabbedHalfHeight + liftOffset + manualHeightOffset;
    }

    private void SampleHighestSurface(Vector3 samplePosition, ref float highestSurface)
    {
        Vector3 rayOrigin = new Vector3(
            samplePosition.x,
            Mathf.Max(samplePosition.y, fallbackHeight) + surfaceRayHeight,
            samplePosition.z);

        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            Vector3.down,
            surfaceHits,
            surfaceRayHeight * 2f + maximumHeightOffset,
            surfaceMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            // 코인이 밖으로 튀는 것을 막는 경계 벽은 충돌에는 사용하지만,
            // 코인을 들어 올릴 표면 높이로는 사용하지 않습니다.
            if (IsCoinBoundary(surfaceHits[i].collider))
                continue;

            // 잡고 있는 코인 자체를 표면으로 인식하면 계속 위로 상승하므로 제외합니다.
            BettingCoin hitCoin = surfaceHits[i].collider.GetComponentInParent<BettingCoin>();
            if (hitCoin == grabbedCoin)
                continue;

            if (surfaceHits[i].point.y > highestSurface)
                highestSurface = surfaceHits[i].point.y;
        }
    }

    private static bool IsCoinBoundary(Collider collider)
    {
        if (collider == null)
            return false;

        Transform current = collider.transform;
        while (current != null)
        {
            if (current.name == "Coin_Collider_Boundary" ||
                current.name.StartsWith("Coin_Boundary_", System.StringComparison.Ordinal))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static float GetCoinHalfHeight(BettingCoin coin)
    {
        Collider[] colliders = coin.GetComponentsInChildren<Collider>();
        float halfHeight = 0f;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i].enabled)
                halfHeight = Mathf.Max(halfHeight, colliders[i].bounds.extents.y);
        }

        return halfHeight;
    }

    private void ReleaseCoin()
    {
        if (grabbedBody != null)
        {
            grabbedBody.isKinematic = false;
            grabbedBody.useGravity = true;
            grabbedBody.WakeUp();
        }

        grabbedCoin = null;
        grabbedBody = null;
        hasTargetPosition = false;
        manualHeightOffset = 0f;
    }
}
