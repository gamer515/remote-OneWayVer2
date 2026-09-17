using TMPro;
using UnityEngine;
using System;
using System.Collections.Generic;

public class StatContainer : MonoBehaviour
{
    // 네 코인은 챕터와 무관하게 같은 능력치를 가리킵니다.
    private static readonly string[] StatNames =
        { "Health", "Speed", "Intelligence", "Charm" };

    [Serializable]
    public class StatEntry
    {
        public string statName; // 현재 챕터의 같은 인덱스에 해당하는 표시 이름
        public int value;
        
        [Header("UI References")]
        public TextMeshProUGUI nameText;  // 스탯 이름을 표시할 텍스트
        public TextMeshProUGUI valueText; // 스탯 수치를 표시할 텍스트
    }

    [Header("Stats Configuration")]
    [SerializeField] private List<StatEntry> statEntries = new List<StatEntry>();
    [Tooltip("새 게임과 새 에피소드에서 시작할 네 능력치의 기본값입니다.")]
    [SerializeField] private int episodeStartValue = 5;

    [Header("Transition Settings")]
    [SerializeField] private int targetStatThreshold = 10;
    public event Action OnTargetStatReached;

    [Header("UI Display Settings")]
    [Tooltip("실제 수치는 제한하지 않고, 이 값을 넘으면 UI에 최대값+로 표시합니다.")]
    [SerializeField, Min(1)] private int displayStatMaximum = 10;

    /// <summary>
    /// 외부에서 읽기 전용으로 스탯 배열을 가져옵니다. (호환성 유지)
    /// </summary>
    public int[] stats
    {
        get
        {
            int[] arr = new int[statEntries.Count];
            for (int i = 0; i < statEntries.Count; i++)
            {
                arr[i] = statEntries[i].value;
            }
            return arr;
        }
    }

    private void Start()
    {
        RefreshAllUI();
    }

    /// <summary>
    /// 특정 인덱스의 스탯을 증가시키고 UI를 갱신합니다.
    /// </summary>
    public void AddStat(int index, int amount)
    {
        if (index >= 0 && index < statEntries.Count)
        {
            statEntries[index].value += amount;
            UpdateStatUI(index);

            // 목표 수치 도달 여부 체크
            if (statEntries[index].value >= targetStatThreshold)
            {
                OnTargetStatReached?.Invoke();
            }
        }
    }

    /// <summary>
    /// 한 번의 선택에서 계산된 네 능력치 변화를 모두 적용합니다.
    /// 음수 변화와 UI 표시 상한을 넘는 실제 값도 그대로 보존합니다.
    /// </summary>
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
            UpdateStatUI(i);
            reachedThreshold |=
                wasBelowThreshold && statEntries[i].value >= targetStatThreshold;
        }

        if (reachedThreshold)
            OnTargetStatReached?.Invoke();
    }

    /// <summary>
    /// 모든 스탯을 새로운 배열 값으로 설정하고 UI를 갱신합니다. (로드 시 사용)
    /// </summary>
    public void SetStats(int[] newStats)
    {
        if (newStats == null) return;
        for (int i = 0; i < Mathf.Min(newStats.Length, statEntries.Count); i++)
        {
            statEntries[i].value = newStats[i];
        }
        RefreshAllUI();
    }

    /// <summary>
    /// 저장된 스탯 인덱스는 유지하고 네 가지 고정 능력치 이름을 표시합니다.
    /// </summary>
    public void SetChapterStatNames(string chapterName)
    {
        int count = Mathf.Min(statEntries.Count, StatNames.Length);
        for (int i = 0; i < count; i++)
            statEntries[i].statName = StatNames[i];

        RefreshAllUI();
    }

    /// <summary>
    /// 모든 스탯을 에피소드 시작값으로 초기화하고 UI를 갱신합니다.
    /// </summary>
    public void ResetForEpisode()
    {
        foreach (var entry in statEntries)
        {
            entry.value = episodeStartValue;
        }
        RefreshAllUI();
    }

    /// <summary>
    /// 특정 인덱스의 UI를 갱신합니다. (이름과 수치 각각 반영)
    /// </summary>
    private void UpdateStatUI(int index)
    {
        if (index >= 0 && index < statEntries.Count)
        {
            var entry = statEntries[index];
            if (entry.nameText != null) entry.nameText.text = entry.statName;
            if (entry.valueText != null)
            {
                entry.valueText.text = entry.value > displayStatMaximum
                    ? $"{displayStatMaximum}+"
                    : entry.value.ToString();
            }
        }
    }

    /// <summary>
    /// 모든 스탯 UI를 현재 데이터에 맞춰 갱신합니다.
    /// </summary>
    public void RefreshAllUI()
    {
        for (int i = 0; i < statEntries.Count; i++)
        {
            UpdateStatUI(i);
        }
    }

    // [호환성용] 기존 UpdateStat 호출 대응
    public void UpdateStat(int index)
    {
        UpdateStatUI(index);
        if (index >= 0 && index < statEntries.Count && statEntries[index].value >= targetStatThreshold)
        {
            OnTargetStatReached?.Invoke();
        }
    }
}
