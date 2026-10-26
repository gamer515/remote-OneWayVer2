using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class StoryPromptModuleCheck
{
    const string Guide="Initial/Initial_01/Encounters/guide",Secret="prototype:loki_mine_mark_secret";
    static int checks;
    static void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
    static SaveIOService Store(string root)=>(SaveIOService)Activator.CreateInstance(typeof(SaveIOService),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{root},null);
    static JObject Document(string key)=>(JObject)typeof(ChapterContentCatalog).GetMethod("Document",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{key});
    static EncounterCardRoot Source(string path)=>JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
    static JObject Stable(string messages)=>JObject.Parse(((string)JArray.Parse(messages)[1]["content"]).Split('\n')[0]);
    static JObject Variable(string messages)=>JObject.Parse(((string)JArray.Parse(messages)[1]["content"]).Split('\n')[1]);
    public static object Regression()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        checks=0;ChapterContentCatalog.Reset();
        var infos=ChapterContentCatalog.Stories.ToArray();
        foreach(var info in infos)
        {
            string path=(string)info["path"];var source=Source(path);var plan=LocalStoryPrompt.Plan(path,source);
            Check(plan.SelectMany(p=>Enumerable.Range(p.start,p.count)).SequenceEqual(Enumerable.Range(0,source.MainStory.Count)),"missing or repeated card: "+path);
            foreach(var range in plan)
            {
                var input=LocalStoryRewrite.SectionMessages(path,source,range.start,range.count,new List<EncounterCard>(),null);
                Check(((JArray)Variable(input)["cards"]).Count==range.count,"wrong request cards: "+path);
                Check(!input.Contains("requiresUnlock_ALL")&&!input.Contains("requiresItems_ALL"),"raw hidden definitions leaked: "+path);
            }
        }
        var original=Source(Guide);var sections=LocalStoryPrompt.Plan(Guide,original);
        Check(sections.Select(r=>r.count).SequenceEqual(new[]{3,1,1,1,2,1}),"Guide semantic/state boundaries wrong");
        Check(sections.Sum(r=>r.count)==9,"Guide lost cards");
        var first=LocalStoryRewrite.SectionMessages(Guide,original,0,3,new List<EncounterCard>(),null);
        var next=LocalStoryRewrite.SectionMessages(Guide,original,3,1,original.MainStory.Take(3).ToList(),null);
        Check(JToken.DeepEquals(Stable(first),Stable(next)),"stable prefix changed between sections");
        Check(!LocalStoryPrompt.CommonRules.Contains("로키")&&!LocalStoryPrompt.CommonRules.Contains("광산"),"NPC/scene content hardcoded in common rules");
        Check((string)Variable(next)["cards"][0]["purpose"]==(string)ChapterContentCatalog.Explanation(Guide)["coreMeaning"]["-1725547473"]["purpose"],"purpose not read from Explanation");
        Check(((JArray)Variable(next)["previousDialogue"]).Count==2,"previous dialogue unbounded");
        var choice=Variable(LocalStoryRewrite.SectionMessages(Guide,original,8,1,original.MainStory.Take(8).ToList(),null));
        Check(((JArray)choice["cards"][0]["options"]).Count==4,"choice set split");
        Check(choice["cards"][0]["options"].Select(o=>(int)o["index"]).SequenceEqual(Enumerable.Range(0,4)) &&
            choice["cards"][0]["options"].All(o=>o["choiceId"]==null && !string.IsNullOrWhiteSpace((string)o["intent"])),"choice meaning/position mapping changed");
        string root=Path.GetFullPath("tmp/StoryPromptModules/"+Guid.NewGuid().ToString("N"));var store=Store(root);
        var locked=LocalStoryNotebook.Contexts(store,Array.Empty<string>());
        string book=LocalStoryNotebook.PathFor(store);
        Check(LocalStoryNotebook.ReadUnlockStates(book)[Secret]==0,"unearned bit is not zero");
        var earned=LocalStoryNotebook.Contexts(store,new[]{Secret});
        Check(LocalStoryNotebook.ReadUnlockStates(book)[Secret]==1,"earned bit not persisted as one");
        var memory=new LocalStoryRevisionRequest {chapterId="Initial",memoryMode="Dream",characterId="Guide",earnedUnlocks=new[]{Secret},npcContexts=earned};
        var known=LocalStoryRewrite.SectionMessages(Guide,original,8,1,new List<EncounterCard>(),memory);
        Check(Stable(known)["characters"][0]["unlockedFacts"].Values<string>().Any(s=>s.Contains("오래된 길")),"unlocked fact absent");
        memory.npcContexts=locked;
        Check(!Stable(LocalStoryRewrite.SectionMessages(Guide,original,8,1,new List<EncounterCard>(),memory))["characters"][0]["unlockedFacts"].Any(),"zero Excel snapshot disclosed secret");
        memory.npcContexts=earned;memory.ownedItemIds=new[]{"prototype_loki_mark"};
        Check(Stable(LocalStoryRewrite.SectionMessages(Guide,original,8,1,new List<EncounterCard>(),memory))["characters"][0]["unlockedFacts"].Count()==2,"item-gated fact not included");
        memory.ownedItemIds=Array.Empty<string>();
        Check(Stable(LocalStoryRewrite.SectionMessages(Guide,original,8,1,new List<EncounterCard>(),memory))["characters"][0]["unlockedFacts"].Count()==1,"returned item retained ownership fact");
        var e=ChapterContentCatalog.Explanation(Guide);string before=LocalStorySections.GenerationPolicyHash(Guide);
        e["paragraphs"][0]["limits"]=new JObject {["maxCards"]=1};NarrativeInteractions.ResetCache();
        Check(LocalStoryPrompt.Plan(Guide,original).Count==8,"paragraph amount ignored");
        Check(before!=LocalStorySections.GenerationPolicyHash(Guide),"edited Explanation did not invalidate generation policy");
        ((JObject)e["paragraphs"][0]).Remove("limits");NarrativeInteractions.ResetCache();
        ((JArray)e["paragraphs"][0]["eventIds"]).RemoveAt(0);NarrativeInteractions.ResetCache();
        bool rejected=false;try{LocalStoryPrompt.Plan(Guide,original);}catch(InvalidDataException){rejected=true;}
        Check(rejected,"incomplete authored paragraph accepted");ChapterContentCatalog.Reset();

        // Temporary new chapter/NPC/story/unlock exist only in the resource cache and isolated Records.
        try
        {
            var documents=(Dictionary<string,JObject>)typeof(ChapterContentCatalog).GetField("documents",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            var catalog=Document("Catalog");const string chapter="ModuleChapter",path="ModuleChapter/ModuleChapter_01/Encounters/probe";
            ((JArray)catalog["chapters"]).Add(new JObject {["id"]=chapter,["folder"]="Chapter_99",["episodes"]=new JArray("ModuleChapter_01")});
            documents["Chapter_99/Characters"]=new JObject {["characters"]=new JArray(new JObject {["characterId"]="ModuleProbe",["name"]="임시 검증 인물",["role"]="검증 역할",["personality"]="MODULE_BASE",["autoGenerate"]=true,
                ["hidden"]=new JObject {["secret"]=new JObject {["requiresUnlock_ALL"]=new JArray("module:secret"),["text"]="MODULE_SECRET"}}})};
            documents["Chapter_99/Terrain"]=new JObject {["interactions"]=new JArray()};
            documents["Chapter_99/Story"]=new JObject {["probe"]=new JObject {["MainStory"]=new JArray(new JObject {["ID"]=989001,["eventIDstable"]="module_probe",["type"]="Next",["speakID"]="ModuleProbe",["Text"]="잠시 이야기하지."})}};
            documents["Chapter_99/Explanation"]=new JObject {["probe"]=new JObject {["coreMeaning"]=new JObject(),["paragraphs"]=new JArray(new JObject {["id"]="probe",["eventIds"]=new JArray("module_probe"),["purpose"]="MODULE_PURPOSE",["currentState"]=new JArray("MODULE_STATE")})}};
            ((JArray)catalog["stories"]).Add(new JObject {["path"]=path,["chapterId"]=chapter,["chapterFolder"]="Chapter_99",["episodeId"]="ModuleChapter_01",["characterId"]="ModuleProbe",["placeId"]="probe",["key"]="probe",["storyFile"]="Chapter_99/Story.json",["explanationFile"]="Chapter_99/Explanation.json"});
            NarrativeInteractions.ResetCache();
            var contexts=LocalStoryNotebook.Contexts(store,new[]{"module:secret"});
            Check(contexts.Single(c=>c.characterId=="ModuleProbe").autoGenerate,"new NPC depends on Guide hardcoding");
            Check(LocalStoryNotebook.ReadUnlockStates(LocalStoryNotebook.ChapterPath(store,chapter))["module:secret"]==1,"new chapter/unlock needs special code/template");
            var request=new LocalStoryRevisionRequest {chapterId=chapter,earnedUnlocks=new[]{"module:secret"},npcContexts=contexts};
            string input=LocalStoryRewrite.SectionMessages(path,Source(path),0,1,new List<EncounterCard>(),request);
            Check(input.Contains("MODULE_BASE")&&input.Contains("MODULE_PURPOSE")&&input.Contains("MODULE_STATE")&&input.Contains("MODULE_SECRET"),"new data did not enter prompt");
            Check(!input.Contains("로키")&&!input.Contains("광산"),"another scene's data leaked into new chapter");
            Check(LocalStoryPrompt.Plan(path,Source(path)).Single().count==1,"new chapter plan failed");
        }
        finally{ChapterContentCatalog.Reset();LocalStoryNotebook.Refresh(store);string temporary=Path.Combine(root,"Records","Chapter_99.xlsx");if(File.Exists(temporary))File.Delete(temporary);}
        Check(ChapterContentCatalog.Character("ModuleProbe")==null,"temporary source remains");
        return new {passed=true,checks,stories=infos.Length,guideRequests=sections.Count,root,newChapterWithoutCode=true,temporaryContentRemoved=true};
    }
    public static object ExportReview()
    {
        ChapterContentCatalog.Reset();var source=Source(Guide);var plan=LocalStoryPrompt.Plan(Guide,source);var previous=new List<EncounterCard>();
        var b=new StringBuilder("# 현재 LLM 프롬프트 전체\n\n현재 코드가 조립한 입력이다. 이 문서 작성 중 모델 추론은 실행하지 않았다.\n앞선 대사는 생성 결과 대신 원문을 넣은 검토 예시다.\n이 문서는 검토용이며 런타임은 Resources의 JSON을 읽는다.\n\n");
        b.Append("공통 규칙: Assets/Decision_YYS/Resources/LocalStory/PromptPolicy.json\n\n장면·단락·목적·상태: Assets/Decision_YYS/Resources/Chapters/Chapter_00/Chapter_00_01/Guide/Explanation.json\n\n인물·해금 설정: Assets/Decision_YYS/Resources/Chapters/Chapter_00/Characters.json\n\n");
        foreach(var range in plan)
        {
            string messages=LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,LocalStoryRewrite.SectionMessages(Guide,source,range.start,range.count,previous,null));
            b.Append("참조 모듈: "+string.Join(", ",LocalStoryPromptModules.Sources(messages))+"\n\n");
            b.Append("## 카드 "+(range.start+1)+"~"+(range.start+range.count)+" / "+LocalStoryPrompt.Paragraph(Guide,source.MainStory[range.start],range.start).id+"\n\n");
            string content=(string)JArray.Parse(messages)[0]["content"];
            string readable=string.Join("\n",content.Split('\n').Select(line=>line.StartsWith("{")?JObject.Parse(line).ToString():line));
            b.Append("### 최종 messages JSON 전체\n```json\n"+JArray.Parse(messages).ToString()+"\n```\n\n### content 전체 — JSON 공백만 펼친 보기\n```text\n"+readable+"\n```\n\n");
            b.Append("### 별도 생성 설정\n```json\n"+LocalStorySampling.Comparison().WithTokenLimit(LocalStoryPrompt.MaxTokens(Guide,source,range.start,range.count)).Parameters(12345+range.start)+"\n```\n\n");
            b.Append("### 별도 출력 문법\n```text\n"+LocalStoryRewrite.Grammar(LocalStorySections.Slice(source,range.start,range.count),true)+"\n```\n\n");
            previous.AddRange(source.MainStory.Skip(range.start).Take(range.count));
        }
        string file=Path.GetFullPath("Docs/CurrentLLMPrompt.md");File.WriteAllText(file,b.ToString());return new {file,requests=plan.Count,inferenceExecuted=false};
    }
}
