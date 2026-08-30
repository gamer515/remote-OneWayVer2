using System;
using System.Collections.Generic;

[Serializable]
public class OmnibusData
{
    public List<ChapterInfo> chapters;
}

[Serializable]
public class ChapterInfo
{
    public string chapterId;
    public List<string> episodeIds;
}

//[Serializable]
//public class SideStoryInfo
//{
//    public List<string> Title;
//}
