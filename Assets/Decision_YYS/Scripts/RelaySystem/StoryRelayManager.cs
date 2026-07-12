using UnityEngine;
using System.Collections.Generic;
using System.Text;

public class StoryRelayManager : MonoBehaviour
{
    [SerializeField] private PromptData promptData;

    /// <summary>
    /// 이야기 데이터를 필터링하고 요약하여 외부로 전송합니다.
    /// </summary>
    /// <param name="triggerType">"MidTransition" 또는 "ChapterEnd"</param>
    public void Relay(string triggerType, string currentFileName, List<Dialogue> history, int[] stats, int chapter)
    {
        // 1. 'change'가 "true"인 지문만 필터링 (핵심 지문만 압축)
        List<Dialogue> filtered = history.FindAll(d => d.change != null && d.change.ToLower() == "true");
        
        // 2. 텍스트 요약 생성 (ID 포함)
        string summary = BuildSummary(filtered);

        // 3. 가장 높은 스탯 찾기 및 분위기 결정
        int maxStat = GetMaxStat(stats);
        string atmosphere = DetermineAtmosphere(maxStat);
        
        // 4. 트리거 타입에 따른 템플릿 선택 및 프롬프트 결합
        string template = (triggerType == "MidTransition") 
            ? promptData.midTransitionTemplate 
            : promptData.chapterEndTemplate;
            
        string finalPrompt = string.Format(template, summary, atmosphere, promptData.responseFormatTemplate);
        
        // 5. 패킷 생성 (파일명 포함)
        StoryPacket packet = new StoryPacket(triggerType, currentFileName, finalPrompt, filtered, stats, chapter);
        
        // 6. 전송 시뮬레이션
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
        // 모든 데이터를 포함한 패킷을 AI 매니저에게 전달
        AIManager.Instance.ProcessPacket(packet);
    }
}
