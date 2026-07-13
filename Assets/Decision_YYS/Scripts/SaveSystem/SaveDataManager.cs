using UnityEngine;

public class SaveDataManager
{
    private ISaveSystem saveSystem;
    private GameProgress cachedProgress;

    public SaveDataManager(ISaveSystem system)
    {
        saveSystem = system;
        // 초기화 시점에 한 번 로드하여 메모리에 보관합니다.
        cachedProgress = saveSystem.Load<GameProgress>("Progress") ?? new GameProgress();
    }

    public void SaveProgress(int chapterIndex, int episodeIndex, int storyIndex)
    {
        cachedProgress.chapterIndex = chapterIndex;
        cachedProgress.episodeIndex = episodeIndex;
        cachedProgress.storyIndex = storyIndex;

        saveSystem.Save("Progress", cachedProgress);
        Debug.Log($"[Save] Progress Saved: Ch {chapterIndex}, Ep {episodeIndex}, St {storyIndex}");
    }

    public void SaveStats(int[] statsArray)
    {
        PlayerStats stats = new PlayerStats
        {
            stats = statsArray
        };
        saveSystem.Save("Stats", stats);
    }

    public void RecordChapterResult(int chapter, int bestIndex, int value)
    {
        // 메모리에 유지 중인 객체에 히스토리를 추가합니다.
        cachedProgress.chapterHistory.Add(new ChapterResult
        {
            chapterIndex = chapter,
            dominantStatIndex = bestIndex,
            dominantStatValue = value
        });

        saveSystem.Save("Progress", cachedProgress);
        Debug.Log($"[Save] Chapter {chapter} Result Recorded: BestStat {bestIndex} ({value})");
    }

    public GameProgress LoadProgress()
    {
        return cachedProgress;
    }

    public PlayerStats LoadStats()
    {
        if (saveSystem.Exists("Stats"))
        {
            return saveSystem.Load<PlayerStats>("Stats");
        }
        return null;
    }

    public bool HasSaveData(string key)
    {
        return saveSystem.Exists(key);
    }
}
