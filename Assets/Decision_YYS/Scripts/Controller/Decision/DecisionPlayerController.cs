using UnityEngine;

/// <summary>
/// DecisionScene에서 Player 이동 명령과 저장할 목표 위치를 관리합니다.
/// </summary>
public sealed class DecisionPlayerController
{
    private readonly Player player;

    public Vector3 TargetPosition { get; private set; }
    public bool IsAvailable => player != null;
    public Animator PlayerAnimator => player != null ? player.GetComponentInChildren<Animator>() : null;
    public Vector3 CurrentPosition => IsAvailable ? player.transform.position : TargetPosition;
    public Quaternion CurrentRotation => IsAvailable ? player.transform.rotation : Quaternion.identity;

    public bool HasReachedTarget(float tolerance = 0.01f)
    {
        return IsAvailable && Mathf.Abs(CurrentPosition.z - TargetPosition.z) <= tolerance;
    }

    public DecisionPlayerController(Player player)
    {
        this.player = player;
    }

    // Walking only changes Z; saved X/Y must stay on the terrain's walking lane.
    // Never hard-code the scene's -200 offset or treat world zero as universally invalid.
    public static Vector3 ResolveStartPosition(Vector3? savedPosition, Vector3 terrainOrigin,
        float terrainEndZ, Vector3 fallbackPosition, out bool recovered)
    {
        recovered = false;
        if (!savedPosition.HasValue)
            return fallbackPosition;

        Vector3 saved = savedPosition.Value;
        const float laneTolerance = 0.5f;
        bool validZ = IsFinite(saved.z) && saved.z >= terrainOrigin.z && saved.z <= terrainEndZ;
        bool validLane = IsFinite(saved.x) && IsFinite(saved.y) &&
            Mathf.Abs(saved.x - terrainOrigin.x) <= laneTolerance &&
            Mathf.Abs(saved.y - terrainOrigin.y) <= laneTolerance;
        if (validLane && validZ)
            return saved;

        recovered = true;
        // Preserve an already travelled distance when only the lateral/height coordinates are corrupt.
        // A stray origin position (0,0,0) on an offset terrain uses the normal entry point instead.
        float safeZ = validZ && saved.z > terrainOrigin.z + 0.01f ? saved.z : fallbackPosition.z;
        return new Vector3(terrainOrigin.x, terrainOrigin.y, safeZ);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public void Initialize(Vector3 startPosition, Quaternion? savedRotation = null)
    {
        if (!IsAvailable)
            return;

        TargetPosition = startPosition;
        player.Initialize(startPosition);
        if (savedRotation.HasValue)
            player.SetFacing(savedRotation.Value);
    }

    public void MoveToZ(float targetZ)
    {
        if (!IsAvailable)
            return;

        TargetPosition = new Vector3(
            TargetPosition.x,
            TargetPosition.y,
            targetZ);

        player.FaceForward();
        player.SetTargetZ(targetZ);
    }

    public void StopAndLookAt(Vector3 characterPosition)
    {
        if (!IsAvailable)
            return;

        TargetPosition = player.transform.position;
        player.StopAndLookAt(characterPosition);
    }
}
