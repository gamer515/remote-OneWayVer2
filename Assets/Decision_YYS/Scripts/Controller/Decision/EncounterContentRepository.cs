using System;

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

/// <summary>지형의 connectStoryCards 경로에서 해당 오브젝트의 데이터만 읽습니다.</summary>
public sealed class EncounterContentRepository
{
    private readonly ScenarioRepository scenarioRepository;

    public EncounterContentRepository(ScenarioRepository scenarioRepository)
    {
        this.scenarioRepository = scenarioRepository;
    }

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
        int separator = contentPath.LastIndexOf('/');
        if (separator <= 0 || separator == contentPath.Length - 1)
        {
            error = $"connectStoryCards 경로가 올바르지 않습니다: {contentPath}";
            return false;
        }

        ScenarioLoadResult result = scenarioRepository.Load(
            contentPath.Substring(0, separator),
            contentPath.Substring(separator + 1));
        cards = result.Scenario;
        error = result.ErrorMessage;

        if (cards == null) return false;
        foreach (Dialogue card in cards.MainStory)
        {
            if (!card.IsChoice) continue;
            if (card.options == null || card.options.Length != 4)
            {
                error = $"{contentPath}/Choice의 id {card.id}에는 선택 문구 4개가 필요합니다.";
                cards = null;
                return false;
            }
        }

        return true;
    }
}
