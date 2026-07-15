using System.Collections.Generic;

[System.Serializable]
public class ChapterResult
{
    public int chapterIndex;
    public int dominantStatIndex;
    public int dominantStatValue;
}

[System.Serializable]
public class GameProgress 
{
    public int bookmarkIndex;
    public int chapterIndex;
    public int episodeIndex;
    public int storyIndex;
    public List<ChapterResult> chapterHistory = new List<ChapterResult>();
}

[System.Serializable]
public class PlayerStats 
{
    public int[] stats;
}
