public enum StoryAdvanceResult
{
    NextDialogue,
    EpisodeCompleted,
    Invalid
}

/// <summary>
/// 지문 인덱스 변경과 현재 지문 조회만 담당합니다.
/// </summary>
public sealed class StoryProgressController
{
    private readonly DecisionSession session;

    public Dialogue Current
    {
        get
        {
            if (!HasCurrentDialogue)
                return null;

            return session.Scenario.MainStory[session.StoryIndex];
        }
    }

    public bool HasCurrentDialogue =>
        session.Scenario?.MainStory != null &&
        session.StoryIndex >= 0 &&
        session.StoryIndex < session.Scenario.MainStory.Count;

    public StoryProgressController(DecisionSession session)
    {
        this.session = session;
    }

    public void SetScenario(ScenarioData scenario)
    {
        session.Scenario = scenario;

        if (scenario?.MainStory == null || scenario.MainStory.Count == 0)
        {
            session.StoryIndex = 0;
            return;
        }

        if (session.StoryIndex < 0 || session.StoryIndex >= scenario.MainStory.Count)
            session.StoryIndex = 0;
    }

    public StoryAdvanceResult Advance()
    {
        if (session.Scenario?.MainStory == null)
            return StoryAdvanceResult.Invalid;

        session.StoryIndex++;
        if (session.StoryIndex < session.Scenario.MainStory.Count)
            return StoryAdvanceResult.NextDialogue;

        session.EpisodeIndex++;
        session.StoryIndex = 0;
        return StoryAdvanceResult.EpisodeCompleted;
    }
}
