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
/// AI 수정 시나리오를 우선 확인하고, 없으면 원본을 불러옵니다.
/// </summary>
public sealed class ScenarioRepository
{
    public ScenarioLoadResult Load(string folder, string file)
    {
        string originalPath = $"{folder}/{file}";
        string aiPath = "NewStory_" + originalPath.Replace("/", "_");
        ScenarioData scenario = SaveIOService.Instance.LoadData<ScenarioData>(aiPath);

        if (ScenarioValidator.TryValidate(scenario, out _))
            return new ScenarioLoadResult(scenario, originalPath, true);

        scenario = SaveIOService.Instance.LoadData<ScenarioData>(originalPath);
        if (ScenarioValidator.TryValidate(scenario, out string errorMessage))
            return new ScenarioLoadResult(scenario, originalPath, false);

        return new ScenarioLoadResult(null, originalPath, false, errorMessage);
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

        for (int i = 0; i < scenario.MainStory.Count; i++)
        {
            Dialogue dialogue = scenario.MainStory[i];
            if (dialogue == null)
            {
                errorMessage = $"MainStory[{i}]가 null입니다.";
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
