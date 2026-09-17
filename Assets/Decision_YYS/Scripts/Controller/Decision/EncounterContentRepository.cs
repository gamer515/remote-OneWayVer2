using System;
using System.Collections.Generic;

[Serializable]
public sealed class EncounterInteractionData
{
    public string displayName;
    public string prompt;
    public EncounterInteractionOption[] options;
}

[Serializable]
public sealed class EncounterInteractionOption
{
    public string text;
    // open: 이 오브젝트의 카드 표시, skip: 지나가기.
    public string action;
}

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
}

/// <summary>지형의 connectStoryCards 경로에서 해당 오브젝트의 데이터만 읽습니다.</summary>
public sealed class EncounterContentRepository
{
    public bool TryLoadInteraction(string contentPath, out EncounterInteractionData data, out string error)
    {
        data = SaveIOService.Instance.LoadResourceData<EncounterInteractionData>(
            contentPath + "/Interaction");

        if (data?.options == null || data.options.Length != 4)
        {
            error = $"{contentPath}/Interaction에는 기어 네 방향의 options 4개가 필요합니다.";
            return false;
        }

        foreach (EncounterInteractionOption option in data.options)
        {
            if (option == null || string.IsNullOrWhiteSpace(option.text) ||
                (option.action != "open" && option.action != "skip"))
            {
                error = $"{contentPath}/Interaction의 text 또는 action(open/skip)이 올바르지 않습니다.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public bool TryLoadCards(string contentPath, out ScenarioData cards, out string error)
    {
        cards = null;
        EncounterCardRoot source = SaveIOService.Instance.LoadResourceData<EncounterCardRoot>(
            contentPath + "/Story");
        if (source?.MainStory == null || source.MainStory.Count == 0)
        {
            error = $"{contentPath}/Story에 카드가 없습니다.";
            return false;
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
                npcEmotion = sourceCard.npcEmotion
            });
        }

        error = null;
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
