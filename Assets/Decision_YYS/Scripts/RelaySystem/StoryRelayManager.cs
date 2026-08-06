using UnityEngine;
using System.Collections.Generic;
using System.Text;

public enum StoryRelayTrigger
{
    MidTransition,
    ChapterEnd
}

public class StoryRelayManager : MonoBehaviour
{
    [SerializeField] private PromptData promptData;

    /// <summary>
    /// 이야기 데이터를 필터링하고 요약하여 외부로 전송합니다.
    /// </summary>
    public void Relay(StoryRelayTrigger trigger, string currentFileName, List<Dialogue> history, int[] stats, int chapter)
    {
        if (promptData == null)
        {
            Debug.LogError("StoryRelayManager에 PromptData가 연결되지 않았습니다.", this);
            return;
        }

        string triggerType = trigger.ToString();
        // 전체 기록 대신 change=true인 핵심 지문만 보내 프롬프트 크기를 제한합니다.
        List<Dialogue> filtered = history != null
            ? history.FindAll(d => d != null && d.ShouldRelay)
            : new List<Dialogue>();
        
        string summary = BuildSummary(filtered);
        int maxStat = GetMaxStat(stats);
        string atmosphere = DetermineAtmosphere(maxStat);

        string template = (trigger == StoryRelayTrigger.MidTransition)
            ? promptData.midTransitionTemplate 
            : promptData.chapterEndTemplate;
            
        string finalPrompt = string.Format(template, summary, atmosphere, promptData.responseFormatTemplate);
        
        StoryPacket packet = new StoryPacket(triggerType, currentFileName, finalPrompt, filtered, stats, chapter);
        SendPacket(packet);
    }

    private int GetMaxStat(int[] stats)
    {
        if (stats == null || stats.Length == 0) return 0;
        int max = stats[0];
        for (int i = 1; i < stats.Length; i++)
        {
            if (stats[i] > max) max = stats[i];
        }
        return max;
    }

    private string DetermineAtmosphere(int maxStat)
    {
        if (promptData == null) return "알 수 없는 분위기";

        if (maxStat <= 4) return promptData.lowStatAtmosphere;
        else if (maxStat == 5) return promptData.midStatAtmosphere;
        else return promptData.highStatAtmosphere;
    }

    private string BuildSummary(List<Dialogue> dialogs)
    {
        if (dialogs == null || dialogs.Count == 0) return "(기록 없음)";
        
        StringBuilder sb = new StringBuilder();
        foreach (var d in dialogs)
        {
            sb.AppendLine($"- [ID: {d.id}] [{d.character}] {d.text}");
        }
        return sb.ToString();
    }

    private void SendPacket(StoryPacket packet)
    {
        if (AIAPIClient.Instance == null)
        {
            Debug.LogError("AIAPIClient 인스턴스를 찾을 수 없습니다.");
            return;
        }

        AIAPIClient.Instance.ProcessPacket(packet);
    }
}
