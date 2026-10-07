using System;
using System.Collections.Generic;

[Serializable]
public sealed class StoryCharacter
{
    public string characterId;
    public string name;
    public string role;
    public string personality;
    public bool repeatWithinTerrain;
}

[Serializable]
public sealed class GuideCoinAction
{
    // take_knowledge / return_knowledge / punish / kneel / allocate
    public string kind;
    public int amount;
    public string readyStoryPath;
    public string retryStoryPath;
    public string charmZeroStoryPath;
}

[Serializable]
public sealed class GuideProgress
{
    public int[] knowledgeBefore;
    public int[] punishmentBefore;
    public string activeEffectId;
    public int effectStage;
    public int[] effectBefore;
    public List<string> completedEffects = new List<string>();
    public bool allocating;
    public int allocationAttempt;
    public int allocationMode; // 0: choose gear direction, 1: add, -1: remove
    public int allocationVisit;
    public int[] allocation = new int[4];
    public int[] allocationCoinsBefore;
    public int[] allocationStatsBefore;
    public int[] committedStats;
    public List<PlayedEncounterCardRecord> runHistory = new List<PlayedEncounterCardRecord>();
}

[Serializable]
public sealed class LocalStoryRevisionRequest
{
    public int sourceRun;
    public int targetRun;
    public string status = "PendingLocalModel";
    public string chapterId;
    public string reason;
    public string[] scenarioPaths;
    public int[] finalStats;
    public List<StoryEventRecord> events;
    public List<PlayedEncounterCardRecord> playedCards;
    public string constraints = "지형/인물/선택 결과/아이템 사실은 변경 금지. 만나지 않은 인물의 과거 만남을 창작하지 말 것. 지문과 허용된 선택지 표현만 변경.";
}

[Serializable]
public sealed class StoryEventRecord
{
    public string eventKey;
    public int sequence;
    public int run;
    public string chapterId;
    public string episodeId;
    public string terrainId;
    public string terrainName;
    public string placeId;
    public string characterId;
    public string characterName;
    public string storyPath;
    public string cardId;
    public string kind;
    public string choiceId;
    public int choiceSlot;
    public string relationshipId;
    public int relationshipDelta;
    public string displayedText;
    public string semanticText;
    public int[] coinsBefore;
    public int[] coinsAfter;
    public int[] statsBefore;
    public int[] statsAfter;
    // Future duel/gambling exchanges use the same journal, without inventing rewards now.
    public string itemId;
    public int quantity;
    public string direction;
    public string reason;
}

public static class GuideAllocationRules
{
    public const int Budget = 20;
    public const int Capacity = 20;

    public static int Remaining(int[] counts)
    {
        int total = 0;
        if (counts == null || counts.Length != 4) return Budget;
        foreach (int count in counts) total += count;
        return Budget - total;
    }

    public static bool TryChange(int[] counts, int index, int direction)
    {
        if (counts == null || counts.Length != 4 || index < 0 || index >= 4 ||
            (direction != 1 && direction != -1)) return false;
        if (direction > 0 && (Remaining(counts) <= 0 || counts[index] >= Capacity)) return false;
        if (direction < 0 && counts[index] <= 0) return false;
        counts[index] += direction;
        return true;
    }

    public static string Outcome(int[] counts)
    {
        if (counts == null || counts.Length != 4 || Remaining(counts) != 0) return "incomplete";
        foreach (int count in counts) if (count < 0 || count > Capacity) return "invalid";
        if (counts[0] == 0) return "death";
        if (counts[1] == 0) return "retry";
        return "ready";
    }
}
