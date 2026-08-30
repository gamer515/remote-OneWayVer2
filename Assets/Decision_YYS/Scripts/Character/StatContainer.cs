using TMPro;
using UnityEngine;
using System;
using System.Collections.Generic;

public class StatContainer : MonoBehaviour
{
    private static readonly string[] InitialStatNames =
        { string.Empty, string.Empty, string.Empty, string.Empty };

    private static readonly string[] CombatStatNames =
        { "Strength", "Technique", "Tactics", "Strategy" };

    private static readonly string[] KnowledgeStatNames =
        { "Intuition", "Learning", "Magic", "Wisdom" };

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

    [Header("Transition Settings")]
    [SerializeField] private int targetStatThreshold = 10;
    public event Action OnTargetStatReached;

    private string[] initialStatNames;

    private void Awake()
    {
        // Initial 챕터로 돌아오거나 새 게임을 시작할 때 사용할 기본 라벨을 보존합니다.
        initialStatNames = new string[statEntries.Count];
        for (int i = 0; i < statEntries.Count; i++)
            initialStatNames[i] = statEntries[i].statName;
    }

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
    /// 스탯 값과 인덱스는 유지하고 현재 챕터에 맞는 화면 표시 이름만 변경합니다.
    /// </summary>
    public void SetChapterStatNames(string chapterName)
    {
        string[] names;
        if (string.Equals(chapterName, Constants.Chapter.Combat.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            names = CombatStatNames;
        }
        else if (string.Equals(chapterName, Constants.Chapter.Knowledge.ToString(),
                     StringComparison.OrdinalIgnoreCase))
        {
            names = KnowledgeStatNames;
        }
        else if (string.Equals(chapterName, Constants.Chapter.Initial.ToString(),
                     StringComparison.OrdinalIgnoreCase))
        {
            names = InitialStatNames;
        }
        else
        {
            // 알 수 없는 챕터는 Inspector에 설정된 기존 네 이름을 사용합니다.
            names = initialStatNames;
        }

        int count = Mathf.Min(statEntries.Count, names.Length);
        for (int i = 0; i < count; i++)
            statEntries[i].statName = names[i];

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
