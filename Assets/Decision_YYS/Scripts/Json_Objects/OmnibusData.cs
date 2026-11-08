using System;
using System.Collections.Generic;

[Serializable]
public class OmnibusData
{
    public List<ChapterInfo> chapters;

    // 진행 상한만 지정합니다. 이후 챕터 데이터는 삭제하거나 목록에서 제외하지 않습니다.
    // 비워 두면 기존처럼 모든 챕터를 진행합니다.
    public string lastPlayableChapterId;
    public string firstPlayableChapterId;
    public string[] legacyChapterIds;

    public int FirstPlayableChapterIndex
    {
        get
        {
            if (string.IsNullOrWhiteSpace(firstPlayableChapterId)) return 0;
            int index = chapters?.FindIndex(chapter => chapter?.chapterId == firstPlayableChapterId) ?? -1;
            if (index < 0 || index >= PlayableChapterCount)
                throw new InvalidOperationException("시작 챕터가 진행 가능 범위에 없습니다: " + firstPlayableChapterId);
            return index;
        }
    }

    public bool IsProgressionLimited => !string.IsNullOrWhiteSpace(lastPlayableChapterId);

    public int PlayableChapterCount
    {
        get
        {
            if (chapters == null) return 0;
            if (!IsProgressionLimited) return chapters.Count;
            int index = chapters.FindIndex(chapter => chapter != null &&
                string.Equals(chapter.chapterId, lastPlayableChapterId, StringComparison.OrdinalIgnoreCase));
            // 잘못된 상한으로 이후 챕터까지 열리지 않도록 검증 실패(0개)로 처리합니다.
            return index < 0 ? 0 : index + 1;
        }
    }
}

[Serializable]
public class ChapterInfo
{
    public string chapterId;
    public List<string> episodeIds;
    public string rulesDocument;
    public string[] rulesEpisodes;
    public string previewEndEpisodeId;
    public int bossStage;
    public bool stopAtEnd;
}

//[Serializable]
//public class SideStoryInfo
//{
//    public List<string> Title;
//}
