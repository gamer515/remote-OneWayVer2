using System;
using System.Collections.Generic;

[Serializable]
public class StoryPacket
{
    public string triggerType; // "MidTransition" 또는 "ChapterEnd"
    public string fileName;    // 현재 수정 대상이 되는 JSON 파일명/경로
    public string finalPrompt; // 템플릿과 데이터가 결합된 최종 문구
    public List<Dialogue> storyHistory; // 전달되는 지문 리스트 (필터링되었거나 전체이거나)
    public int[] stats; // 전송 시점의 성향 수치
    public int chapterIndex;

    public StoryPacket(string type, string file, string prompt, List<Dialogue> history, int[] currentStats, int chapter)
    {
        triggerType = type;
        fileName = file;
        finalPrompt = prompt;
        storyHistory = history;
        stats = currentStats;
        chapterIndex = chapter;
    }
}
