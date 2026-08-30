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
    public int cuttentPlaceIndex;
    public float[] currentPosition;
    // 마지막 챕터까지 끝낸 회차인지 기록해 중단 세이브와 완료 기록을 구분합니다.
    public bool isCompleted;
    public List<ChapterResult> chapterHistory = new List<ChapterResult>();
}

[System.Serializable]
public class ChapterResult
{
    public int chapterIndex;
    public int dominantStatIndex;
    public int dominantStatValue;
}

[System.Serializable]
public class PlayerStats 
{
    public int[] stats;
}
