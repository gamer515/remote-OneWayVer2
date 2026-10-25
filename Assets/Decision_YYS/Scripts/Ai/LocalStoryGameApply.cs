using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Failed-output diagnostic playback and explicit test-result promotion, with canonical game bindings.</summary>
public static class LocalStoryGameApply
{
    public static EncounterCardRoot BuildFailedPreview(LocalStoryTestReport report,out string reason)
    {
        if(report.reviewRequired && report.reviewGeneratedCards==0)
            throw new FormatException("모든 구간이 표시 불가 출력이라 원문을 유지합니다.");
        var source=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(report.storyPath));
        if(!LocalStoryRewrite.SameBindings(source,report.original))throw new FormatException("실패 결과와 현재 원문의 게임 연결이 다릅니다.");
        foreach(string raw in new[]{report.reviewRawOutput,report.correctionRawOutput,report.rawOutput}
            .Where(s=>!string.IsNullOrWhiteSpace(s)).OrderByDescending(s=>LocalStoryTestArchive.RawCards(s)?.Count??0))
        {
            if(LocalStoryRewrite.TryBuildForReview(raw,source,out var built,out string partial,true))
            {
                report.reviewGeneratedCards=LocalStoryTestArchive.RawCards(raw)?.Count??0;
                reason=string.Join(" / ",new[]{report.reviewError??report.runtimeError??report.correctionError??report.error??"생성 검사 실패",partial}.Where(s=>!string.IsNullOrWhiteSpace(s)));
                return built;
            }
            if(TryIncompletePreview(raw,source,out built,out int count))
            {
                report.reviewGeneratedCards=count;
                reason=(report.runtimeError??report.correctionError??report.error??"생성 중단")+" / "+count+"/"+source.MainStory.Count+"카드 생성 문구 확인, 나머지는 원문 유지";
                return built;
            }
        }
        throw new FormatException("실패 원출력이 JSON 카드/선택지 구조에 맞지 않아 표시할 수 없습니다.");
    }
    private static bool TryIncompletePreview(string raw,EncounterCardRoot source,out EncounterCardRoot built,out int count)
    {
        built=null;count=0;JArray fragments;
        try{fragments=JObject.Parse(raw)["incompleteSections"] as JArray;}
        catch(Newtonsoft.Json.JsonException){return false;}
        if(fragments==null)return false;
        var candidate=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(source));var used=new HashSet<int>();
        foreach(var fragment in fragments)
        {
            if(fragment["start"]?.Type!=JTokenType.Integer || fragment["count"]?.Type!=JTokenType.Integer)continue;
            int start=(int)fragment["start"],length=(int)fragment["count"];
            if(start<0 || length<1 || start>source.MainStory.Count-length)continue;
            string text=(string)fragment["rawOutput"];int provided=LocalStoryTestArchive.RawCards(text)?.Count??0;
            if(provided==0 || Enumerable.Range(start,provided).Any(used.Contains) ||
                !LocalStoryRewrite.TryBuildForReview(text,LocalStorySections.Slice(source,start,length),out var part,out _,true))continue;
            for(int i=0;i<provided;i++)
            {
                candidate.MainStory[start+i].text=part.MainStory[i].text;
                candidate.MainStory[start+i].options=part.MainStory[i].options;used.Add(start+i);
            }
        }
        count=used.Count;if(count==0)return false;built=candidate;return true;
    }
    public static int NextRun(LocalStoryTestReport report)
    {
        string profile=Path.Combine(Application.persistentDataPath,"Saves","Profile.json");
        int active=File.Exists(profile)?JsonUtility.FromJson<ProfileData>(File.ReadAllText(profile)).currentRun:1;
        return Math.Max(Math.Max(2,active+1),report?.targetRun??2);
    }
    public static EncounterCardRoot Build(LocalStoryTestReport report,string stage,bool useEdit,string editPath=null)=>Build(report,stage,useEdit,out _,editPath);
    public static EncounterCardRoot Build(LocalStoryTestReport report,string stage,bool useEdit,out string reviewError,string editPath=null)
    {
        reviewError=null;
        if(report==null || (stage!="draft" && stage!="correction")) throw new ArgumentException("결과와 초안/교정 단계를 선택하세요.");
        string source=NarrativeInteractions.StorySource(report.storyPath);
        if(source==null) throw new InvalidOperationException("현재 원문을 찾을 수 없습니다.");
        var original=JsonUtility.FromJson<EncounterCardRoot>(source);
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
            var lines=LocalStoryTestArchive.ReadEditData(editPath,JObject.Parse(JsonUtility.ToJson(report)))[stage] as JArray;
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
                var person=ChapterContentCatalog.Asset("Story_Json_Data/Characters/"+id);
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
        {
            reviewError=error;
            if(!LocalStoryRewrite.TryBuildForReview(patch,original,out built,out _,true))throw new FormatException("게임 적용 구조 검사 실패: "+error);
        }
        if(!useEdit && stage=="correction" && report.reviewRequired)reviewError=report.reviewError??reviewError??"검사 실패 문구 포함";
        return built;
    }
    public static string Save(LocalStoryTestReport report,string stage,bool useEdit,int targetRun,
        SaveIOService storage,string editPath=null)
    {
        if(targetRun<2 || storage==null) throw new ArgumentException("적용 회차는 2 이상이어야 합니다.");
        var built=Build(report,stage,useEdit,out string reviewError,editPath);
        bool saved=reviewError==null?storage.SaveGeneratedContentForTest(targetRun,"Encounters",report.storyPath+"/Story",built):
            storage.SaveGeneratedContentForReview(targetRun,"Encounters",report.storyPath+"/Story",built,reviewError,true);
        if(!saved)
            throw new IOException("게임 확인용 저장에 실패했습니다.");
        return targetRun+"회차 / "+report.storyPath+" / "+(useEdit?"Edit ":"AI ")+stage+(reviewError==null?"":" / 검사 실패본 반영: "+reviewError);
    }
}
