using System;
using UnityEngine;

public enum BattleMouseMovement
{
    Free = 0,       // 마우스를 상하좌우로 따라가기
    Gravity = 1    // 마우스 좌우 이동 + 좌클릭 점프
}

public enum BattlePlayerBoundary
{
    Box = 0,       // BattleBox 내부로 이동 제한
    Screen = 1     // 화면 내부로 이동 제한
}

public enum BattlePlayerStartPosition
{
    KeepAndClamp = 0, // 현재 위치 유지, 범위를 벗어나면 안으로 보정
    Normalized = 1   // 이동 범위 안의 상대 위치로 배치
}

[Serializable]
public class BattlePlayerPreparation
{
    public BattleMouseMovement movement =
        BattleMouseMovement.Free;

    public BattlePlayerBoundary boundary =
        BattlePlayerBoundary.Box;

    public bool visible = true;

    public BattlePlayerStartPosition startPosition =
        BattlePlayerStartPosition.KeepAndClamp;

    // (0, 0)은 왼쪽 아래, (0.5, 0.5)는 중앙, (1, 1)은 오른쪽 위
    public Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);
}