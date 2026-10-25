using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Isolated evidence only. Never reads/writes player saves or promotes generated content.
public static class PromptCompositionCheck
{
    const string PathId="Initial/Initial_01/Encounters/guide";
    public static Task<object> Baseline() => Capture("before");
    public static Task<object> After() => Capture("after");
    public static Task<object> Final() => Capture("final");
    public static Task<object> Verified() => Capture("verified");
    public static async Task<object> StrictRuntime()
    {
        ChapterContentCatalog.Reset();var runtime=LocalStoryPrompt.Policy["runtime"];bool prior=(bool)runtime["allowFailedOutput"];
        runtime["allowFailedOutput"]=false;
        try{return await StrictRuntimeEnabled();}
        finally{runtime["allowFailedOutput"]=prior;}
    }
    private static async Task<object> StrictRuntimeEnabled()
    {
        Check(!Application.isPlaying,"Stop gameplay first.");
        var storage=(SaveIOService)Activator.CreateInstance(typeof(SaveIOService),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,
            null,new object[]{System.IO.Path.GetFullPath("tmp/PromptComposition/Strict/"+Guid.NewGuid().ToString("N"))},null);
        var source=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(PathId));
        var failed=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(source));
        failed.MainStory[8].options[0]=failed.MainStory[8].choiceActions[0].choiceId;
        Check(storage.SaveGeneratedContentForReview(2,"Encounters",PathId+"/Story",failed,"isolated ID failure"),"could not preserve diagnostic evidence");
        Check(!storage.TryLoadGeneratedContent(2,"Encounters",PathId+"/Story",out EncounterCardRoot _),"failed evidence became normal runtime content");
        var request=new LocalStoryRevisionRequest {sourceRun=1,targetRun=2,chapterId="Initial",npcContexts=new[]{new LocalStoryNpcContext{characterId="Guide",name="로키",model="Gemma",autoGenerate=true}}};
        var result=await LocalStoryRuntimePipeline.ProduceDialogue(request,request.npcContexts[0],PathId,storage,(report,ct)=>{
            report.rawOutput=new JObject{["cards"]=new JArray(failed.MainStory.Select((c,i)=>new JObject{["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}))}.ToString();
            LocalStoryRewrite.EvaluateDraft(report,report.original,report.interactionMemory);return Task.CompletedTask;
        },CancellationToken.None,()=>true);
        Check(result.status=="Failed"&&File.Exists(result.reportPath),"failed generation was applied or evidence lost");
        Check(storage.ApplySavedFailedOutputs()==0,"normal policy automatically promoted failed output");
        return new {passed=true,checks=5,realSavesUntouched=true,diagnosticReport=result.reportPath};
    }
    static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    static JObject Request(string messages)=>JObject.Parse(((string)JArray.Parse(messages)[1]["content"]).Split('\n')[1]);
    public static object Regression()
    {
        Check(!Application.isPlaying,"Stop gameplay first.");ChapterContentCatalog.Reset();int checks=0;
        void Assert(bool value,string message){Check(value,message);checks++;}
        var original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(PathId));
        string Make(int start,int count,LocalStoryRevisionRequest memory=null)=>LocalStoryPrompt.Messages(PathId,original,start,count,new List<EncounterCard>(),memory);
        string normal=Make(0,3),choice=Make(8,1),garbled=Make(4,1);
        string[] Modules(string input)=>LocalStoryPromptModules.Sources(input).Select(System.IO.Path.GetFileName).ToArray();
        Assert(!Modules(normal).Contains("Garbled")&&!Modules(normal).Contains("Choice")&&!Modules(normal).Contains("Correction"),"irrelevant modules in ordinary dialogue");
        Assert(Modules(choice).Contains("Choice")&&Modules(garbled).Contains("Garbled"),"conditional module missing");
        Assert(!Modules(Make(0,3,new LocalStoryRevisionRequest {memoryMode="Dream"})).Contains("MemoryNone"),"memory rules mixed");
        Assert(Modules(Make(0,3,new LocalStoryRevisionRequest {memoryMode="Dream"})).Contains("MemoryDream"),"dream module missing");
        foreach(string messages in new[]{normal,choice,garbled})
        {
            var request=Request(messages);
            Assert(request["examples"] is JArray examples && examples.Count<=2 && examples.All(e=>e["input"] is JObject && e["output"]?["cards"] is JArray),"conditional examples lost input/output");
            Assert(request["nextEvent"]==null,"future event leaked");
            Assert(!messages.Contains("choiceId")&&!messages.Contains("loki_question_")&&!messages.Contains("Positive"),"execution identifier/category leaked");
        }
        var options=Request(choice)["cards"][0]["options"];
        Assert(options.Count()==4&&options.All(o=>(string)o["speaker"]=="여행자"&&(string)o["listener"]=="로키"),"option perspectives incorrect");
        var state=Request(Make(5,1))["cards"][0];
        Assert((string)state["actor"]=="로키"&&(string)state["target"]=="여행자"&&state["stateAfter"]!=null,"event participants/state missing from Explanation");
        string correction=LocalStoryPrompt.Messages(PathId,original,8,1,new List<EncounterCard>(),null,"{}","invalid");
        Assert(Modules(correction).Contains("Correction"),"repair rule missing");
        Assert(LocalStoryPrompt.Policy["runtime"]["allowFailedOutput"]?.Type==JTokenType.Boolean,"missing configurable failed playback policy");
        var sampleCopy=LocalStorySections.Slice(original,8,1);sampleCopy.MainStory[0].options[0]=(string)Request(choice)["examples"][0]["output"]["cards"][0]["options"][0];
        Assert(!LocalStoryPromptModules.ValidateExampleCopies(LocalStorySections.Slice(original,8,1),sampleCopy,choice,out _),"unrelated example copied into player option");
        var module=LocalStoryPromptModules.Read("LocalStory/PromptModules/Common");string hash=LocalStoryPrompt.Hash;
        var rules=(JArray)module["rules"];rules.Add("MODULE_FILE_SENTINEL");
        try {Assert(Make(0,3).Contains("MODULE_FILE_SENTINEL"),"rules hardcoded instead of composed");Assert(LocalStoryPrompt.Hash!=hash,"module edit did not invalidate generation");}
        finally {rules.RemoveAt(rules.Count-1);}
        var other=LocalStorySections.Slice(original,8,1);other.MainStory[0].options[0]=original.MainStory[0].eventIdStable;
        Assert(!LocalStoryRewrite.ValidateCardWording(original.MainStory[8],other.MainStory[0],out _),"another card's identifier bypassed guard");
        string folder=System.IO.Path.GetFullPath("tmp/PromptComposition");Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder,"composed-examples.json"),new JObject{["normal"]=JArray.Parse(normal),["choice"]=JArray.Parse(choice),["garbled"]=JArray.Parse(garbled),["correction"]=JArray.Parse(correction)}.ToString(),new UTF8Encoding(false));
        return new {passed=true,checks,normalModules=Modules(normal),choiceModules=Modules(choice),realSavesUntouched=true};
    }
    static async Task<object> Capture(string label)
    {
        if(Application.isPlaying || LocalStoryRuntimeService.IsBusy) throw new Exception("Stop gameplay/generation first.");
        ChapterContentCatalog.Reset();
        LocalStoryLifetime.CancelAndWait();
        var report=new LocalStoryTestReport {storyPath=PathId,model=LocalStoryTestModels.Name(LocalStoryTestModel.Gemma),
            original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(PathId)),seed=12345,allowFailedOutput=true};
        string folder=System.IO.Path.GetFullPath("tmp/PromptComposition");Directory.CreateDirectory(folder);
        string file=System.IO.Path.Combine(folder,label+".json");
        using var stop=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var watch=System.Diagnostics.Stopwatch.StartNew();
        try {await LocalStoryRuntimePipeline.Infer(report,stop.Token);}
        catch(Exception e) {report.runtimeError=e.ToString();}
        finally {
            report.seconds=watch.Elapsed.TotalSeconds;
            File.WriteAllText(file,JsonUtility.ToJson(report,true),new UTF8Encoding(false));
            LocalStoryLifetime.CancelAndWait();
        }
        return new {file,report.seconds,report.valid,report.correctionValid,report.sectionsGenerated,report.sectionsCorrected,
            report.backend,report.runtimeError,realSavesUntouched=true};
    }
}
