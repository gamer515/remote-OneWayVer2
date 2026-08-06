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

    public void SaveCheckpoint(DecisionSession session, int[] stats)
    {
        SaveProgress(session);
        SaveStats(stats);
    }

    public void ClearPlayerPosition()
    {
        saveManager.ClearPlayerPosition();
    }

    public void SaveChapterResult(ChapterResult result)
    {
        if (result == null)
            return;

        saveManager.RecordChapterResult(
            result.chapterIndex,
            result.dominantStatIndex,
            result.dominantStatValue);
    }
}
