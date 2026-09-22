using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어의 네 능력치 데이터와 임계값 이벤트만 관리합니다.
/// UI 표시 책임은 갖지 않습니다.
/// </summary>
public class StatContainer : MonoBehaviour
{
    private static readonly string[] StatNames =
        { "Health", "Speed", "Intelligence", "Charm" };

    [Serializable]
    public class StatEntry
    {
        public string statName;
        public int value;
    }

    [Header("Stats Configuration")]
    [SerializeField] private List<StatEntry> statEntries = new List<StatEntry>();
    [Tooltip("새 게임과 새 에피소드에서 시작할 네 능력치의 기본값입니다.")]
    [SerializeField] private int episodeStartValue = 5;

    [Header("Transition Settings")]
    [SerializeField] private int targetStatThreshold = 10;

    public event Action OnTargetStatReached;

    public int[] stats
    {
        get
        {
            int[] values = new int[statEntries.Count];
            for (int i = 0; i < statEntries.Count; i++)
                values[i] = statEntries[i].value;

            return values;
        }
    }

    public void AddStat(int index, int amount)
    {
        if (index < 0 || index >= statEntries.Count)
            return;

        bool wasBelowThreshold = statEntries[index].value < targetStatThreshold;
        statEntries[index].value += amount;

        if (wasBelowThreshold && statEntries[index].value >= targetStatThreshold)
            OnTargetStatReached?.Invoke();
    }

    public void AddStats(int[] changes)
    {
        if (changes == null)
            return;

        int count = Mathf.Min(changes.Length, statEntries.Count);
        bool reachedThreshold = false;

        for (int i = 0; i < count; i++)
        {
            bool wasBelowThreshold = statEntries[i].value < targetStatThreshold;
            statEntries[i].value += changes[i];
            reachedThreshold |=
                wasBelowThreshold && statEntries[i].value >= targetStatThreshold;
        }

        if (reachedThreshold)
            OnTargetStatReached?.Invoke();
    }

    public void SetStats(int[] newStats)
    {
        if (newStats == null)
            return;

        int count = Mathf.Min(newStats.Length, statEntries.Count);
        for (int i = 0; i < count; i++)
            statEntries[i].value = newStats[i];
    }

    public void SetChapterStatNames(string chapterName)
    {
        int count = Mathf.Min(statEntries.Count, StatNames.Length);
        for (int i = 0; i < count; i++)
            statEntries[i].statName = StatNames[i];
    }

    public void ResetForEpisode()
    {
        foreach (StatEntry entry in statEntries)
            entry.value = episodeStartValue;
    }

    // 기존 호출부와의 호환성을 유지하며 임계값만 확인합니다.
    public void UpdateStat(int index)
    {
        if (index >= 0 &&
            index < statEntries.Count &&
            statEntries[index].value >= targetStatThreshold)
        {
            OnTargetStatReached?.Invoke();
        }
    }
}
