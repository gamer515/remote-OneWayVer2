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
[Serializable] public sealed class MineChapterProgress : MineCollectorProgress
{
    public List<MinePlaceVisit> visits = new List<MinePlaceVisit>();
    public List<MineNpcBalance> recipients = new List<MineNpcBalance>();
    public List<string> completedActions = new List<string>();
    public float elapsedSeconds, savedDayLength, fatigue;
    public bool initialized, resting;
    public bool variedRoomLayout;
    public int collected, persuasionAttempts;
    public string activityKey, resultPath;
    public string pendingOpeningPath;
    public int racePresses;
    public float activityRemaining;
    public int[] activityDelta;
    public int nextTaxDay, taxScheduleTier, taxScheduleInterval, collectorScheduleVersion;
    public string taxChapterId;
    public bool collectorOpeningCompleted;
    public List<MineCollectorProgress> collectorPatrols = new List<MineCollectorProgress>();
    public List<MineTaxArrear> taxArrears = new List<MineTaxArrear>();
    public MineLearningProgress learning = new MineLearningProgress();
}

// The inherited fields retain the old single-patrol checkpoint layout for migration.
[Serializable] public class MineCollectorProgress
{
    public int collectorDay = -1;
    public int collectorGlobalChunk = -1;
    public bool collectorPending;
    public float collectorZ, collectorEntryZ;
    public Vector3[] collectorPositions;
    public int collectorRouteVersion, collectorCount, collectorPhase, collectorStop;
    public float collectorWait;
    public bool collectorPassedPlayer;
    public int collectorNpcPauseIndex, collectorNpcWitnessIndex;
    public int collectorNpcActorIndex, collectorNpcWitnessActor;
    public float collectorNpcPause;
    public bool taxPatrolActive;
    public float collectorRouteStartZ, collectorRouteEndZ;
    public bool collectorReturning;
    public int collectorChunkIndex;
    public bool collectorChaseClosed;
    public MineTaxInvoice taxInvoice;
    public string shortcutPursuitInvoiceId;
    public bool HasTaxInvoice => taxInvoice?.lines?.Length > 0;
}

[Serializable] public sealed class MineCollectorPatrolSettings
{
    public int inspectionStart = 1, inspectionIncrease = 1;
    public float inspectionSpeed = 3.4f;
    public float chaseSpeedStart = 3.8f, chaseSpeedIncrease = .20f, chaseSpeedMax = 4.7f;

    public int MinimumCoins(int chunkIndex) => Mathf.Max(0, inspectionStart + inspectionIncrease * chunkIndex);
    public float InspectionSpeed => Mathf.Max(.01f, inspectionSpeed);
    public float ChaseSpeed(int chunkIndex) => Mathf.Max(.01f, Mathf.Min(chaseSpeedMax, chaseSpeedStart + chaseSpeedIncrease * chunkIndex));
}

/// <summary>All provisional balance values are owned by the approved trial settings JSON.</summary>
[Serializable] public sealed class MineChapterSettings
{
    public ChapterDayCycle dayCycle;
    public float collectorArrivalSeconds, collectorNpcWaitMin, collectorNpcWaitMax;
    public MineCollectorPatrolSettings collectorPatrol;
    public float laborSeconds, successBase, successBonus, abilityScale;
    public float encounterDistance;
    public int baseTax, alternatePaymentMultiplier;
    public float taxCostScale = 1f;
    public float fatigueMax, fatigueResume, walkingFatiguePerSecond, recoveryPerSecond;
    public int racePressTarget, duelHitTarget;
    public float raceSeconds, rumorChance, bossAttackPerCoin, persuasionBase, persuasionPerAttempt;
    public int statusThreshold;
    public float statusPenaltyPerPoint;
    public string bossPrefab;
    public MineOpening[] openings;
    public string[] moods;
    public static MineChapterSettings Load(string document = "Chapter_01/Rules")
    {
        var authored = ChapterWorkbookContent.Get(document);
        var asset = authored == null ? Resources.Load<TextAsset>("Chapters/" + document) : null;
        if (authored == null && asset == null) throw new InvalidOperationException("청크 1 시험 설정이 없습니다.");
        var rules = authored != null ? authored.ToObject<MineChapterSettings>() : JsonUtility.FromJson<MineChapterSettings>(asset.text);
        if (rules.collectorPatrol == null)
        {
            asset ??= Resources.Load<TextAsset>("Chapters/" + document);
            rules.collectorPatrol = (asset == null ? null : JsonUtility.FromJson<MineChapterSettings>(asset.text).collectorPatrol)
                ?? new MineCollectorPatrolSettings();
        }
        // Existing installs keep their own copy of Rules.xlsx. Migrate only the old shipped timing.
        if (document == "Chapter_01/Rules" && rules.dayCycle is { daySeconds: 135f, duskSeconds: 15f,
                nightSeconds: 135f, dawnSeconds: 15f } cycle)
            cycle.daySeconds = cycle.nightSeconds = 75f;
        return rules;
    }
    public float SuccessChance(int ability) => successBase + successBonus * Mathf.Max(0, ability) / (Mathf.Max(0, ability) + abilityScale);
    public float StatusMultiplier(int strength) => 1 + Mathf.Max(0, statusThreshold - strength) * statusPenaltyPerPoint;
    public float BossAttackMultiplier(int collected) => 1 + Mathf.Max(0, collected) * bossAttackPerCoin;
    public float PersuasionChance(int charm, int attempts, int persuasion = 0)
    {
        float skillBonus = Mathf.Clamp(persuasion * .03f, 0f, .15f);
        return Mathf.Clamp01(SuccessChance(charm) * persuasionBase + skillBonus - attempts * persuasionPerAttempt);
    }
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
