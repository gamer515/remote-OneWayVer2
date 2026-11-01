/// <summary>
/// 챕터 결과 기록, 다음 챕터 준비와 저장 순서를 관리합니다.
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

    public bool PrepareBattleTransition(
        UnityEngine.Vector3 playerPosition,
        UnityEngine.Quaternion playerRotation)
    {
        if (!CanCompleteCurrentChapter())
            return false;

        CompleteCurrentChapter(
            clearHistory: false,
            playerPosition,
            playerRotation);
        return true;
    }

    public bool CompleteChapter(
        UnityEngine.Vector3 playerPosition,
        UnityEngine.Quaternion playerRotation)
    {
        if (!CanCompleteCurrentChapter())
            return false;

        CompleteCurrentChapter(
            clearHistory: true,
            playerPosition,
            playerRotation);
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
        bool clearHistory,
        UnityEngine.Vector3 playerPosition,
        UnityEngine.Quaternion playerRotation)
    {
        // Capture the completed chapter's stats before advancing progression.
        int completedChapterIndex = session.ChapterIndex;
        int[] statsSnapshot = statContainer.stats;
        if (relayManager != null)
        {
            foreach (CompletedEpisodeRecord episode in session.CompletedEpisodes)
            {
                if (episode == null) continue;
                int[] episodeStats = episode.finalStats ?? statsSnapshot;
                StoryInfluenceProfile influence = relayManager.CreateInfluenceProfile(
                    episodeStats,
                    completedChapterIndex,
                    session.RunNumber);
                saveService.SaveInfluenceProfile(influence);
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
            session.PlayedEncounterHistory.Clear();
        }

        if (!saveService.HasGuideAllocation) statContainer.ResetForEpisode();
        // 챕터 지형이 한 월드로 이어지므로 이전 챕터의 플레이어 위치를 그대로 보존합니다.
        // 증가된 다음 챕터 진행도와 현재 플레이어 위치를 하나의 체크포인트로 저장합니다.
        saveService.SaveCheckpoint(
            session, statContainer.stats, playerPosition, playerRotation);
    }
}
