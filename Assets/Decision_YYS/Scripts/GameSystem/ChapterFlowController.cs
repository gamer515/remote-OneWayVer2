/// <summary>
/// 챕터 결과 기록, 외부 전달, 다음 챕터 준비와 저장 순서를 관리합니다.
/// </summary>
public sealed class ChapterFlowController
{
    private readonly DecisionSession session;
    private readonly StatContainer statContainer;
    private readonly StoryRelayManager relayManager;
    private readonly DecisionSaveService saveService;

    public ChapterFlowController(
        DecisionSession session,
        StatContainer statContainer,
        StoryRelayManager relayManager,
        DecisionSaveService saveService)
    {
        this.session = session;
        this.statContainer = statContainer;
        this.relayManager = relayManager;
        this.saveService = saveService;
    }

    public bool PrepareBattleTransition()
    {
        if (!CanCompleteCurrentChapter())
            return false;

        CompleteCurrentChapter(StoryRelayTrigger.MidTransition, clearHistory: false);
        return true;
    }

    public bool CompleteChapter()
    {
        if (!CanCompleteCurrentChapter())
            return false;

        CompleteCurrentChapter(StoryRelayTrigger.ChapterEnd, clearHistory: true);
        return true;
    }

    private bool CanCompleteCurrentChapter()
    {
        return session?.Omnibus?.MainStories != null &&
               statContainer != null &&
               session.ChapterIndex >= 0 &&
               session.ChapterIndex < session.Omnibus.MainStories.Count;
    }

    private void CompleteCurrentChapter(StoryRelayTrigger relayTrigger, bool clearHistory)
    {
        // Relay에는 초기화 전 스탯과 완료된 챕터 번호가 전달되어야 하므로 먼저 스냅샷을 만듭니다.
        int completedChapterIndex = session.ChapterIndex;
        int[] statsSnapshot = statContainer.stats;
        ChapterResult result = statContainer.CreateChapterResult(completedChapterIndex);

        saveService.SaveChapterResult(result);

        if (relayManager != null)
        {
            relayManager.Relay(
                relayTrigger,
                session.ScenarioPath,
                session.PlayedHistory,
                statsSnapshot,
                completedChapterIndex);
        }

        session.ChapterIndex++;
        session.EpisodeIndex = 0;
        session.StoryIndex = 0;

        if (clearHistory)
            session.PlayedHistory.Clear();

        statContainer.ResetAllStats();
        // 다음 챕터가 이전 지형 위치에서 시작하지 않도록 진행도와 함께 위치를 초기화합니다.
        saveService.ClearPlayerPosition();
        saveService.SaveCheckpoint(session, statContainer.stats);
    }
}
