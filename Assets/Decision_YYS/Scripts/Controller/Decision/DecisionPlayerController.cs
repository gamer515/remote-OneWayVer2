using UnityEngine;

/// <summary>
/// DecisionScene에서 Player 이동 명령과 저장할 목표 위치를 관리합니다.
/// </summary>
public sealed class DecisionPlayerController
{
    private readonly Player player;

    public Vector3 TargetPosition { get; private set; }
    public bool IsAvailable => player != null;
    public Vector3 CurrentPosition => IsAvailable ? player.transform.position : TargetPosition;

    public bool HasReachedTarget(float tolerance = 0.01f)
    {
        return IsAvailable && Mathf.Abs(CurrentPosition.z - TargetPosition.z) <= tolerance;
    }

    public DecisionPlayerController(Player player)
    {
        this.player = player;
    }

    public void Initialize(Vector3 startPosition)
    {
        if (!IsAvailable)
            return;

        TargetPosition = startPosition;
        player.Initialize(startPosition);
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
