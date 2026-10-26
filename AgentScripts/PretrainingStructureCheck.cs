using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Returns evidence to the caller only. No reports or player save writes.
public static class PretrainingStructureCheck
{
    public static async Task<object> PlaybackWithoutReports()
    {
        if(Application.isPlaying || LocalStoryRuntimeService.IsBusy)throw new Exception("Stop gameplay first.");
        ChapterContentCatalog.Reset();var policy=LocalStoryPrompt.Policy;var prior=(JObject)policy.DeepClone();
        string root=Path.GetFullPath("tmp/PlaybackWithoutReports/"+Guid.NewGuid().ToString("N"));int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        object old=null;bool swapped=false;
        var field=typeof(SaveIOService).GetFields(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Single(f=>f.FieldType==typeof(SaveIOService));
        try
        {
            policy["runtime"]["saveDiagnosticReports"]=false;policy["runtime"]["allowFailedOutput"]=true;policy["runtime"]["maxCorrectionsPerSection"]=0;
            var scenario=JObject.Parse(File.ReadAllText("Tests/LocalStoryBenchmark/CompositionCases.json"))["cases"][0];string path=(string)scenario["storyPath"];
            var source=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
            var store=(SaveIOService)Activator.CreateInstance(typeof(SaveIOService),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{root},null);
            store.SaveProfile(new ProfileData{currentRun=1});store.SaveRunCheckpoint(1,"Progress",new GameProgress());
            LocalStoryDialogueQueue.QueuePaths(store,1,source.MainStory[0].speakerId,new[]{path},"isolated");
            var pending=LocalStoryDialogueQueue.Next(store);int repairs=0;
            await LocalStoryDialogueQueue.Process(store,pending,(r,ct)=>LocalStorySections.Generate(r,ct,(input,repair,token,stream)=>{
                if(repair)repairs++;
                var slice=LocalStorySections.Slice(r.original,input.start,input.count);
                var cards=new JArray(slice.MainStory.Select((c,i)=>new JObject{["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}));
                var choice=cards.FirstOrDefault(c=>c["options"].Any());
                if(choice!=null)choice["options"][0]=slice.MainStory[(int)choice["index"]].choiceActions[0].choiceId;
                return Task.FromResult(new LocalStoryModelOutput{json=new JObject{["cards"]=cards}.ToString(),backend="isolated"});
            }),CancellationToken.None);
            var job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(job.status=="ReviewRequired","failed output was not applied");Check(repairs==0,"repair limit ignored");
            Check(string.IsNullOrEmpty(job.reportPath),"unexpected report path: "+job.reportPath);
            Check(!Directory.Exists(Path.Combine(root,"LocalStoryTests")),"diagnostic directory recreated");
            Check((job.sectionCheckpoint?.inputs?.Count??0)==0 && (job.sectionCheckpoint?.outputs?.Count??0)==0,"finished job retained duplicate prompts/output");
            Check(store.NarrativeRequiresReview(2,path,out _),"review flag lost");
            old=field.GetValue(null);field.SetValue(null,store);swapped=true;
            Check(new EncounterContentRepository(2).TryLoadCards(path,out var replay,out var error),error);
            int index=source.MainStory.FindIndex(c=>(c.options?.Length??0)>0);
            Check(replay.MainStory[index].options[0]==source.MainStory[index].choiceActions[0].choiceId,"failed wording replaced on next run");
            Check(new EncounterContentRepository(1).TryLoadCards(path,out var current,out error),error);
            Check(LocalStoryRewrite.SameBindings(JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(current)),JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(replay))),"game bindings changed");
            Check(current.MainStory[index].options[0]==source.MainStory[index].options[0],"current run changed");
            LocalStoryDialogueQueue.QueuePaths(store,1,source.MainStory[0].speakerId,new[]{path},"repeat");
            Check(LocalStoryDialogueQueue.Load(store,1).jobs.Count==1&&LocalStoryDialogueQueue.Next(store)==null,"completed request requeued");
            return new{passed=true,checks,actualSaveWrites=false,diagnosticFilesWritten=0};
        }
        finally
        {
            if(swapped)field.SetValue(null,old);
            policy.RemoveAll();foreach(var item in prior.Properties())policy.Add(item.Name,item.Value.DeepClone());
            string allowed=Path.GetFullPath("tmp/PlaybackWithoutReports")+Path.DirectorySeparatorChar;
            if(root.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    public static async Task<object> ProbeRuntime(string profile="baseFlash")
    {
        if(Application.isPlaying || LocalStoryRuntimeService.IsBusy)throw new Exception("Stop gameplay first.");
        var suite=JObject.Parse(File.ReadAllText("Tests/LocalStoryBenchmark/CompositionCases.json"));
        var settings=(JObject)LocalStoryPrompt.Policy["inference"].DeepClone();settings.Merge(suite["comparisonProfiles"][profile]["inference"]);
        string root=Path.Combine(Application.streamingAssetsPath,"LocalStory"),model=LocalStoryTestModels.PathFor(root,LocalStoryTestModel.Gemma);
        LocalStoryLifetime.CancelAndWait();
        return await Task.Run<object>(()=>{var watch=System.Diagnostics.Stopwatch.StartNew();try{
            using var native=new LocalStoryNative(Path.Combine(root,"runtimes","win-x64","native"),model,true,settings);
            return new{supported=true,seconds=watch.Elapsed.TotalSeconds,backend=native.Backend,settings};
        }catch(Exception e){return new{supported=false,seconds=watch.Elapsed.TotalSeconds,error=e.Message,settings};}});
    }
    public static async Task<object> Run(string profile="baseline",int seed=12345)
    {
        if(Application.isPlaying || LocalStoryRuntimeService.IsBusy)throw new Exception("Stop gameplay/generation first.");
        ChapterContentCatalog.Reset();
        var suite=JObject.Parse(File.ReadAllText("Tests/LocalStoryBenchmark/CompositionCases.json"));
        var patch=suite["comparisonProfiles"]?[profile] as JObject??throw new ArgumentException("Unknown profile");
        var policy=LocalStoryPrompt.Policy;var prior=(JObject)policy.DeepClone();var results=new JArray();
        using var stop=new CancellationTokenSource(TimeSpan.FromMinutes(6));
        void Cancel(UnityEditor.PlayModeStateChange state){if(state==UnityEditor.PlayModeStateChange.ExitingEditMode)stop.Cancel();}
        UnityEditor.EditorApplication.playModeStateChanged+=Cancel;
        try
        {
            policy.Merge(patch,new Newtonsoft.Json.Linq.JsonMergeSettings{MergeArrayHandling=MergeArrayHandling.Replace});
            foreach(var scenario in suite["cases"])
            {
                var report=new LocalStoryTestReport{storyPath=(string)scenario["storyPath"],model=LocalStoryTestModels.Name(LocalStoryTestModel.Gemma),
                    original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource((string)scenario["storyPath"])),
                    interactionMemory=scenario["memory"].ToObject<LocalStoryRevisionRequest>(),seed=seed,allowFailedOutput=true};
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds((int)suite["timeoutSecondsPerCase"]));
                var watch=System.Diagnostics.Stopwatch.StartNew();
                try{await LocalStoryRuntimePipeline.Infer(report,timeout.Token);}catch(Exception e){report.runtimeError=e.Message;}
                results.Add(new JObject{["case"]=(string)scenario["id"],["seconds"]=watch.Elapsed.TotalSeconds,["draftRequests"]=report.sectionsGenerated,
                    ["repairRequests"]=report.sectionsCorrected,["valid"]=report.correctionAttempted?report.correctionValid:report.valid,
                    ["reviewRequired"]=report.reviewRequired,["error"]=report.runtimeError??report.reviewError??report.correctionError??report.error,
                    ["backend"]=report.backend,["fallback"]=report.fallback,["raw"]=report.rawOutput,["correction"]=report.correctionRawOutput,
                    ["review"]=report.reviewRawOutput,["sections"]=JArray.FromObject(report.sectionOutputs),["requests"]=JArray.FromObject(report.sectionInputs)});
            }
            return new JObject{["profile"]=profile,["seed"]=seed,["settings"]=policy.DeepClone(),["actualSaveWrites"]=false,["reportFilesWritten"]=0,["results"]=results};
        }
        finally
        {
            policy.RemoveAll();foreach(var item in prior.Properties())policy.Add(item.Name,item.Value.DeepClone());
            UnityEditor.EditorApplication.playModeStateChanged-=Cancel;
        }
    }
}
