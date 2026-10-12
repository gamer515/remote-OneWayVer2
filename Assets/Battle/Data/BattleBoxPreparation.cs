using System;
using UnityEngine;

public enum BattleBoxTransition
{
    Unchanged = 0, // 기존 동작 유지
    Morph = 1,     // 현재 모습에서 부드럽게 변형
    Instant = 2    // 즉시 변경
}

[Serializable]
public class BattleBoxPreparation
{
    public BattleBoxTransition transition =
        BattleBoxTransition.Unchanged;

    public Vector2 size = new Vector2(8f, 8f);

    public bool keepCurrentCenter = true;
    public Vector2 center = new Vector2(0f, -3f);

    // 현재 중심 유지보다 우선하여 카메라 화면 중앙에 배치한다.
    public bool useScreenCenter;

    // 회전 중인 상자의 대각선까지 화면 안에 들어오도록 크기를 제한한다.
    public bool fitRotationToScreen;
    [Min(0f)] public float rotationScreenMargin = 0.5f;

    [Min(0f)]
    public float seconds = 0.5f;
}