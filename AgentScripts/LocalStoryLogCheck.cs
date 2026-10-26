using System;
using UnityEngine;

public static class LocalStoryLogCheck
{
    public static string Verify()
    {
        int count=0;
        foreach(LocalStoryLogStage stage in Enum.GetValues(typeof(LocalStoryLogStage)))
        {
            string text=LocalStoryLog.Format(stage,"로그 표시 검사 / 실제 생성 아님");
            if(!text.Contains("[LocalStory][생성 ") || !text.Contains("<b>") ||
                !text.EndsWith("</b></color>"))
                throw new Exception("Stage formatting failed: "+stage);
            // Review and deferred work intentionally share the warning colour.
            count++;
        }
        string escaped=LocalStoryLog.Format(LocalStoryLogStage.Failed,"<color=green>원문</color>");
        if(escaped.Contains("<color=green>") || !escaped.Contains("＜color=green＞"))
            throw new Exception("Message changed stage colour");
        count++;
        string observed=null; LogType observedType=LogType.Error;
        Application.LogCallback capture=(message,stack,type) => {
            if(message.Contains("로그 표시 검사 / 실제 생성 아님")) { observed=message; observedType=type; }
        };
        Application.logMessageReceived+=capture;
        try
        {
            LocalStoryLog.Write(LocalStoryLogStage.Complete,"로그 표시 검사 / 실제 생성 아님");
            if(observed==null || observedType!=LogType.Log || !observed.Contains("#74E892"))
                throw new Exception("Completion emission failed");
            count++;
            LocalStoryLog.Write(LocalStoryLogStage.Failed,"로그 표시 검사 / 실제 생성 아님");
            if(observedType!=LogType.Warning || !observed.Contains("#FF7777"))
                throw new Exception("Failure severity/colour failed");
            count++;
            foreach(bool external in new[]{false,true})
            {
                var report=new LocalStoryTestReport {model=external?"OmniRoute / explain":"Gemma 3 4B",storyPath="로그 검사"};
                LocalStoryLog.Generation(LocalStoryLogStage.Working,report,"로그 표시 검사 / 실제 생성 아님");
                if(observedType!=LogType.Log || !observed.Contains(external?"[외부 통신]":"[내장형]") || !observed.Contains(report.model))
                    throw new Exception("Generation transport/model missing from emitted log");
                count++;
            }
        }
        finally { Application.logMessageReceived-=capture; }
        return count+" log checks passed; no generation/gameplay/save changes.";
    }
}
