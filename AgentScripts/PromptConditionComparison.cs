using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Synthetic, isolated comparison. All scene definitions and review criteria are JSON data.
public static class PromptConditionComparison
{
    public static async Task<object> Run(string variant="default")
    {
        if(Application.isPlaying||LocalStoryRuntimeService.IsBusy)throw new Exception("Stop gameplay/generation first.");
        ChapterContentCatalog.Reset();
        var suite=JObject.Parse(File.ReadAllText("Tests/LocalStoryBenchmark/CompositionCases.json"));
        if(variant!="default")
        {
            var selection=suite["variants"]?[variant]??throw new ArgumentException("Unknown comparison variant.");
            var ids=selection["caseIds"].Values<string>().ToArray();
            suite["cases"]=new JArray(suite["cases"].Where(c=>ids.Contains((string)c["id"])).Select(c=>c.DeepClone()));
            foreach(string key in new[]{"seeds","exampleLimits"})suite[key]=selection[key].DeepClone();
        }
        string root=Path.GetFullPath("tmp/PromptConditionComparison/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+variant);Directory.CreateDirectory(root);
        var composition=LocalStoryPrompt.Policy["composition"];int previous=(int)composition["maxExamples"];
        var results=new JArray();using var all=new CancellationTokenSource(TimeSpan.FromMinutes(15));
        void CancelOnPlay(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode)all.Cancel();}
        EditorApplication.playModeStateChanged+=CancelOnPlay;
        try
        {
            foreach(var scenario in suite["cases"])
            foreach(int seed in suite["seeds"].Values<int>())
            foreach(int maxExamples in suite["exampleLimits"].Values<int>())
            {
                all.Token.ThrowIfCancellationRequested();if(Application.isPlaying)throw new OperationCanceledException();
                LocalStoryLifetime.CancelAndWait();composition["maxExamples"]=maxExamples;
                var report=new LocalStoryTestReport{storyPath=(string)scenario["storyPath"],model=LocalStoryTestModels.Name(LocalStoryTestModel.Gemma),
                    original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource((string)scenario["storyPath"])),
                    interactionMemory=scenario["memory"].ToObject<LocalStoryRevisionRequest>(),seed=seed,sourceRun=1,targetRun=2,allowFailedOutput=true,
                    createdUtc=DateTime.UtcNow.ToString("o"),testScenario="Synthetic / "+(string)scenario["id"]+" / examples="+maxExamples+" / seed="+seed};
                var watch=System.Diagnostics.Stopwatch.StartNew();
                report.sectionProgress=(start,count,index,total,phase)=>File.WriteAllText(Path.Combine(root,"Progress.json"),new JObject{
                    ["case"]=(string)scenario["id"],["seed"]=seed,["maxExamples"]=maxExamples,["completedCases"]=results.Count,
                    ["section"]=index,["sections"]=total,["phase"]=phase,["seconds"]=watch.Elapsed.TotalSeconds}.ToString());
                using var budget=CancellationTokenSource.CreateLinkedTokenSource(all.Token);budget.CancelAfter(TimeSpan.FromSeconds((int)suite["timeoutSecondsPerCase"]));
                try{await LocalStoryRuntimePipeline.Infer(report,budget.Token);}
                catch(Exception error){report.runtimeError=error.ToString();}
                report.seconds=watch.Elapsed.TotalSeconds;
                string file=LocalStoryTestArchive.Save(Path.Combine(root,"LocalStoryTests"),JObject.Parse(JsonUtility.ToJson(report)));
                results.Add(new JObject{["case"]=(string)scenario["id"],["seed"]=seed,["maxExamples"]=maxExamples,["seconds"]=report.seconds,
                    ["automaticChecksPassed"]=report.correctionAttempted?report.correctionValid:report.valid,["humanQualityAccepted"]=JValue.CreateNull(),
                    ["draftRequests"]=report.sectionsGenerated,["repairRequests"]=report.sectionsCorrected,["reviewRequired"]=report.reviewRequired,
                    ["error"]=report.runtimeError??report.reviewError??report.correctionError??report.error,["report"]=file,
                    ["reviewCriteria"]=scenario["review"].DeepClone()});
                File.WriteAllText(Path.Combine(root,"Results.json"),results.ToString(),new UTF8Encoding(false));
            }
        }
        finally
        {
            composition["maxExamples"]=previous;EditorApplication.playModeStateChanged-=CancelOnPlay;
            LocalStoryLifetime.CancelAndWait();
        }
        return new{root,cases=results.Count,actualSaveWrites=false,report=Path.Combine(root,"Results.json")};
    }
}
