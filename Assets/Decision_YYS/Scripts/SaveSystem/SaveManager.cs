using UnityEngine;

public class SaveManager
{
    private readonly ProfileData profile;
    private GameProgress cachedProgress;

    public int CurrentRun => profile.currentRun;

    public SaveManager()
    {
        // 프로필의 현재 회차를 기준으로 진행도와 능력치의 저장 폴더를 고정합니다.
        profile = SaveIOService.Instance.LoadOrCreateProfile();
        cachedProgress = SaveIOService.Instance.LoadRunData<GameProgress>(CurrentRun, "Progress")
            ?? new GameProgress();
    }

    /// <summary>
    /// 현재 스토리 진행도를 저장합니다.
    /// </summary>
    /// <param name="chapterIndex">챕터 인덱스. 예: Initial, Combat, Knowledge</param>
    /// <param name="episodeIndex">챕터 내부 에피소드 인덱스.</param>
    /// <param name="storyIndex">에피소드 내부 지문 인덱스.</param>
    public void SaveProgress(int chapterIndex, int episodeIndex, int storyIndex)
    {
        cachedProgress.chapterIndex = chapterIndex;
        cachedProgress.episodeIndex = episodeIndex;
        cachedProgress.storyIndex = storyIndex;

        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
        Debug.Log($"[Save] Progress Saved: Ch {chapterIndex}, Ep {episodeIndex}, St {storyIndex}");
    }

    public void SaveStats(int[] statsArray)
    {
        PlayerStats stats = new PlayerStats
        {
            stats = statsArray
        };
        SaveIOService.Instance.SaveRunData(CurrentRun, "Stats", stats);
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

        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
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

        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
        Debug.Log($"[Save] Chapter {chapter} Result Recorded: BestStat {bestIndex} ({value})");
    }

    public GameProgress LoadProgress()
    {
        return cachedProgress;
    }

    public PlayerStats LoadStats()
    {
        if (SaveIOService.Instance.RunDataExists(CurrentRun, "Stats"))
        {
            return SaveIOService.Instance.LoadRunData<PlayerStats>(CurrentRun, "Stats");
        }
        return null;
    }

    public bool HasSaveData(string key)
    {
        return SaveIOService.Instance.RunDataExists(CurrentRun, key);
    }

    /// <summary>
    /// 현재 회차를 완료 상태로 저장합니다. 여러 번 호출되어도 회차 번호는 증가하지 않습니다.
    /// </summary>
    public void CompleteCurrentRun()
    {
        if (cachedProgress.isCompleted)
            return;

        cachedProgress.isCompleted = true;
        SaveIOService.Instance.SaveRunData(CurrentRun, "Progress", cachedProgress);
        Debug.Log($"[Save] {CurrentRun}회차 완료 상태 저장");
    }

    /// <summary>
    /// 기존 회차를 보존하고 다음 회차의 빈 진행도를 만든 뒤 활성 회차를 변경합니다.
    /// 실제 씬 재시작은 이 메서드를 호출하는 화면 흐름에서 담당합니다.
    /// </summary>
    public int StartNextRun()
    {
        CompleteCurrentRun();

        int nextRun = CurrentRun + 1;
        GameProgress nextProgress = new GameProgress();

        // 다음 회차 파일을 먼저 만든 뒤 Profile을 갱신하여 불완전한 회차를 가리키지 않게 합니다.
        SaveIOService.Instance.SaveRunData(nextRun, "Progress", nextProgress);
        SaveIOService.Instance.SaveRunData(nextRun, "Stats", new PlayerStats());
        profile.currentRun = nextRun;
        SaveIOService.Instance.SaveProfile(profile);
        cachedProgress = nextProgress;

        Debug.Log($"[Save] {nextRun}회차 저장 데이터 생성 및 활성화 완료");
        return nextRun;
    }
}
