using UnityEngine;

/// <summary>
/// Keeps the road map's orthographic footprint inside the player's current chunk.
/// </summary>
[RequireComponent(typeof(Camera))]
public class RoadViewCameraController : MonoBehaviour
{
    private const float PlayerRearMargin = .5f;

    [Header("Initial Position")]
    [SerializeField] private Vector3 initialCameraPosition =
        new Vector3(-200f, 22f, 10.5f);
    [SerializeField] private float playerFollowOffsetZ = 0.5f;

    [Header("Follow Settings")]
    [SerializeField, Min(0f)] private float maxDistance = 20f;
    [SerializeField, Min(0.01f)] private float smoothTime = 2f;

    private Transform playerTransform;
    private EnvController environment;
    private Camera mapCamera;
    private float preferredSize;
    private float followVelocity;
    private float groundY;
    private float centerZ;
    private Vector3 originalGroundOffset;
    private Vector3 groundExtentPerSize;

    private void Awake()
    {
        mapCamera = GetComponent<Camera>();
        preferredSize = mapCamera.orthographicSize;
        if (mapCamera.targetTexture is RenderTexture target)
            mapCamera.aspect = (float)target.width / target.height;
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = new Color(.13f, .11f, .10f);
        transform.position = initialCameraPosition;
    }

    public void Initialize(Transform playerTransform, bool useInitialPosition, EnvController environment)
    {
        if (playerTransform == null)
        {
            Debug.LogError("Road View 카메라에 연결할 Player Transform이 없습니다.", this);
            return;
        }

        this.playerTransform = playerTransform;
        this.environment = environment;
        followVelocity = 0f;

        groundY = environment.TerrainOrigin.y;
        Vector3 forward = transform.forward;
        originalGroundOffset = forward * ((groundY - initialCameraPosition.y) / forward.y);
        // Orthographic rays are parallel. Project the two screen axes onto the floor
        // to get the full view footprint without changing the authored camera rotation.
        Vector3 groundRight = ProjectOntoGround(transform.right, forward);
        Vector3 groundUp = ProjectOntoGround(transform.up, forward);
        groundExtentPerSize = new Vector3(
            Mathf.Abs(groundRight.x) * mapCamera.aspect + Mathf.Abs(groundUp.x), 0f,
            Mathf.Abs(groundRight.z) * mapCamera.aspect + Mathf.Abs(groundUp.z));

        float startZ = useInitialPosition ? initialCameraPosition.z + originalGroundOffset.z : PlayerFollowZ;
        ApplyFrame(startZ, startZ);
    }

    private static Vector3 ProjectOntoGround(Vector3 axis, Vector3 forward) =>
        axis - forward * (axis.y / forward.y);

    private float PlayerFollowZ => playerTransform.position.z + playerFollowOffsetZ + originalGroundOffset.z;

    private void LateUpdate()
    {
        if (playerTransform == null)
            return;

        float followZ = PlayerFollowZ;
        float targetZ = Mathf.Clamp(centerZ, followZ - maxDistance, followZ + maxDistance);
        ApplyFrame(centerZ, targetZ);
    }

    private void ApplyFrame(float currentZ, float targetZ)
    {
        float centerX = initialCameraPosition.x + originalGroundOffset.x;
        float size = preferredSize;
        float minimumZ = float.NegativeInfinity;
        float maximumZ = float.PositiveInfinity;
        if (environment != null && environment.TryGetCurrentMapBounds(out var bounds))
        {
            size = Mathf.Min(preferredSize, .5f * Mathf.Min(
                bounds.height / groundExtentPerSize.z, bounds.width / groundExtentPerSize.x));
            centerX = bounds.center.x;

            // At a chunk change the old camera position can lie outside the new legal range.
            float halfDepth = size * groundExtentPerSize.z;
            minimumZ = Mathf.Min(bounds.yMin + halfDepth, bounds.center.y);
            maximumZ = Mathf.Max(bounds.yMax - halfDepth, bounds.center.y);
            // The original look-ahead can put the player just below the frame at an entrance.
            float playerLimit = playerTransform.position.z + Mathf.Max(0f, halfDepth - PlayerRearMargin);
            maximumZ = Mathf.Clamp(playerLimit, minimumZ, maximumZ);
            float clampedZ = Mathf.Clamp(currentZ, minimumZ, maximumZ);
            if (clampedZ != currentZ) followVelocity = 0f;
            currentZ = clampedZ;
            targetZ = Mathf.Clamp(targetZ, minimumZ, maximumZ);
        }
        mapCamera.orthographicSize = size;
        float nextZ = Mathf.SmoothDamp(currentZ, targetZ, ref followVelocity, smoothTime);
        centerZ = Mathf.Clamp(nextZ, minimumZ, maximumZ);
        if (centerZ != nextZ) followVelocity = 0f;

        // Moving along the view direction preserves framing while keeping raised geometry
        // and even the closest ground corner in front of the near plane.
        float viewDepth = (mapCamera.nearClipPlane + mapCamera.farClipPlane) * .5f;
        transform.position = new Vector3(centerX, groundY, centerZ) - transform.forward * viewDepth;
    }
}
