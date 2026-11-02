using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// DecisionScene에서 Player 이동 명령과 저장할 목표 위치를 관리합니다.
/// </summary>
public sealed class DecisionPlayerController
{
    private readonly Player player;
    private readonly Queue<Vector3> path = new Queue<Vector3>();
    private Vector3 currentTarget;
    private float laneX;

    public Vector3 TargetPosition { get; private set; }
    public bool IsAvailable => player != null;
    public Animator PlayerAnimator => player != null ? player.GetComponentInChildren<Animator>() : null;
    public Vector3 CurrentPosition => IsAvailable ? player.transform.position : TargetPosition;
    public Quaternion CurrentRotation => IsAvailable ? player.transform.rotation : Quaternion.identity;

    public bool HasReachedTarget(float tolerance = 0.01f)
    {
        if (!IsAvailable || Vector3.Distance(CurrentPosition, currentTarget) > tolerance) return false;
        if (path.Count == 0) return true;
        currentTarget = path.Dequeue(); player.SetDestination(currentTarget); return false;
    }

    public DecisionPlayerController(Player player)
    {
        this.player = player;
    }

    // Allow saved lateral approaches within the authored place width.
    // Never hard-code the scene's -200 offset or treat world zero as universally invalid.
    public static Vector3 ResolveStartPosition(Vector3? savedPosition, Vector3 terrainOrigin,
        float terrainEndZ, Vector3 fallbackPosition, out bool recovered, float laneTolerance = 0.5f)
    {
        recovered = false;
        if (!savedPosition.HasValue)
            return fallbackPosition;

        Vector3 saved = savedPosition.Value;
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

    public void Initialize(Vector3 startPosition, Quaternion? savedRotation = null, float? walkingLaneX = null)
    {
        if (!IsAvailable)
            return;

        TargetPosition = startPosition;
        currentTarget = startPosition; laneX = walkingLaneX ?? startPosition.x; path.Clear();
        player.Initialize(startPosition);
        if (savedRotation.HasValue)
            player.SetFacing(savedRotation.Value);
    }

    public void MoveToZ(float targetZ)
    {
        if (!IsAvailable)
            return;

        MoveAlongPath(new[] { new Vector3(laneX, CurrentPosition.y, targetZ) });
    }

    public void MoveAlongPath(IEnumerable<Vector3> waypoints)
    {
        path.Clear();
        foreach (var point in waypoints) if (Vector3.Distance(point, CurrentPosition) > 0.01f) path.Enqueue(point);
        TargetPosition = path.Count > 0 ? path.Last() : CurrentPosition;
        currentTarget = path.Count > 0 ? path.Dequeue() : CurrentPosition;
        if (IsAvailable) { player.ResetNpcFraming(); player.SetDestination(currentTarget); }
    }
    public void PauseAndLookAt(Vector3 position) { if (IsAvailable) player.StopAndLookAt(position); }
    public void ResumeMovement() { if (IsAvailable) player.SetDestination(currentTarget); }

    public void StopAndLookAt(Vector3 characterPosition)
    {
        if (!IsAvailable)
            return;

        TargetPosition = player.transform.position;
        currentTarget = TargetPosition; path.Clear();
        player.StopAndLookAt(characterPosition);
    }
    public void FocusNpc(Transform npc, bool fullBody) { if (IsAvailable) player.FocusNpc(npc, fullBody); }
    public void ResetNpcFraming() { if (IsAvailable) player.ResetNpcFraming(); }
}
