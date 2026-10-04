using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public sealed class EncounterCardRoot
{
    public List<EncounterCard> MainStory;
}

[Serializable]
public sealed class EncounterCard
{
    public string type;
    public bool isTransition;
    public string background;
    public string text;
    public string[] options;
    public int[] statWeights;
    public string npcEmotion;
    public StoryChoiceAction[] choiceActions;
    public string startAction;
    public int duelHitTarget;
    public string winStoryPath;
    public string loseStoryPath;
    public string errorStoryPath;
}

/// <summary>지형의 connectStoryCards 경로에서 해당 오브젝트의 데이터만 읽습니다.</summary>
public sealed class EncounterContentRepository
{
    private readonly int runNumber;

    public EncounterContentRepository(int runNumber = 1)
    {
        this.runNumber = Math.Max(1, runNumber);
    }

    public bool TryLoadCards(string contentPath, out ScenarioData cards, out string error)
    {
        cards = null;
        EncounterCardRoot original = SaveIOService.Instance.LoadResourceData<EncounterCardRoot>(
            contentPath + "/Story");
        if (original?.MainStory == null || original.MainStory.Count == 0)
        {
            error = $"{contentPath}/Story에 카드가 없습니다.";
            return false;
        }

        EncounterCardRoot source = original;
        if (runNumber > 1 && SaveIOService.Instance.TryLoadGeneratedContent(
                runNumber,
                "Encounters",
                contentPath + "/Story",
                out EncounterCardRoot generated))
        {
            if (TryApplyGeneratedText(original, generated, out string generatedError))
            {
                Debug.Log(
                    $"[EncounterContentRepository] {runNumber}회차 생성 이야기를 사용합니다: " +
                    $"{contentPath}/Story");
            }
            else
            {
                Debug.LogWarning(
                    $"[EncounterContentRepository] 생성 이야기 검증 실패로 원본을 사용합니다: " +
                    $"{contentPath}/Story ({generatedError})");
            }
        }

        cards = new ScenarioData { MainStory = new List<Dialogue>() };
        for (int index = 0; index < source.MainStory.Count; index++)
        {
            EncounterCard sourceCard = source.MainStory[index];
            if (sourceCard == null || string.IsNullOrWhiteSpace(sourceCard.text) ||
                (sourceCard.type != "Next" && sourceCard.type != "Choice" && sourceCard.type != "End"))
            {
                error = $"{contentPath}/Story의 {index}번째 카드 형식이 올바르지 않습니다.";
                cards = null;
                return false;
            }
            if (sourceCard.type == "Choice" &&
                (sourceCard.options == null || sourceCard.options.Length != 4))
            {
                error = $"{contentPath}/Story의 {index}번째 선택 카드에는 기어 방향 문구 4개가 필요합니다.";
                cards = null;
                return false;
            }

            if (!ValidateActions(sourceCard, out string actionError))
            {
                error = $"{contentPath}/Story의 {index}번째 카드: {actionError}";
                cards = null;
                return false;
            }

            // JSON은 오브젝트 폴더와 배열 순서로 식별합니다. 런타임 기록용 번호만 결정적으로 만듭니다.
            cards.MainStory.Add(new Dialogue
            {
                id = StableCardId(contentPath, index),
                type = sourceCard.type,
                isTransition = sourceCard.isTransition,
                background = sourceCard.background,
                text = sourceCard.text,
                options = sourceCard.options,
                statWeights = sourceCard.statWeights,
                npcEmotion = sourceCard.npcEmotion,
                choiceActions = sourceCard.choiceActions,
                startAction = sourceCard.startAction,
                duelHitTarget = sourceCard.duelHitTarget,
                winStoryPath = sourceCard.winStoryPath,
                loseStoryPath = sourceCard.loseStoryPath,
                errorStoryPath = sourceCard.errorStoryPath
            });
        }

        error = null;
        return true;
    }


    private static bool StoryExists(string path) => !string.IsNullOrWhiteSpace(path) &&
        Resources.Load<TextAsset>("Story_Json_Data/" + path + "/Story") != null;

