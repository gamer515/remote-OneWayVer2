using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DecisionScene에서 사용하는 런타임 진행 상태입니다.
/// </summary>
public sealed class DecisionSession
{
    public OmnibusData Omnibus { get; }
    public ScenarioData Scenario { get; set; }
    public int ChapterIndex { get; set; }
    public int EpisodeIndex { get; set; }
    public int StoryIndex { get; set; }
    public string ScenarioPath { get; set; }
    public List<Dialogue> PlayedHistory { get; } = new List<Dialogue>();

    public DecisionSession(
        OmnibusData omnibus,
        int chapterIndex,
        int episodeIndex,
        int storyIndex)
    {
        Omnibus = omnibus;
        ChapterIndex = chapterIndex;
        EpisodeIndex = episodeIndex;
        StoryIndex = storyIndex;
    }
}

/// <summary>
/// GameManager가 DecisionManager에 전달하는 초기 의존성과 데이터입니다.
/// </summary>
public sealed class DecisionStartData
{
    public DecisionSession Session { get; }
    public Player Player { get; }
    public PlayerStats PlayerStats { get; }
    public SaveManager SaveManager { get; }
    public Vector3? SavedPlayerPosition { get; }

    public DecisionStartData(
        DecisionSession session,
        Player player,
        PlayerStats playerStats,
        SaveManager saveManager,
        Vector3? savedPlayerPosition)
    {
        Session = session;
        Player = player;
        PlayerStats = playerStats;
        SaveManager = saveManager;
        SavedPlayerPosition = savedPlayerPosition;
    }
}
