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

    public bool PrepareBattleTransition(UnityEngine.Vector3 playerPosition)
    {
        if (!CanCompleteCurrentChapter())
            return false;

        CompleteCurrentChapter(
            StoryRelayTrigger.MidTransition,
            clearHistory: false,
            playerPosition);
        return true;
    }

    public bool CompleteChapter(UnityEngine.Vector3 playerPosition)
    {
        if (!CanCompleteCurrentChapter())
            return false;

        CompleteCurrentChapter(
            StoryRelayTrigger.ChapterEnd,
            clearHistory: true,
            playerPosition);
        return true;
    }

    private bool CanCompleteCurrentChapter()
    {
        return session?.Omnibus?.chapters != null &&
               statContainer != null &&
               session.ChapterIndex >= 0 &&
               session.ChapterIndex < session.Omnibus.chapters.Count;
    }

    private void CompleteCurrentChapter(
        StoryRelayTrigger relayTrigger,
        bool clearHistory,
        UnityEngine.Vector3 playerPosition)
    {
        // Relay에는 초기화 전 스탯과 완료된 챕터 번호가 전달되어야 하므로 먼저 스냅샷을 만듭니다.
        int completedChapterIndex = session.ChapterIndex;
        int[] statsSnapshot = statContainer.stats;
        if (relayManager != null && completedChapterIndex != (int)Constants.Chapter.Initial)
        {
            StoryInfluenceProfile influence = relayManager.CreateInfluenceProfile(
                statsSnapshot,
                completedChapterIndex,
                session.RunNumber);
            saveService.SaveInfluenceProfile(influence);

            foreach (CompletedEpisodeRecord episode in session.CompletedEpisodes)
            {
                if (episode == null) continue;
                relayManager.Relay(
                    StoryRelayTrigger.EpisodeEnd,
                    episode.scenarioPath,
                    episode.storyHistory,
                    episode.selectedChoices,
                    statsSnapshot,
                    completedChapterIndex,
                    session.RunNumber,
                    influence);
            }

            // 임계치 전투는 현재 에피소드가 정상 종료되지 않았으므로 별도 전환 요청으로 전달합니다.
            if (relayTrigger == StoryRelayTrigger.MidTransition && session.PlayedHistory.Count > 0)
            {
                relayManager.Relay(
                    StoryRelayTrigger.MidTransition,
                    session.ScenarioPath,
                    session.PlayedHistory,
                    session.SelectedChoices,
                    statsSnapshot,
                    completedChapterIndex,
                    session.RunNumber,
                    influence);
            }
        }

        session.CompletedEpisodes.Clear();
        saveService.ClearCompletedEpisodes();

        session.ChapterIndex++;
        session.EpisodeIndex = 0;
        session.StoryIndex = 0;

        if (clearHistory)
        {
            session.PlayedHistory.Clear();
            session.SelectedChoices.Clear();
        }

        statContainer.ResetAllStats();
        // 다음 챕터가 이전 지형 위치에서 시작하지 않도록 진행도와 함께 위치를 초기화합니다.
        // 챕터 지형이 한 월드로 이어지므로 이전 챕터의 플레이어 위치를 그대로 보존합니다.
        // 증가된 다음 챕터 진행도와 현재 플레이어 위치를 하나의 체크포인트로 저장합니다.
        saveService.SaveCheckpoint(session, statContainer.stats, playerPosition);
    }
}
