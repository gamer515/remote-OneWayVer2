using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class StoryParagraphTimingCheck
{
    const string Guide="Initial/Initial_01/Encounters/guide";
    static LocalStoryTestReport Report()=>new LocalStoryTestReport {storyPath=Guide,model=LocalStoryTestModels.Name(LocalStoryTestModel.Gemma),
        original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(Guide)),seed=12345,sourceRun=1,targetRun=2,
        testScenario="Paragraph frontend verification / isolated",createdUtc=DateTime.UtcNow.ToString("o")};
    static string Patch(EncounterCardRoot source)=>new JObject {["cards"]=new JArray(source.MainStory.Select((c,i)=>new JObject {
        ["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}))}.ToString(Newtonsoft.Json.Formatting.None);
    public static async Task<object> Regression()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        int checks=0;void Check(bool yes,string message){if(!yes)throw new Exception(message);checks++;}
        ChapterContentCatalog.Reset();var report=Report();var calls=new List<(int start,int count,bool repair)>();int progress=0;
        report.sectionProgress=(s,c,i,total,phase)=>{Check(total==6,"unexpected section total");progress++;};
        await LocalStoryTestPanel.GenerateSections(report,CancellationToken.None,(input,repair,token,stream)=>
        {
            calls.Add((input.start,input.count,repair));
            var source=LocalStorySections.Slice(report.original,input.start,input.count);foreach(var c in source.MainStory)c.text+=" …";
            return Task.FromResult(new LocalStoryModelOutput {json=Patch(source),backend="IsolatedFake",sessionReused=calls.Count>1});
        });
        Check(report.valid&&!report.correctionAttempted,"test button still runs unconditional correction");
        Check(calls.Count==6&&calls.Select(c=>c.count).SequenceEqual(new[]{3,1,1,1,2,1}),"test button bypasses authored paragraphs");
        Check(calls.Sum(c=>c.count)==9&&report.original.MainStory.Sum(c=>c.options?.Length??0)==8,"missing dialogue or choices");
        Check(progress==6&&report.sectionsGenerated==6,"progress does not match actual requests");
        Check(report.sectionInputs.All(i=>i.maxTokens<1800)&&report.generationGrammar==report.sectionInputs.Last().grammar,"diagnostics describe unused full-scene request");
        var gameplay=Report();await LocalStorySections.Generate(gameplay,CancellationToken.None,(input,repair,token,stream)=>
        {
            var source=LocalStorySections.Slice(gameplay.original,input.start,input.count);foreach(var c in source.MainStory)c.text+=" …";
            return Task.FromResult(new LocalStoryModelOutput {json=Patch(source),backend="IsolatedFake"});
        });
        Check(report.sectionInputs.Select(i=>i.prompt).SequenceEqual(gameplay.sectionInputs.Select(i=>i.prompt)),"test/game prompts differ");
        var fixedReport=Report();int repairs=0;
        await LocalStoryTestPanel.GenerateSections(fixedReport,CancellationToken.None,(input,repair,token,stream)=>
        {
            if(repair)repairs++;
            var source=LocalStorySections.Slice(fixedReport.original,input.start,input.count);
            if(input.start==0&&!repair)source.MainStory[0].text="Guide → Player; 잘못된 접두사";
            return Task.FromResult(new LocalStoryModelOutput {json=Patch(source),backend="IsolatedFake"});
        });
        Check(fixedReport.correctionValid&&repairs==1&&fixedReport.sectionsGenerated==6,"repair regenerated successful sections");
        string root=Path.GetFullPath("tmp/ParagraphFrontendCheck/"+Guid.NewGuid().ToString("N"));
        string file=LocalStoryTestArchive.Save(root,JObject.Parse(JsonUtility.ToJson(report)));var saved=LocalStoryTestArchive.ReadReport(file);
        Check(saved["diagnostics"]["sectionTimings"].Count()==6&&saved["diagnostics"]["sectionTimings"].Sum(s=>(int)s["cards"])==9,"compact timing record missing");
        Check(saved["sectionInputs"]==null&&saved["sectionOutputs"]==null&&!Directory.GetFiles(root,"Evidence.zip",SearchOption.AllDirectories).Any(),"full per-section evidence returned");
        Check(LocalStoryTestPanel.DisplayOutput(LocalStoryTestArchive.Load(file)).Contains("단락 4개 / 작성 요청 6회"),"saved UI loses paragraph/request counts");
        var leaked=LocalStorySections.Slice(report.original,8,1);leaked.MainStory[0].options[0]=leaked.MainStory[0].choiceActions[0].choiceId;
        Check(!LocalStoryRewrite.TryBuild(Patch(leaked),LocalStorySections.Slice(report.original,8,1),null,out _,out var leakError)&&leakError.Contains("내부 이벤트/선택 ID"),"choice ID accepted as dialogue");
        Check(!LocalStoryRewrite.ValidateCardWording(report.original.MainStory[8],leaked.MainStory[0],out _),"runtime accepts leaked ID");
        leaked.MainStory[0].choiceActions[0].choiceId="merchant_accept_trade";leaked.MainStory[0].options[0]="merchant_accept_trade";
        var merchantSource=LocalStorySections.Slice(report.original,8,1);merchantSource.MainStory[0].choiceActions[0].choiceId="merchant_accept_trade";
        Check(!LocalStoryRewrite.ValidateCardWording(merchantSource.MainStory[0],leaked.MainStory[0],out _),"identifier guard depends on Loki IDs");
        string locked=Path.Combine(root,"BrieflyLocked.json");File.WriteAllText(locked,"{\"value\":1}");
        var handle=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.Read);
        var release=Task.Run(()=>{Thread.Sleep(70);handle.Dispose();});
        try{typeof(LocalStoryTestArchive).GetMethod("Write",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static,null,new[]{typeof(string),typeof(string)},null)
            .Invoke(null,new object[]{locked,"{\"value\":2}"});}finally{await release;}
        Check((int)JObject.Parse(File.ReadAllText(locked))["value"]==2,"brief Windows lock lost atomic update");
        return new {passed=true,checks,requests=calls.Count,paragraphs=4,allCards=9,options=8,wholeSceneCorrectionRemoved=true};
    }
    static Dictionary<string,string> Fingerprints()
    {
        string root=Path.Combine(Application.persistentDataPath,"Saves");
        return Directory.Exists(root)?Directory.GetFiles(root,"*.json",SearchOption.AllDirectories).ToDictionary(f=>f,f=>LocalStoryModelHash.FileHash(f,CancellationToken.None)):new Dictionary<string,string>();
    }
    public static async Task<object> NativeBenchmark()
    {
        if(Application.isPlaying||LocalStoryRuntimeService.IsBusy)throw new InvalidOperationException("Play와 자동 생성을 중지한 상태에서 실행하세요.");
        LocalStoryLifetime.CancelAndWait();ChapterContentCatalog.Reset();var fingerprints=Fingerprints();
        string root=Path.GetFullPath("tmp/ParagraphTiming");Directory.CreateDirectory(root);var cases=new JArray();
        using var budget=new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try
        {
            foreach(string name in new[]{"cold","resident"})
            {
                var report=Report();var watch=System.Diagnostics.Stopwatch.StartNew();bool residentBefore=LocalStoryInference.HasResidentModel;
                report.sectionProgress=(start,count,index,total,phase)=>File.WriteAllText(Path.Combine(root,"Progress.json"),new JObject {
                    ["case"]=name,["request"]=index,["total"]=total,["cards"]=(start+1)+"~"+(start+count),["phase"]=phase,["seconds"]=watch.Elapsed.TotalSeconds }.ToString());
                try{await LocalStoryTestPanel.GenerateSections(report,budget.Token);}catch(Exception e){report.runtimeError=e.Message;}
                report.seconds=watch.Elapsed.TotalSeconds;
                string file=LocalStoryTestArchive.Save(Path.Combine(root,"LocalStoryTests"),JObject.Parse(JsonUtility.ToJson(report)));
                var item=new JObject {["case"]=name,["residentBefore"]=residentBefore,["seconds"]=report.seconds,
                    ["valid"]=report.correctionAttempted?report.correctionValid:report.valid,["reviewRequired"]=report.reviewRequired,["reviewGeneratedCards"]=report.reviewGeneratedCards,
                    ["draftCalls"]=report.sectionsGenerated,["repairCalls"]=report.sectionsCorrected,
                    ["verificationSeconds"]=report.draftVerificationSeconds+report.correctionVerificationSeconds,
                    ["modelLoadSeconds"]=report.draftModelLoadSeconds+report.correctionModelLoadSeconds,
                    ["generationSeconds"]=report.draftGenerationSeconds+report.correctionGenerationSeconds,
                    ["cardsTargeted"]=report.sectionInputs?.Sum(i=>i.count),["error"]=report.runtimeError??report.correctionError??report.error,
                    ["reportPath"]=file,["timings"]=LocalStoryTestArchive.ReadReport(file)["diagnostics"]?["sectionTimings"]?.DeepClone(),
                    ["output"]=report.correctionAttempted?report.correctionRawOutput:report.rawOutput};
                cases.Add(item);File.WriteAllText(Path.Combine(root,"Result.json"),cases.ToString());
            }
        }
        finally{LocalStoryLifetime.CancelAndWait();}
        var after=Fingerprints();if(fingerprints.Any(p=>!after.TryGetValue(p.Key,out var hash)||hash!=p.Value)||after.Count!=fingerprints.Count)throw new IOException("실제 저장 파일이 변경되었습니다.");
        return new {cases,file=Path.Combine(root,"Result.json"),actualSaveFilesUnchanged=fingerprints.Count};
    }
}
