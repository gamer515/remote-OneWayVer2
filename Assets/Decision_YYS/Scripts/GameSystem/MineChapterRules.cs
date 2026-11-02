using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public sealed class MineChapterAction
{
    public string kind, winStoryPath, loseStoryPath;
    public int ability;
    public int[] successDelta, failureDelta;
    public bool guaranteed, granted;
}
[Serializable] public sealed class MineNpcBalance
{
    public string characterId;
    public int[] coins = new int[4];
}
[Serializable] public sealed class MinePlaceVisit
{
    public string placeId, openingPath;
    public bool granted;
    public bool paidCharm;
    public string returnStoryPath;
}
[Serializable] public sealed class MineChapterProgress
{
    public List<MinePlaceVisit> visits = new List<MinePlaceVisit>();
    public List<MineNpcBalance> recipients = new List<MineNpcBalance>();
    public List<string> completedActions = new List<string>();
    public float elapsedSeconds, collectorZ, fatigue;
    public bool initialized, resting;
    public int collected, persuasionAttempts;
    public string activityKey, resultPath;
    public string pendingOpeningPath;
    public int racePresses;
    public float activityRemaining;
    public int[] activityDelta;
    public int collectorRouteVersion, collectorCount, collectorPhase, collectorStop;
    public float collectorWait;
    public bool collectorPassedPlayer;
    public int collectorDirection;
    public bool collectorArrived;
}

/// <summary>All provisional balance values are owned by the approved trial settings JSON.</summary>
[Serializable] public sealed class MineChapterSettings
{
    public float laborSeconds, successBase, successBonus, abilityScale;
    public float encounterDistance;
    public int baseTax, alternatePaymentMultiplier;
    public float fatigueMax, fatigueResume, walkingFatiguePerSecond, recoveryPerSecond;
    public int racePressTarget, duelHitTarget;
    public float raceSeconds, rumorChance, bossAttackPerCoin, persuasionBase, persuasionPerAttempt;
    public int statusThreshold;
    public float statusPenaltyPerPoint;
    public string bossPrefab;
    public MineOpening[] openings;
    public string[] moods;
    public static MineChapterSettings Load()
    {
        var asset = Resources.Load<TextAsset>("Chapters/Chapter_01/Rules");
        if (asset == null) throw new InvalidOperationException("청크 1 시험 설정이 없습니다.");
        return JsonUtility.FromJson<MineChapterSettings>(asset.text);
    }
    public float SuccessChance(int ability) => successBase + successBonus * Mathf.Max(0, ability) / (Mathf.Max(0, ability) + abilityScale);
    public float StatusMultiplier(int strength) => 1 + Mathf.Max(0, statusThreshold - strength) * statusPenaltyPerPoint;
    public float BossAttackMultiplier(int collected) => 1 + Mathf.Max(0, collected) * bossAttackPerCoin;
    public float PersuasionChance(int charm, int attempts) => Mathf.Clamp01(SuccessChance(charm) * persuasionBase - attempts * persuasionPerAttempt);
    public static int[] ApplyDelta(int[] balances, int[] delta)
    {
        if (balances == null || balances.Length != 4 || delta == null || delta.Length != 4)
            throw new ArgumentException("네 능력 잔액과 변화량이 필요합니다.");
        var result = new int[4];
        for (int i = 0; i < 4; i++) result[i] = Mathf.Max(0, balances[i] + delta[i]);
        return result;
    }
    public static double Roll(int seed, int visit, string key)
    {
        unchecked { foreach (char c in key ?? "") seed = seed * 31 + c; }
        return new System.Random(seed ^ visit).NextDouble();
    }
}
[Serializable] public sealed class MineOpening
{
    public string placeId, rumor;
    public string[] variants;
}
