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
    public int changeMagnitude;
    public int randomSeed;
}

[Serializable]
// 구형 세이브 읽기 호환 전용입니다. 현재 게임은 StoryEventRecord에 실제 선택/결과를 기록합니다.
public class BettingDecisionRecord
{
    public int dialogueId;
    public string dialogueText;
    public int[] coinCounts;
    public int[] statWeights;
    public int[] statChanges;
}

[Serializable]
public class PlayedEncounterCardRecord
{
    public string placeId;
    public string encounterPath;
    public int cardIndex;
    public Dialogue card;
}

[Serializable]
public class CompletedEpisodeRecord
{
    public string scenarioPath;
    public List<Dialogue> storyHistory = new List<Dialogue>();
    public List<PlayedEncounterCardRecord> encounterHistory = new List<PlayedEncounterCardRecord>();
    // 과거 저장 파일의 가중치 베팅 기록을 보존하기 위한 필드이며 새 플레이에서는 작성하지 않습니다.
    public List<BettingDecisionRecord> bettingDecisions = new List<BettingDecisionRecord>();
    public int[] finalStats;
    public int[] remainingCoins;
}
