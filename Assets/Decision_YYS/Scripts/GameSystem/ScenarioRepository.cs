using System;
using UnityEngine;
using System.Collections.Generic;

public readonly struct ScenarioLoadResult
{
    public ScenarioData Scenario { get; }
    public string SourcePath { get; }
    public bool UsesAiRevision { get; }
    public string ErrorMessage { get; }

    public ScenarioLoadResult(
        ScenarioData scenario,
        string sourcePath,
        bool usesAiRevision,
        string errorMessage = null)
    {
        Scenario = scenario;
        SourcePath = sourcePath;
        UsesAiRevision = usesAiRevision;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// 분리된 이야기/선택지 JSON을 id로 병합하고, AI 통합본이 있으면 우선 사용합니다.
/// </summary>
public sealed class ScenarioRepository
{
    private readonly int runNumber;

    public ScenarioRepository(int runNumber = 1)
    {
        this.runNumber = Math.Max(1, runNumber);
    }

    public ScenarioLoadResult Load(string folder, string file)
    {
        string originalPath = $"{folder}/{file}";
        ScenarioData originalScenario = LoadOriginal(folder, file, out string mergeError);
        if (!ScenarioValidator.TryValidate(originalScenario, out string validationError))
        {
            string error = string.IsNullOrEmpty(mergeError) ? validationError : mergeError;
            return new ScenarioLoadResult(null, originalPath, false, error);
        }

        bool hasGeneratedScenario = SaveIOService.Instance.TryLoadGeneratedContent(
            runNumber,
            "Episodes",
            $"{originalPath}/Story",
            out ScenarioData aiScenario);
        // AI 저장본에서는 수정 대상인 text만 가져옵니다. destination 등 구조 정보는
        // 최신 원본을 유지하므로 예전 NewStory 파일이 있어도 지형 연결이 어긋나지 않습니다.
        if (hasGeneratedScenario)
        {
            if (ScenarioValidator.TryValidate(aiScenario, out string generatedError) &&
                TryApplyAiText(originalScenario, aiScenario))
                return new ScenarioLoadResult(originalScenario, originalPath, true);

            if (string.IsNullOrEmpty(generatedError))
                generatedError = "원본과 생성 이야기의 id 구성이 다릅니다.";

            Debug.LogWarning(
                $"[ScenarioRepository] {runNumber}회차 생성 이야기가 올바르지 않아 원본을 사용합니다: " +
                $"{originalPath} ({generatedError})");
        }

        return new ScenarioLoadResult(originalScenario, originalPath, false);
    }

    private static bool TryApplyAiText(ScenarioData original, ScenarioData aiRevision)
    {
        Dictionary<int, string> aiTextById = new Dictionary<int, string>();
        foreach (Dialogue dialogue in aiRevision.MainStory)
            aiTextById[dialogue.id] = dialogue.text;

        if (original.MainStory.Count != aiTextById.Count)
            return false;

        foreach (Dialogue dialogue in original.MainStory)
        {
            if (!aiTextById.ContainsKey(dialogue.id))
                return false;
        }

        foreach (Dialogue dialogue in original.MainStory)
        {
            dialogue.text = aiTextById[dialogue.id];
        }

        return true;
    }

    public ScenarioData LoadOriginalByPath(string originalPath, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            errorMessage = "시나리오 경로가 비어 있습니다.";
            return null;
        }

        int separatorIndex = originalPath.LastIndexOf('/');
        if (separatorIndex <= 0 || separatorIndex >= originalPath.Length - 1)
        {
            errorMessage = $"시나리오 경로 형식이 올바르지 않습니다: {originalPath}";
            return null;
        }

        return LoadOriginal(
            originalPath.Substring(0, separatorIndex),
            originalPath.Substring(separatorIndex + 1),
            out errorMessage);
    }

    private ScenarioData LoadOriginal(string folder, string file, out string errorMessage)
    {
        StoryContentData contentData = SaveIOService.Instance.LoadResourceData<StoryContentData>(
            $"{folder}/{file}/Story");
        StoryChoiceData choiceData = SaveIOService.Instance.LoadResourceData<StoryChoiceData>(
            $"{folder}/{file}/Choice");

        if (contentData?.MainStory == null || choiceData?.StoryChoices == null)
        {
            errorMessage = $"분리된 시나리오 파일을 불러오지 못했습니다: {folder}/{file}";
            return null;
        }

        Dictionary<int, StoryChoice> choicesById = new Dictionary<int, StoryChoice>();
        foreach (StoryChoice choice in choiceData.StoryChoices)
        {
            if (choice == null || !choicesById.TryAdd(choice.id, choice))
            {
                errorMessage = $"선택지 JSON에 null 또는 중복 id가 있습니다: {choice?.id}";
                return null;
            }
        }

        ScenarioData scenario = new ScenarioData { MainStory = new List<Dialogue>() };
        HashSet<int> storyIds = new HashSet<int>();

        foreach (StoryContent content in contentData.MainStory)
        {
            if (content == null || !storyIds.Add(content.id))
            {
                errorMessage = $"이야기 JSON에 null 또는 중복 id가 있습니다: {content?.id}";
                return null;
            }

            bool isChoice = string.Equals(
                content.type,
                DialogueType.Choice.ToString(),
                StringComparison.OrdinalIgnoreCase);
            choicesById.TryGetValue(content.id, out StoryChoice choice);

            if (isChoice && choice == null)
            {
                errorMessage = $"Choice 이야기 id {content.id}에 대응하는 선택지 정보가 없습니다.";
                return null;
            }

            if (!isChoice && choice != null)
            {
                errorMessage = $"Choice가 아닌 이야기 id {content.id}에 불필요한 선택지 정보가 있습니다.";
                return null;
            }

            scenario.MainStory.Add(new Dialogue
            {
                id = content.id,
                change = content.change.ToString(),
                type = content.type,
                isTransition = content.isTransition,
                background = content.background,
                destination = content.destination,
                character = content.character,
                text = content.text,
                option = choice?.option,
                figure = choice?.figure,
                npcEmotion = choice?.npcEmotion
            });
        }

        foreach (int choiceId in choicesById.Keys)
        {
            if (!storyIds.Contains(choiceId))
            {
                errorMessage = $"이야기 없이 선택지 JSON에만 존재하는 id가 있습니다: {choiceId}";
                return null;
            }
        }

        errorMessage = null;
        return scenario;
    }
}

public static class ScenarioValidator
{
    public static bool TryValidate(ScenarioData scenario, out string errorMessage)
    {
        if (scenario?.MainStory == null || scenario.MainStory.Count == 0)
        {
            errorMessage = "MainStory가 비어 있습니다.";
            return false;
        }

        HashSet<int> ids = new HashSet<int>();
        for (int i = 0; i < scenario.MainStory.Count; i++)
        {
            Dialogue dialogue = scenario.MainStory[i];
            if (dialogue == null || !ids.Add(dialogue.id))
            {
                errorMessage = $"MainStory[{i}]가 null이거나 id가 중복됩니다.";
                return false;
            }

            if (dialogue.Type == DialogueType.Unknown)
            {
                errorMessage = $"MainStory[{i}]의 type '{dialogue.type}'을 인식할 수 없습니다.";
                return false;
            }

            if (!dialogue.IsChoice)
                continue;

            if (dialogue.option == null || dialogue.figure == null ||
                dialogue.option.Length == 0 || dialogue.option.Length != dialogue.figure.Length)
            {
                errorMessage = $"MainStory[{i}]의 선택지와 수치 배열이 올바르지 않습니다.";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }
}
