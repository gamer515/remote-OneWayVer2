using System;
using System.Linq;
using UnityEngine;

/// <summary>Authored attitude slots, not a guess from relationship scores or button positions.</summary>
public static class StoryChoiceRoles
{
    public static string Expected(int slot) => slot == 0 ? "Positive" : slot == 1 ? "Neutral" :
        slot == 2 ? "Negative" : slot == 3 ? "Chaotic" : "Unknown";

    public static bool Validate(EncounterCard card, out string error)
    {
        error = null;
        var actions = card.choiceActions ?? Array.Empty<StoryChoiceAction>();
        if (string.IsNullOrEmpty(card.choiceKind))
        {
            // Legacy action menus remain readable; relationship choices must be explicitly authored.
            if (actions.Any(a => a != null && (!string.IsNullOrEmpty(a.choiceRole) ||
                !string.IsNullOrEmpty(a.relationshipId))))
                error = "관계 선택에는 choiceKind와 선택별 choiceRole을 지정하세요.";
        }
        else if (card.type != "Choice") error = "choiceKind는 Choice 카드에만 지정합니다.";
        else if (card.choiceKind == "Mechanic")
        {
            if (actions.Any(a => !string.IsNullOrEmpty(a?.choiceRole)))
                error = "조작 선택(Mechanic)에 긍정/부정 역할을 붙이지 마세요.";
        }
        else if (card.choiceKind == "Attitude")
        {
            if (card.options?.Length != 4 || actions.Length != 4)
                error = "태도 선택에는 문구와 행동이 각각 4개 필요합니다.";
            else if (actions.Any(a => a == null || string.IsNullOrWhiteSpace(a.choiceId)) ||
                actions.Select(a => a.choiceId).Distinct().Count() != 4)
                error = "태도 선택의 choiceId는 비어 있지 않고 서로 달라야 합니다.";
            else for (int i = 0; i < 4; i++)
                if (actions[i].choiceRole != Expected(i))
                { error = (i + 1) + "번 choiceRole은 " + Expected(i) + "여야 합니다."; break; }
        }
        else error = "choiceKind는 Attitude 또는 Mechanic입니다.";
        return error == null;
    }

    public static void ValidateRoot(EncounterCardRoot root)
    {
        if (root?.MainStory == null) throw new InvalidOperationException("이야기 카드가 없습니다.");
        for (int i = 0; i < root.MainStory.Count; i++)
            if (root.MainStory[i] == null || !Validate(root.MainStory[i], out _))
                throw new InvalidOperationException("선택 성격/순서를 확인하세요: index=" + i);
    }

    public static string Recorded(string kind, StoryChoiceAction action) => kind == "Mechanic" ? "Mechanic" :
        kind == "Attitude" && IsAttitude(action?.choiceRole) ? action.choiceRole : "Unknown";
    private static bool IsAttitude(string role) => role == "Positive" || role == "Neutral" ||
        role == "Negative" || role == "Chaotic";

    public static string ForMemory(StoryEventRecord record)
    {
        // New records carry their authored role. Missing legacy roles are resolved by stable ID, never slot.
        if (record.choiceRole == "Mechanic" || IsAttitude(record.choiceRole)) return record.choiceRole;
        if (!string.IsNullOrEmpty(record.storyPath) && !string.IsNullOrEmpty(record.choiceId))
        {
            var asset = Resources.Load<TextAsset>("Story_Json_Data/" + record.storyPath + "/Story");
            var root = asset == null ? null : JsonUtility.FromJson<EncounterCardRoot>(asset.text);
            var matches = (root?.MainStory ?? new System.Collections.Generic.List<EncounterCard>())
                .Where(c => c != null && (string.IsNullOrEmpty(c.speakerId) || c.speakerId == record.characterId))
                .SelectMany(c => (c.choiceActions ?? Array.Empty<StoryChoiceAction>())
                    .Where(a => a?.choiceId == record.choiceId).Select(a => new { card = c, action = a })).ToArray();
            if (matches.Length == 1 && Validate(matches[0].card, out _))
                return Recorded(matches[0].card.choiceKind, matches[0].action);
        }
        return "Unknown";
    }

    // Older test/Edit input may lack these descriptive fields. Never repair conflicting explicit roles or IDs.
    public static void FillMissingMetadata(EncounterCard older, EncounterCard current)
    {
        if (string.IsNullOrEmpty(older.choiceKind)) older.choiceKind = current.choiceKind;
        if (older.choiceActions?.Length != current.choiceActions?.Length) return;
        for (int i = 0; i < (older.choiceActions?.Length ?? 0); i++)
            if (older.choiceActions[i] != null && current.choiceActions[i] != null &&
                string.IsNullOrEmpty(older.choiceActions[i].choiceRole))
                older.choiceActions[i].choiceRole = current.choiceActions[i].choiceRole;
    }
}