    private static bool ValidateActions(EncounterCard card, out string error)
    {
        if (card.duelHitTarget < 0)
        {
            error = "duelHitTarget은 양수이며, 생략/0이면 기본 3회입니다.";
            return false;
        }
        if (card.choiceActions != null && card.choiceActions.Length > 0)
        {
            if (card.type != "Choice" || card.choiceActions.Length != 4)
            {
                error = "choiceActions는 Choice 카드에서 네 선택지와 순서를 맞춰야 합니다.";
                return false;
            }
            foreach (StoryChoiceAction choice in card.choiceActions)
            {
                if (choice == null || (choice.action != "continue" && choice.action != "skip" &&
                    choice.action != "story") || (choice.action == "story" && !StoryExists(choice.storyPath)))
                {
                    error = "선택 행동 또는 분기 Story 경로가 올바르지 않습니다.";
                    return false;
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(card.startAction) &&
            (card.type != "Next" || (card.startAction != "duel" && card.startAction != "coin_heads" &&
             card.startAction != "coin_tails") || !StoryExists(card.winStoryPath) ||
             !StoryExists(card.loseStoryPath) || !StoryExists(card.errorStoryPath)))
        {
            error = "미니게임 시작 Next 카드의 행동/승리/패배/오류 Story 경로를 확인하세요.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool ChoiceActionsEqual(StoryChoiceAction[] left, StoryChoiceAction[] right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
            if (JsonUtility.ToJson(left[index]) != JsonUtility.ToJson(right[index])) return false;
        return true;
    }

    private static bool TryApplyGeneratedText(
        EncounterCardRoot original,
        EncounterCardRoot generated,
        out string error)
    {
        if (generated?.MainStory == null ||
            generated.MainStory.Count != original.MainStory.Count)
        {
            error = "원본과 생성 이야기의 카드 수가 다릅니다.";
            return false;
        }

        for (int index = 0; index < original.MainStory.Count; index++)
        {
            EncounterCard originalCard = original.MainStory[index];
            EncounterCard generatedCard = generated.MainStory[index];
            if (originalCard == null || generatedCard == null ||
                string.IsNullOrWhiteSpace(generatedCard.text))
            {
                error = $"{index}번째 생성 카드 또는 text가 비어 있습니다.";
                return false;
            }

            if (generatedCard.type != originalCard.type ||
                generatedCard.isTransition != originalCard.isTransition ||
                generatedCard.background != originalCard.background ||
                generatedCard.npcEmotion != originalCard.npcEmotion ||
                generatedCard.startAction != originalCard.startAction ||
                generatedCard.duelHitTarget != originalCard.duelHitTarget ||
                generatedCard.winStoryPath != originalCard.winStoryPath ||
                generatedCard.loseStoryPath != originalCard.loseStoryPath ||
                generatedCard.errorStoryPath != originalCard.errorStoryPath ||
                !ChoiceActionsEqual(generatedCard.choiceActions, originalCard.choiceActions) ||
                !ArrayEquals(generatedCard.options, originalCard.options) ||
                !ArrayEquals(generatedCard.statWeights, originalCard.statWeights))
            {
                error = $"{index}번째 카드의 text 외 구조가 원본과 다릅니다.";
                return false;
            }

            if (!HasSameImmutableCores(originalCard.text, generatedCard.text))
            {
                error = $"{index}번째 카드의 {{ }} 핵심 문자열이 변경되었습니다.";
                return false;
            }
        }

        for (int index = 0; index < original.MainStory.Count; index++)
            original.MainStory[index].text = generated.MainStory[index].text;

        error = null;
        return true;
    }

    private static bool ArrayEquals<T>(T[] left, T[] right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int index = 0; index < left.Length; index++)
            if (!comparer.Equals(left[index], right[index])) return false;
        return true;
    }

    private static bool HasSameImmutableCores(string originalText, string generatedText)
    {
        MatchCollection originalCores = Regex.Matches(originalText ?? string.Empty, @"\{[^{}]*\}");
        MatchCollection generatedCores = Regex.Matches(generatedText ?? string.Empty, @"\{[^{}]*\}");
        if (originalCores.Count != generatedCores.Count) return false;

        for (int index = 0; index < originalCores.Count; index++)
            if (!string.Equals(
                    originalCores[index].Value,
                    generatedCores[index].Value,
                    StringComparison.Ordinal))
                return false;
        return true;
    }

    private static int StableCardId(string contentPath, int index)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in contentPath)
                hash = (hash ^ character) * 16777619;
            return (int)((hash ^ (uint)index) * 16777619);
        }
    }
}
