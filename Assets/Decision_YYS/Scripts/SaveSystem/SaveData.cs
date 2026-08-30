using System.Collections.Generic;

[System.Serializable]
public class ProfileData
{
    // 저장 형식이 바뀔 때 기존 세이브를 변환하기 위한 버전입니다.
    public int schemaVersion = 1;

    // 다음 단계에서 회차 전환 기능이 이 값을 변경합니다.
    public int currentRun = 1;
}

[System.Serializable]
public class GameProgress 
{
    public int chapterIndex;
    public int episodeIndex;
    public int storyIndex;
    public float[] currentPosition;
    // 마지막 챕터까지 끝낸 회차인지 기록해 중단 세이브와 완료 기록을 구분합니다.
    public bool isCompleted;
    public List<StoryInfluenceProfile> influenceHistory = new List<StoryInfluenceProfile>();
    public List<CompletedEpisodeRecord> pendingEpisodes = new List<CompletedEpisodeRecord>();
}

[System.Serializable]
public class PlayerStats
{
    public int[] stats;
}

public enum ContentGenerationStatus
{
    Pending,
    Generating,
    Ready,
    Failed
}

[System.Serializable]
public class GeneratedEpisodeStatus
{
    public string scenarioPath;
    // JSON을 직접 확인할 때 상태를 알아볼 수 있도록 enum 이름을 문자열로 저장합니다.
    public string status;
    public string errorMessage;
}

[System.Serializable]
public class GeneratedContentManifest
{
    public int sourceRun;
    public int targetRun;
    public List<GeneratedEpisodeStatus> episodes = new List<GeneratedEpisodeStatus>();
}
