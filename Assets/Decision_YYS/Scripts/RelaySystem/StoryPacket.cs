using System;
using System.Collections.Generic;

public enum StoryChangeIntensity
{
    Subtle,
    Moderate,
    Strong
}

[Serializable]
public class StoryInfluenceProfile
{
    public int chapterIndex;
    public int sourceRun;
    public StoryChangeIntensity intensity;
    public int[] scores;
    public int majorWeaknessIndex;
    public string majorWeaknessName;
    public int majorWeaknessValue;
    public int minorWeaknessIndex;
    public string minorWeaknessName;
    public int minorWeaknessValue;
    public int totalStatValue;
    public int randomSeed;
}

[Serializable]
public class ChoiceSelectionRecord
{
    public int dialogueId;
    public int optionIndex;
    public string optionText;
    public int statChange;
}

[Serializable]
public class CompletedEpisodeRecord
{
    public string scenarioPath;
    public List<Dialogue> storyHistory = new List<Dialogue>();
    public List<ChoiceSelectionRecord> selectedChoices = new List<ChoiceSelectionRecord>();
}

[Serializable]
public class StoryPacket
{
    public string fileName;    // 현재 수정 대상이 되는 JSON 파일명/경로
    public string finalPrompt; // 템플릿과 데이터가 결합된 최종 문구
    public List<Dialogue> storyHistory; // 전달되는 지문 리스트 (필터링되었거나 전체이거나)
    public int sourceRun;
    public int targetRun;

    public StoryPacket(
        string file,
        string prompt,
        List<Dialogue> history,
        int sourceRunNumber)
    {
        fileName = file;
        finalPrompt = prompt;
        storyHistory = history;
        sourceRun = Math.Max(1, sourceRunNumber);
        targetRun = sourceRun + 1;
    }
}
