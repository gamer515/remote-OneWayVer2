using System;
using UnityEngine;

public static class BattleEntry
{
    private static BattleStartData pendingData;

    // BattleScene으로 이동하기 전에 호출
    public static void SetNext(BattleStartData data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        pendingData = data;
    }

    // BattleScene 초기화 시 한 번만 호출
    public static BattleStartData TakeOrDefault()
    {
        BattleStartData data =
            pendingData ?? BattleStartData.CreateDefault();

        pendingData = null;
        return data;
    }

    // Domain Reload를 꺼도 이전 Play의 데이터가 남지 않도록 초기화
    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        pendingData = null;
    }
}