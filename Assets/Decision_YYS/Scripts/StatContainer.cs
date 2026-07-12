using TMPro;
using UnityEngine;
using System;
using System.Collections.Generic;

public class StatContainer : MonoBehaviour
{
    [Serializable]
    public class StatEntry
    {
        public string statName; // 인스펙터 표시용 이름 (Wealth, Strength 등)
        public Constants.StatType statType;
        public int value;
        
        [Header("UI References")]
        public TextMeshProUGUI nameText;  // 스탯 이름을 표시할 텍스트
        public TextMeshProUGUI valueText; // 스탯 수치를 표시할 텍스트
    }

    [Header("Stats Configuration")]
    [SerializeField] private List<StatEntry> statEntries = new List<StatEntry>();

    [Header("Transition Settings")]
    [SerializeField] private int targetStatThreshold = 10;
    public event Action OnTargetStatReached;

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
    /// 모든 스탯을 0으로 초기화하고 UI를 갱신합니다.
    /// </summary>
    public void ResetAllStats()
    {
        foreach (var entry in statEntries)
        {
            entry.value = 0;
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
            if (entry.valueText != null) entry.valueText.text = entry.value.ToString();
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
