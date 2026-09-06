/// <summary>
/// DecisionScene의 런타임 상태를 SaveManager 형식으로 변환해 저장합니다.
/// </summary>
public sealed class DecisionSaveService
{
    private readonly SaveManager saveManager;

    public DecisionSaveService(SaveManager saveManager)
    {
        this.saveManager = saveManager;
    }

    public void SaveProgress(DecisionSession session)
    {
        if (session == null)
            return;

        saveManager.SaveProgress(
            session.ChapterIndex,
            session.EpisodeIndex,
            session.StoryIndex);
    }

    public void SaveProgress(DecisionSession session, UnityEngine.Vector3 playerPosition)
    {
        if (session == null)
            return;

        saveManager.SaveProgressAndPlayerPosition(
            session.ChapterIndex,
            session.EpisodeIndex,
            session.StoryIndex,
            playerPosition);
    }

    public void SaveStats(int[] stats)
    {
        saveManager.SaveStats(stats);
    }

    public void SaveCheckpoint(
        DecisionSession session,
        int[] stats,
        UnityEngine.Vector3 playerPosition)
    {
        // 씬 전환 체크포인트는 진행도와 위치를 함께 저장해 서로 다른 시점의 값이 섞이지 않게 합니다.
        SaveProgress(session, playerPosition);
        SaveStats(stats);
    }

    public void ClearPlayerPosition()
    {
        saveManager.ClearPlayerPosition();
    }

    public void SaveRemainingCoins(int[] remainingCoins)
    {
        saveManager.SaveRemainingCoins(remainingCoins);
    }

    public void SaveInfluenceProfile(StoryInfluenceProfile profile)
    {
        saveManager.RecordInfluenceProfile(profile);
    }

    public void SaveCompletedEpisode(CompletedEpisodeRecord episode)
    {
        saveManager.SaveCompletedEpisode(episode);
    }

    public void ClearCompletedEpisodes()
    {
        saveManager.ClearPendingEpisodes();
    }

    public void CompleteCurrentRun()
    {
        saveManager.CompleteCurrentRun();
    }

    public int StartNextRun()
    {
        return saveManager.StartNextRun();
    }
}
