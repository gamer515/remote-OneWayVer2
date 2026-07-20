using UnityEngine;

public class SaveManager
{
    private GameProgress cachedProgress;

    public SaveManager()
    {
        // 초기화 시점에 한 번 로드하여 메모리에 보관합니다.
        cachedProgress = SaveIOService.Instance.Load<GameProgress>("Progress") ?? new GameProgress();
    }

    /// <summary>
    /// 게임 진행도 저장.
    /// </summary>
    /// <param name="chapterIndex">챕터 인덱스. 예: Initial, Martial, Wisdom</param>
    /// <param name="episodeIndex">각 챕터 내부 에피스도 인덱스.</param>
    /// <param name="storyIndex">내부 대화 인텍스.</param>
    public void SaveProgress(int chapterIndex, int episodeIndex, int storyIndex)
    {
        cachedProgress.chapterIndex = chapterIndex;
        cachedProgress.episodeIndex = episodeIndex;
        cachedProgress.storyIndex = storyIndex;

        SaveIOService.Instance.Save("Progress", cachedProgress);
        Debug.Log($"[Save] Progress Saved: Ch {chapterIndex}, Ep {episodeIndex}, St {storyIndex}");
    }

    public void SaveStats(int[] statsArray)
    {
        PlayerStats stats = new PlayerStats
        {
            stats = statsArray
        };
        SaveIOService.Instance.Save("Stats", stats);
    }

    /// <summary>
    /// 각 챕터마다 가장 높은 수치 저장.
    /// </summary>
    /// <param name="chapter">가장 수치가 높았던 챕터.</param>
    /// <param name="bestIndex">스탯 순서.</param>
    /// <param name="value">해당 스탯의 값.</param>
    public void RecordChapterResult(int chapter, int bestIndex, int value)
    {
        // 메모리에 유지 중인 객체에 히스토리를 추가합니다.
        cachedProgress.chapterHistory.Add(new ChapterResult
        {
            chapterIndex = chapter,
            dominantStatIndex = bestIndex,
            dominantStatValue = value
        });

        SaveIOService.Instance.Save("Progress", cachedProgress);
        Debug.Log($"[Save] Chapter {chapter} Result Recorded: BestStat {bestIndex} ({value})");
    }

    public GameProgress LoadProgress()
    {
        return cachedProgress;
    }

    public PlayerStats LoadStats()
    {
        if (SaveIOService.Instance.Exists("Stats"))
        {
            return SaveIOService.Instance.Load<PlayerStats>("Stats");
        }
        return null;
    }

    public bool HasSaveData(string key)
    {
        return SaveIOService.Instance.Exists(key);
    }
}
