using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Explicit test-result promotion only. Inference never calls this class automatically.</summary>
public static class LocalStoryGameApply
{
    public static int NextRun(LocalStoryTestReport report)
    {
        string profile=Path.Combine(Application.persistentDataPath,"Saves","Profile.json");
        int active=File.Exists(profile)?JsonUtility.FromJson<ProfileData>(File.ReadAllText(profile)).currentRun:1;
        return Math.Max(Math.Max(2,active+1),report?.targetRun??2);
    }
    public static EncounterCardRoot Build(LocalStoryTestReport report,string stage,bool useEdit,string editPath=null)
    {
        if(report==null || (stage!="draft" && stage!="correction")) throw new ArgumentException("결과와 초안/교정 단계를 선택하세요.");
        var asset=Resources.Load<TextAsset>("Story_Json_Data/"+report.storyPath+"/Story");
        if(asset==null) throw new InvalidOperationException("현재 원문을 찾을 수 없습니다.");
        var original=JsonUtility.FromJson<EncounterCardRoot>(asset.text);
        if(report.original?.MainStory?.Count!=original.MainStory?.Count) throw new InvalidOperationException("생성 당시와 현재 원문의 카드 수가 다릅니다.");
        for(int i=0;i<original.MainStory.Count;i++)
        {
            var old=JsonUtility.FromJson<EncounterCard>(JsonUtility.ToJson(report.original.MainStory[i]));
            var now=JsonUtility.FromJson<EncounterCard>(JsonUtility.ToJson(original.MainStory[i]));
            // Core annotations can be added after an old test. Executable bindings must still match.
            old.text=now.text=null; old.options=now.options=null; old.coreMeaning=now.coreMeaning=null;
            StoryChoiceRoles.FillMissingMetadata(old,now);
            if(JsonUtility.ToJson(old)!=JsonUtility.ToJson(now))
                throw new InvalidOperationException("현재 원문과 생성 당시의 기능/분기 연결이 다릅니다: "+i);
        }
        string patch;
        if(useEdit)
        {
            if(!File.Exists(editPath)) throw new FileNotFoundException("번호별 Edit 파일이 없습니다.",editPath);
            var lines=JObject.Parse(File.ReadAllText(editPath))[stage] as JArray;
            if(lines==null || lines.Count!=original.MainStory.Count) throw new FormatException("Edit "+stage+"의 카드 수가 원문과 다릅니다.");
            var cards=new JArray();
            for(int i=0;i<lines.Count;i++)
            {
                if(!(lines[i] is JObject line) || line.Properties().Any(p=>!new[]{"speaker","text","options"}.Contains(p.Name)) ||
                    line["speaker"]?.Type!=JTokenType.String || line["text"]?.Type!=JTokenType.String)
                    throw new FormatException("Edit은 speaker/text와 options만 사용합니다: "+i);
                var card=original.MainStory[i];
                string id=card.speakerId;
                if(string.IsNullOrEmpty(id)) id=report.storyPath.Contains("/guide")?"Guide":report.storyPath.Contains("/duelist")?"Duelist":"Gambler";
                var person=Resources.Load<TextAsset>("Story_Json_Data/Characters/"+id);
                string name=person==null?id:JsonUtility.FromJson<StoryCharacter>(person.text).name;
                if((string)line["speaker"]!=id && (string)line["speaker"]!=name)
                    throw new FormatException("Edit 화자가 현재 카드와 다릅니다: "+i+" / "+name);
                var options=line["options"] as JArray??new JArray();
                if((line["options"]!=null && !(line["options"] is JArray)) || options.Any(o=>o.Type!=JTokenType.String))
                    throw new FormatException("Edit options는 문자열 배열입니다: "+i);
                cards.Add(new JObject{["index"]=i,["text"]=line["text"].DeepClone(),["options"]=options.DeepClone()});
            }
            patch=new JObject{["cards"]=cards}.ToString(Newtonsoft.Json.Formatting.None);
        }
        else patch=stage=="draft"?report.rawOutput:report.correctionRawOutput;
        if(!LocalStoryRewrite.TryBuild(patch,original,report.interactionMemory,out var built,out string error))
            throw new FormatException("게임 적용 검사 실패: "+error);
        return built;
    }
    public static string Save(LocalStoryTestReport report,string stage,bool useEdit,int targetRun,
        SaveIOService storage,string editPath=null)
    {
        if(targetRun<2 || storage==null) throw new ArgumentException("적용 회차는 2 이상이어야 합니다.");
        var built=Build(report,stage,useEdit,editPath);
        if(!storage.SaveGeneratedContentForTest(targetRun,"Encounters",report.storyPath+"/Story",built))
            throw new IOException("게임 확인용 저장에 실패했습니다.");
        return targetRun+"회차 / "+report.storyPath+" / "+(useEdit?"Edit ":"AI ")+stage;
    }
}
