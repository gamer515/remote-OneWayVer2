using UnityEngine;

public class SaveManager
{
    private GameProgress cachedProgress;

    public SaveManager()
    {
        // 진행도와 챕터 기록이 서로 덮어쓰이지 않도록 동일 인스턴스를 캐시합니다.
        cachedProgress = SaveIOService.Instance.Load<GameProgress>("Progress") ?? new GameProgress();
    }

    /// <summary>
    /// 현재 스토리 진행도를 저장합니다.
    /// </summary>
    /// <param name="chapterIndex">챕터 인덱스. 예: Initial, Strength, Wisdom</param>
    /// <param name="episodeIndex">챕터 내부 에피소드 인덱스.</param>
    /// <param name="storyIndex">에피소드 내부 지문 인덱스.</param>
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

    public void SaveProgressAndPlayerPosition(
        int chapterIndex,
        int episodeIndex,
        int storyIndex,
        Vector3 position)
    {
        // 지문 인덱스와 목표 위치는 같은 체크포인트이므로 한 번의 파일 쓰기로 저장합니다.
        cachedProgress.chapterIndex = chapterIndex;
        cachedProgress.episodeIndex = episodeIndex;
        cachedProgress.storyIndex = storyIndex;
        cachedProgress.currentPosition = new[] { position.x, position.y, position.z };

        SaveIOService.Instance.Save("Progress", cachedProgress);
        Debug.Log(
            $"[Save] Checkpoint Saved: Ch {chapterIndex}, Ep {episodeIndex}, " +
            $"St {storyIndex}, Z {position.z}");
    }

    public Vector3? LoadPlayerPosition()
    {
        float[] position = cachedProgress.currentPosition;
        if (position == null || position.Length < 3)
            return null;

        for (int i = 0; i < 3; i++)
        {
            if (float.IsNaN(position[i]) || float.IsInfinity(position[i]))
                return null;
        }

        return new Vector3(position[0], position[1], position[2]);
    }

    public void ClearPlayerPosition()
    {
        cachedProgress.currentPosition = null;
    }

    /// <summary>
    /// 완료한 챕터의 대표 스탯 결과를 기록합니다.
    /// </summary>
    /// <param name="chapter">완료한 챕터 인덱스.</param>
    /// <param name="bestIndex">대표 스탯 인덱스.</param>
    /// <param name="value">대표 스탯 값.</param>
    public void RecordChapterResult(int chapter, int bestIndex, int value)
    {
        if (cachedProgress.chapterHistory == null)
            cachedProgress.chapterHistory = new System.Collections.Generic.List<ChapterResult>();

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
