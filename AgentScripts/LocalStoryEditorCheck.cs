using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;
public static class LocalStoryEditorCheck
{
    private static void Assert(bool condition,string reason) { if(!condition) throw new Exception(reason); }
    private static string Patch(EncounterCardRoot root)
    {
        return "{\"cards\":[" + string.Join(",",root.MainStory.Select((c,i)=>"{\"index\":"+i+",\"text\":"+LocalStoryRewrite.Quote(c.text)+",\"options\":["+string.Join(",",(c.options??Array.Empty<string>()).Select(LocalStoryRewrite.Quote))+"]}"))+"]}";
    }
    public static string Regression()
    {
        int checks=0;
        foreach(string path in LocalStoryTestPanel.DiscoverPaths())
        {
            var root=JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text);
            string valid=Patch(root);
            Assert(LocalStoryRewrite.TryBuild(valid,root,null,out var result,out var error),path+": "+error); checks++;
            Assert(JsonUtility.ToJson(root)==JsonUtility.ToJson(result),"roundtrip "+path); checks++;
            Assert(!LocalStoryRewrite.TryBuild(valid+" junk",root,null,out _,out _),"trailing "+path); checks++;
            Assert(!LocalStoryRewrite.TryBuild(valid.Replace("\"index\":0","\"index\":1"),root,null,out _,out _),"index "+path); checks++;
            Assert(!LocalStoryRewrite.TryBuild(valid.Replace("\"cards\":","\"rules\":0,\"cards\":"),root,null,out _,out _),"injected key "+path); checks++;
            Assert(!LocalStoryRewrite.TryBuild(valid.Substring(0,valid.Length-1),root,null,out _,out _),"truncation "+path); checks++;
        }
        var single=new EncounterCardRoot{MainStory=new System.Collections.Generic.List<EncounterCard>{new EncounterCard{type="Next",text="{왕국 광산}에서 5개를 돌려주지.",speakerId="Guide"}}};
        Assert(!LocalStoryRewrite.TryBuild(Patch(single).Replace("5개","6개"),single,null,out _,out _),"numeric mutation"); checks++;
        Assert(!LocalStoryRewrite.TryBuild(Patch(single).Replace("왕국 광산","다른 광산"),single,null,out _,out _),"core mutation"); checks++;
        Assert(!LocalStoryRewrite.TryBuild(Patch(single).Replace("{왕국 광산}","로키: {왕국 광산}"),single,null,out _,out _),"role prefix"); checks++;
        Assert(LocalStoryRewrite.Similarity(single,single)==1,"similarity"); checks++;
        return checks+" checks passed; original resources/real player saves untouched.";
    }
    public static string ReportRegression()
    {
        var root = new EncounterCardRoot { MainStory = new System.Collections.Generic.List<EncounterCard> {
            new EncounterCard { type="Next", text="어서 오게." },
            new EncounterCard { type="Choice", text="무엇을 원하나?", options=new[]{"이야기를 듣겠다."} }
        }};
        string before = JsonUtility.ToJson(root);
        var report = new LocalStoryTestReport { rawOutput=Patch(root), correctionRawOutput="교정 원문", correctionError="기존 교정 결과" };
        LocalStoryRewrite.EvaluateDraft(report, root, null);
        Assert(report.valid && report.unchangedCards==2 && report.changedCards==0 && report.literalSimilarity==1,"unchanged report");
        LocalStoryRewrite.EvaluateDraft(report, root, null);
        Assert(report.unchangedCards==2,"repeat evaluation accumulated counts");
        report.rawOutput=report.rawOutput.Replace("어서 오게.","여기까지 왔군.").Replace("이야기를 듣겠다.","설명을 듣겠다.");
        string raw=report.rawOutput;
        LocalStoryRewrite.EvaluateDraft(report, root, null);
        Assert(report.valid && report.changedCards==2 && report.unchangedCards==0 && report.literalSimilarity<1,"changed report");
        Assert(report.rawOutput==raw && report.correctionRawOutput=="교정 원문" && report.correctionError=="기존 교정 결과","draft/correction raw mutated");
        report.rawOutput="broken JSON";
        LocalStoryRewrite.EvaluateDraft(report, root, null);
        Assert(!report.valid && report.generated==null && !string.IsNullOrEmpty(report.error),"invalid draft replaced with source/stale candidate");
        Assert(report.changedCards==0 && report.unchangedCards==0 && report.literalSimilarity==0,"stale metrics retained");
        Assert(report.rawOutput=="broken JSON" && JsonUtility.ToJson(root)==before,"failed raw/source changed");
        return "7 checks passed: shared metrics, repeat-safe counts, invalid raw preserved, no source/correction mutation.";
    }
    public static async System.Threading.Tasks.Task<string> Infer()
    {
        string path="Initial/Initial_01/Encounters/guide";
        var root=JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text);
        root.MainStory = root.MainStory.Take(3).ToList();
        var memory=new LocalStoryRevisionRequest { sourceRun=1,targetRun=2,chapterId="Initial",events=new System.Collections.Generic.List<StoryEventRecord>{
            new StoryEventRecord {chapterId="Initial",characterId="Guide",characterName="로키",terrainName="Dungeon",kind="choice",choiceId="loki_insult",semanticText="꺼져!",relationshipDelta=-2}}};
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var output=await LocalStoryInference.Generate(System.IO.Path.Combine(Application.streamingAssetsPath,"LocalStory"),LocalStoryRewrite.Messages(path,root,memory),LocalStoryRewrite.Grammar(root),12345,System.Threading.CancellationToken.None,null);
        bool valid=LocalStoryRewrite.TryBuild(output.json,root,memory,out var result,out var error);
        string summary=$"Seconds={watch.Elapsed.TotalSeconds:F2}; Backend={output.backend}; Fallback={output.fallback}; Valid={valid}; Error={error}\n{output.json}";
        SessionState.SetString("LocalStorySyntheticResult",summary);
        return summary;
    }
    public static string BriefRegression()
    {
        string path=LocalStoryTestInputs.GuidePath;
        var root=JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text);
        string before=JsonUtility.ToJson(root);
        Assert(LocalStoryRewrite.WritingBrief(path,root)!=null,"Guide brief not loaded");
        string first=LocalStoryRewrite.Messages(path,root,LocalStoryTestInputs.Create(1));
        Assert(!first.Contains(root.MainStory[0].text),"original prose still used as Guide template");
        Assert(first.Contains(LocalStoryRewrite.WritingPolicy) && first.Contains("첫 만남이다"),"first-meeting input missing");
        Assert(first.Contains(root.MainStory[4].semanticText) && !first.Contains(root.MainStory[4].text),"phonetic meaning missing / prose still fixed");
        var positive=LocalStoryTestInputs.Create(2); var negative=LocalStoryTestInputs.Create(3);
        Assert(LocalStoryRewrite.Messages(path,root,positive).Contains("반가워한다"),"friendly direction missing");
        Assert(LocalStoryRewrite.Messages(path,root,negative).Contains("빈정거림"),"insult direction missing");
        negative.events[0].semanticText="과거기록유출검사용";
        negative.events[0].characterId="Gambler";
        Assert(!LocalStoryRewrite.Messages(path,root,negative).Contains("과거기록유출검사용"),"unrelated NPC memory leaked");
        negative.events[0].characterId="Guide"; negative.events[0].chapterId="Chapter_1";
        Assert(!LocalStoryRewrite.Messages(path,root,negative).Contains("과거기록유출검사용"),"unrelated chapter memory leaked");
        var changed=JsonUtility.FromJson<EncounterCardRoot>(before); changed.MainStory[0].eventIdStable="other";
        bool rejected=false;
        try { LocalStoryRewrite.Messages(path,changed,null); } catch(InvalidOperationException) { rejected=true; }
        Assert(rejected,"stale outline attached to changed Story order");
        Assert(JsonUtility.ToJson(root)==before,"Guide source mutated");
        return "10 checks passed: scene brief, no full source prose, phonetic meaning not fixed wording, scenario separation, memory filtering, stale binding rejection, source unchanged.";
    }
    public static async System.Threading.Tasks.Task<string> QualityCase(int scenario=1,int seed=12345)
    {
        string path=LocalStoryTestInputs.GuidePath;
        var root=JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text);
        string before=JsonUtility.ToJson(root);
        var memory=LocalStoryTestInputs.Create(scenario);
        var report=new LocalStoryTestReport {storyPath=path,original=root,sourceRun=memory?.sourceRun??1,targetRun=memory?.targetRun??2,
            testScenario="Synthetic / "+LocalStoryTestInputs.Name(scenario),seed=seed,createdUtc=DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")};
        report.prompt=LocalStoryRewrite.Messages(path,root,memory);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var output=await LocalStoryInference.Generate(System.IO.Path.Combine(Application.streamingAssetsPath,"LocalStory"),report.prompt,
            LocalStoryRewrite.Grammar(root),seed,System.Threading.CancellationToken.None,null);
        report.seconds=watch.Elapsed.TotalSeconds; report.backend=output.backend; report.rawOutput=output.json; report.fallback=output.fallback;
        LocalStoryRewrite.EvaluateDraft(report, root, memory);
        Assert(JsonUtility.ToJson(root)==before,"source mutated by inference");
        string saved=SaveIOService.Instance.SaveLocalStoryTest(report);
        SessionState.SetString("LocalStoryQuality"+scenario,JsonUtility.ToJson(report));
        return $"Scenario={report.testScenario}; Seconds={report.seconds:F2}; Valid={report.valid}; Error={report.error}; Changed={report.changedCards}; Similarity={report.literalSimilarity:F3}; Test report={saved}\n{report.rawOutput}";
    }
    public static string CorrectionRegression()
    {
        string path=LocalStoryTestInputs.GuidePath;
        var root=JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text);
        string before=JsonUtility.ToJson(root), raw=Patch(root);
        string prompt=LocalStoryRewrite.CorrectionMessages(path,root,LocalStoryTestInputs.Create(3),raw);
        string escaped=LocalStoryRewrite.Quote(raw);
        Assert(prompt.Contains(escaped.Substring(1,escaped.Length-2)),"draft absent from proofreading prompt");
        Assert(prompt.Contains("난독") && prompt.Contains("꺼져!"),"fixed wording / memory missing");
        bool rejected=false; try { LocalStoryRewrite.CorrectionMessages(path,root,null,raw.Substring(0,raw.Length-1)); } catch(FormatException) { rejected=true; }
        Assert(rejected,"truncated draft sent for correction");
        rejected=false; try { LocalStoryRewrite.CorrectionMessages(path,root,null,raw.Replace(root.MainStory[4].text,root.MainStory[4].semanticText)); } catch(FormatException) { rejected=true; }
        Assert(!rejected,"normalised garbling cannot reach correction");
        Assert(!LocalStoryRewrite.TryBuild(raw.Replace(root.MainStory[4].text,root.MainStory[4].semanticText),root,null,out _,out _),"normalised garbling can be applied without repair");
        Assert(LocalStorySampling.Legacy().Parameters(12345)=="{\"n_predict\":1800,\"temperature\":0.65,\"top_p\":0.9,\"seed\":12345,\"cache_prompt\":false}","legacy sampling changed");
        Assert(LocalStorySampling.Recommended().Parameters(12345).Contains("\"top_k\":20,\"min_p\":0"),"recommended params missing");
        var report=new LocalStoryTestReport {rawOutput=raw,correctionAttempted=true,correctionRawOutput="잘못된 교정 출력",correctionError="failed"};
        string display=LocalStoryTestPanel.DisplayOutput(report);
        Assert(display.Contains(LocalStoryTestArchive.DialogueText(raw)) && display.Contains("잘못된 교정 출력"),"failed correction or draft hidden");
        Assert(report.rawOutput==raw && report.correctionRawOutput=="잘못된 교정 출력","raw output mutated");
        Assert(JsonUtility.ToJson(root)==before,"source mutated");
        Assert(LocalStoryRewrite.Grammar(root).Contains("ws ::= [ \\t\\n\\r]?"),"unbounded whitespace loop allowed");
        return "11 checks passed: draft/correction separation, malformed input rejected, normalised draft reaches repair but fails application, old sampling preserved, recommended explicit, bounded whitespace, source unchanged.";
    }
    public static async System.Threading.Tasks.Task<string> StartTwoPass(int scenario=3,bool recommended=true)
    {
        Assert(SessionState.GetString("TwoPassStatus","")!="running","quality test already running");
        SessionState.SetString("TwoPassStatus","running");
        await TwoPass(scenario,recommended);
        return TwoPassStatus();
    }
    public static string TwoPassStatus()
    {
        string status=SessionState.GetString("TwoPassStatus","idle");
        return status=="running"?status:status.Split(new[]{'\n'},2)[0];
    }
    private static async System.Threading.Tasks.Task TwoPass(int scenario,bool recommended)
    {
        var report=new LocalStoryTestReport {storyPath=LocalStoryTestInputs.GuidePath,seed=12345,testScenario="Synthetic / "+LocalStoryTestInputs.Name(scenario),createdUtc=DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")};
        var watch=System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var root=JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Asset("Story_Json_Data/"+report.storyPath+"/Story").text); report.original=root;
            string before=JsonUtility.ToJson(root);
            var memory=LocalStoryTestInputs.Create(scenario);
            var preset=recommended?LocalStorySampling.Recommended():LocalStorySampling.Legacy();
            report.samplingProfile=preset.Name; report.draftParameters=preset.Parameters(report.seed);
            report.prompt=LocalStoryRewrite.Messages(report.storyPath,root,memory);
            string location=System.IO.Path.Combine(Application.streamingAssetsPath,"LocalStory"), grammar=LocalStoryRewrite.Grammar(root);
            using var cancellation=new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
            var draft=await LocalStoryInference.Generate(location,report.prompt,grammar,report.seed,cancellation.Token,t=>report.rawOutput=t,preset);
            report.rawOutput=draft.json; report.backend=draft.backend; report.fallback=draft.fallback; report.draftSeconds=watch.Elapsed.TotalSeconds;
            LocalStoryRewrite.EvaluateDraft(report, root, memory);
            report.correctionPrompt=LocalStoryRewrite.CorrectionMessages(report.storyPath,root,memory,report.rawOutput);
            report.correctionAttempted=true; var correctionWatch=System.Diagnostics.Stopwatch.StartNew(); var correctionPreset=LocalStorySampling.Proofreading();
            report.correctionParameters=correctionPreset.Parameters(report.seed);
            try
            {
                var correction=await LocalStoryInference.Generate(location,report.correctionPrompt,grammar,report.seed,cancellation.Token,t=>report.correctionRawOutput=t,correctionPreset);
                report.correctionRawOutput=correction.json; report.correctionBackend=correction.backend; report.correctionFallback=correction.fallback;
                report.correctionValid=LocalStoryRewrite.TryBuild(correction.json,root,memory,out report.corrected,out report.correctionError);
            }
            finally { report.correctionSeconds=correctionWatch.Elapsed.TotalSeconds; }
            Assert(JsonUtility.ToJson(root)==before,"source changed");
        }
        catch(Exception e) { if(report.correctionAttempted) report.correctionError=e.Message; else report.error=e.Message; }
        finally
        {
            report.seconds=watch.Elapsed.TotalSeconds;
            string saved=SaveIOService.Instance.SaveLocalStoryTest(report);
            SessionState.SetString("TwoPassReport",JsonUtility.ToJson(report));
            SessionState.SetString("TwoPassStatus",$"Completed {report.samplingProfile}: draft={report.draftSeconds:F2}s valid={report.valid} error={report.error}; correction={report.correctionSeconds:F2}s valid={report.correctionValid} error={report.correctionError}; Saved={saved}\nDraft={report.rawOutput}\nCorrection={report.correctionRawOutput}");
        }
    }
    public static string Persistence()
    {
        var field=typeof(SaveIOService).GetField("_instance",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        var previous=field.GetValue(null);
        var ctor=typeof(SaveIOService).GetConstructor(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(string)},null);
        string temp=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"OneWayLocalStory_"+Guid.NewGuid().ToString("N"));
        var isolated=(SaveIOService)ctor.Invoke(new object[]{temp}); field.SetValue(null,isolated);
        try
        {
            string path="Initial/Initial_01/Encounters/guide";
            var root=JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text);
            var changed=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(root));
            changed.MainStory[0].text="자네, 여기에 있으면 안 된다고.";
            changed.MainStory[8].options[0]="정말 신이라는 거야?";
            Assert(isolated.LoadLatestLocalStoryRevision()==null,"unexpected existing request");
            Assert(isolated.SaveGeneratedContent(2,"Encounters",path+"/Story",changed),"save failed");
            Assert(new EncounterContentRepository(2).TryLoadCards(path,out var loaded,out var error),error);
            Assert(loaded.MainStory[0].text==changed.MainStory[0].text && loaded.MainStory[8].options[0]==changed.MainStory[8].options[0],"wording not applied next run");
            Assert(new EncounterContentRepository(1).TryLoadCards(path,out var first,out error)&&first.MainStory[0].text==root.MainStory[0].text,"run 1 changed");
            changed.MainStory[8].choiceActions[0].action="skip";
            isolated.SaveGeneratedContent(2,"Encounters",path+"/Story",changed);
            Assert(new EncounterContentRepository(2).TryLoadCards(path,out loaded,out error)&&loaded.MainStory[0].text==root.MainStory[0].text,"unsafe result did not fall back");
            var request=new LocalStoryRevisionRequest{sourceRun=2,targetRun=3,chapterId="Initial"};
            isolated.SaveRunCheckpoint(2,"LocalStoryRevisionRequest",request);
            Assert(isolated.LoadLatestLocalStoryRevision()?.targetRun==3,"request read failed");
            isolated.SaveRunCheckpoint(9999,"LocalStoryRevisionRequest",new LocalStoryRevisionRequest{sourceRun=9999,targetRun=10000});
            isolated.SaveRunCheckpoint(10000,"LocalStoryRevisionRequest",new LocalStoryRevisionRequest{sourceRun=10000,targetRun=10001});
            Assert(isolated.LoadLatestLocalStoryRevision()?.targetRun==10001,"numeric run ordering failed");
            changed=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(root));
            changed.MainStory[4].text="알아듣지 못하겠지?";
            isolated.SaveGeneratedContent(2,"Encounters",path+"/Story",changed);
            Assert(new EncounterContentRepository(2).TryLoadCards(path,out loaded,out error)&&loaded.MainStory[4].text==root.MainStory[4].text,"fixed phonetic wording changed");
            return "Persistence passed: next-run dialogue/options applied; run 1/source preserved; changed rule rejected atomically; isolated storage="+temp;
        }
        finally{field.SetValue(null,previous);}
    }
    public static string OpenPanel()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null,"Direct DecisionScene Play required"); controller.Open();
        return "opened; UI-only, no generation or game save change";
    }
    public static string GeneratePanelGuide()
    { return GenerateGuideScenario(0); }
    public static string GenerateGuideScenario(int scenario=3,bool useWritingExamples=true)
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null,"Direct DecisionScene Play required");
        var fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var paths=(System.Collections.Generic.List<string>)typeof(LocalStoryTestPanel).GetField("paths",fields).GetValue(controller);
        typeof(LocalStoryTestPanel).GetField("selected",fields).SetValue(controller,paths.IndexOf("Initial/Initial_01/Encounters/guide"));
        typeof(LocalStoryTestPanel).GetField("inputScenario",fields).SetValue(controller,scenario);
        UnityEngine.Object.FindFirstObjectByType<DecisionMiniGameTestLauncher>()?.StopTest();
        controller.Open();
        var button=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(b=>b.name=="로컬 생성");
        if(useWritingExamples) button.onClick.Invoke(); else controller.GenerateWithoutWritingExamplesForComparison();
        return (useWritingExamples?"Started via actual generation button":"Started via test-only no-example comparison entry")+"; scenario="+LocalStoryTestInputs.Name(scenario)+"; no next-run apply.";
    }
    public static string TestInputSwitch()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null && !controller.IsGenerating,"idle test panel required");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var paths=(System.Collections.Generic.List<string>)typeof(LocalStoryTestPanel).GetField("paths",flags).GetValue(controller);
        typeof(LocalStoryTestPanel).GetField("selected",flags).SetValue(controller,paths.IndexOf(LocalStoryTestInputs.GuidePath));
        var scenarioField=typeof(LocalStoryTestPanel).GetField("inputScenario",flags);
        scenarioField.SetValue(controller,0); controller.Open();
        var button=(Button)typeof(LocalStoryTestPanel).GetField("inputButton",flags).GetValue(controller);
        for(int i=1;i<=3;i++)
        {
            button.onClick.Invoke();
            Assert((int)scenarioField.GetValue(controller)==i,"scenario did not cycle");
            var memory=(LocalStoryRevisionRequest)typeof(LocalStoryTestPanel).GetField("memory",flags).GetValue(controller);
            Assert(i==1?memory==null:memory.events.Single().relationshipDelta==(i==2?1:-2),"wrong synthetic memory");
            var label=(TMP_Text)typeof(LocalStoryTestPanel).GetField("contextLabel",flags).GetValue(controller);
            Assert(label.text.Contains("테스트 전용") && label.text.Contains(LocalStoryTestInputs.Name(i)),"synthetic input not clearly labelled");
        }
        button.onClick.Invoke(); Assert((int)scenarioField.GetValue(controller)==0,"did not return to actual records");
        controller.Close();
        return "Input cycle passed: actual / first / friendly / insult; synthetic memory labelled; no generation or player-save writes.";
    }
    public static string Workers()
    {
        var workers=(System.Collections.ICollection)typeof(LocalStoryLifetime).GetField("Workers",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
        return "active workers="+workers.Count;
    }
    public static string ModelSelectionRegression()
    {
        string path=LocalStoryTestInputs.GuidePath, runtime=System.IO.Path.Combine(Application.streamingAssetsPath,"LocalStory");
        var source=JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text);
        string before=JsonUtility.ToJson(source); int checks=0;
        var probe=new GameObject("DefaultModelCheck");probe.SetActive(false);
        try {Assert(probe.AddComponent<LocalStoryTestPanel>().SelectedModel==LocalStoryTestModel.Gemma,"new test panel must default to Gemma");checks++;}
        finally {UnityEngine.Object.DestroyImmediate(probe);}
        foreach(int scenario in new[]{1,2,3})
        {
            string prompt=LocalStoryRewrite.Messages(path,source,LocalStoryTestInputs.Create(scenario));
            Assert(LocalStoryTestModels.Messages(LocalStoryTestModel.Qwen,prompt)==prompt,"Qwen prompt changed"); checks++;
            var input=Newtonsoft.Json.Linq.JArray.Parse(prompt);
            var gemma=Newtonsoft.Json.Linq.JArray.Parse(LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,prompt));
            Assert(gemma.Count==1 && (string)gemma[0]["role"]=="user" &&
                (string)gemma[0]["content"]==(string)input[0]["content"]+"\n\n"+(string)input[1]["content"],"Gemma instructions altered"); checks++;
            string correction=LocalStoryRewrite.CorrectionMessages(path,source,LocalStoryTestInputs.Create(scenario),Patch(source));
            var c=Newtonsoft.Json.Linq.JArray.Parse(correction);
            var g=Newtonsoft.Json.Linq.JArray.Parse(LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,correction));
            Assert(g.Count==1 && (string)g[0]["content"]==(string)c[0]["content"]+"\n\n"+(string)c[1]["content"],"correction adapter changed instructions"); checks++;
        }
        Assert(LocalStorySampling.Comparison().Parameters(12345)==LocalStorySampling.Recommended().Parameters(12345),"comparison changed generation settings"); checks++;
        Assert(LocalStoryTestModels.PathFor(runtime,LocalStoryTestModel.Qwen)==System.IO.Path.Combine(runtime,LocalStoryTestModels.QwenFile),"Qwen location changed"); checks++;
        string gemmaPath=LocalStoryTestModels.PathFor(runtime,LocalStoryTestModel.Gemma);
        Assert(gemmaPath==System.IO.Path.GetFullPath("Tests/LocalStoryBenchmark/Models/"+LocalStoryTestModels.GemmaFile),"Gemma wrong location"); checks++;
        var manifest=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("Tests/LocalStoryBenchmark/GemmaManifest.json"));
        Assert((string)manifest["sha256"]==LocalStoryTestModels.GemmaSha256 && (long)manifest["bytes"]==LocalStoryTestModels.GemmaBytes,"Gemma identity differs from accepted comparison"); checks++;
        bool rejected=false;
        try { LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,"[]"); } catch(InvalidOperationException) { rejected=true; }
        Assert(rejected,"unsupported prompt accepted"); checks++;
        rejected=false;
        try { LocalStoryTestModels.VerifyGemma(gemmaPath+".missing",System.Threading.CancellationToken.None); } catch(System.IO.FileNotFoundException) { rejected=true; }
        Assert(rejected,"missing Gemma silently substituted"); checks++;
        using(var cancel=new System.Threading.CancellationTokenSource())
        {
            cancel.Cancel(); rejected=false;
            try { LocalStoryTestModels.VerifyGemma(gemmaPath,cancel.Token); } catch(OperationCanceledException) { rejected=true; }
            Assert(rejected,"file validation ignores cancellation"); checks++;
        }
        Assert(JsonUtility.ToJson(source)==before,"source modified"); checks++;
        return checks+" checks passed: both model paths, identical instructions for first/friendly/insult + correction, unchanged settings, missing-file/cancel guards, source unchanged.";
    }
    public static async System.Threading.Tasks.Task<string> ModelRequestCancellation()
    {
        string root=System.IO.Path.Combine(Application.streamingAssetsPath,"LocalStory");
        using(var cancel=new System.Threading.CancellationTokenSource())
        {
            cancel.Cancel(); bool rejected=false;
            try { await LocalStoryInference.Generate(root,"[]","root ::= \"x\"",12345,cancel.Token,null,null,
                LocalStoryTestModels.PathFor(root,LocalStoryTestModel.Gemma)); }
            catch(OperationCanceledException) { rejected=true; }
            Assert(rejected,"cancelled Gemma request executed");
        }
        return "Cancelled Gemma request rejected before loading; existing generation unaffected.";
    }
    public static async System.Threading.Tasks.Task<string> ModelSelectionUI()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null && !controller.IsGenerating,"idle Decision test panel required");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var type=typeof(LocalStoryTestPanel);
        var saved=controller.SelectedModel;
        type.GetField("readingSaved",flags).SetValue(controller,false);
        controller.Open();
        // A newly opened camera-space Canvas needs a rendered frame before its screen-space raycast test.
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        await System.Threading.Tasks.Task.Delay(150);
        var model=(UnityEngine.UI.Button)type.GetField("modelButton",flags).GetValue(controller);
        var generate=(UnityEngine.UI.Button)type.GetField("generateButton",flags).GetValue(controller);
        string original=((TMP_Text)type.GetField("originalLabel",flags).GetValue(controller)).text;
        model.onClick.Invoke();
        Assert(controller.SelectedModel!=saved && generate.GetComponentInChildren<TMP_Text>().text.Contains(controller.SelectedModel.ToString()),"model button/generate label mismatch");
        Assert(((TMP_Text)type.GetField("originalLabel",flags).GetValue(controller)).text==original,"model switch changed source");
        model.onClick.Invoke(); Assert(controller.SelectedModel==saved,"model did not cycle");
        Canvas.ForceUpdateCanvases();
        var canvas=model.GetComponentInParent<Canvas>(); var rect=(RectTransform)model.transform;
        var point=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,rect.TransformPoint(rect.rect.center));
        var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        UnityEngine.EventSystems.EventSystem.current.RaycastAll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=point},hits);
        Assert(hits.Count>0 && hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>()==model,"model selection button blocked");
        var oldJob=type.GetField("job",flags).GetValue(controller); var pending=new System.Threading.Tasks.TaskCompletionSource<bool>();
        try
        {
            type.GetField("job",flags).SetValue(controller,pending.Task); type.GetMethod("SetBusy",flags).Invoke(controller,new object[]{true});
            Assert(!model.interactable && !generate.interactable,"model not locked while generating");
            model.onClick.Invoke(); Assert(controller.SelectedModel==saved,"busy callback changed selected model");
        }
        finally { pending.SetResult(true); type.GetField("job",flags).SetValue(controller,oldJob); type.GetMethod("SetBusy",flags).Invoke(controller,new object[]{false}); }
        type.GetField("readingSaved",flags).SetValue(controller,true);
        type.GetMethod("SetBusy",flags).Invoke(controller,new object[]{false});
        Assert(!model.interactable && !generate.interactable,"saved viewer allows generation/model changes");
        model.onClick.Invoke(); Assert(controller.SelectedModel==saved,"saved-view callback changed model");
        type.GetField("readingSaved",flags).SetValue(controller,false); controller.Open(); type.GetMethod("SetBusy",flags).Invoke(controller,new object[]{false});
        return "Model UI passed: toggle labels, source preservation, actual raycast, busy callback guard, saved-view guard; no inference or gameplay changes.";
    }
    public static string StartModelGuide(string model="Gemma",int scenario=1,bool useWritingExamples=true)
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null && !controller.IsGenerating,"idle test panel required");
        var requested=(LocalStoryTestModel)Enum.Parse(typeof(LocalStoryTestModel),model);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(LocalStoryTestPanel).GetField("readingSaved",flags).SetValue(controller,false);
        controller.Open();
        if(controller.SelectedModel!=requested)
            ((UnityEngine.UI.Button)typeof(LocalStoryTestPanel).GetField("modelButton",flags).GetValue(controller)).onClick.Invoke();
        Assert(controller.SelectedModel==requested,"requested model not selected");
        string message=GenerateGuideScenario(scenario,useWritingExamples);
        Assert(controller.Report!=null,"generation did not start; inspect test panel status");
        Assert(controller.Report.model==LocalStoryTestModels.Name(requested),"report tagged with wrong model");
        Assert(!((UnityEngine.UI.Button)typeof(LocalStoryTestPanel).GetField("modelButton",flags).GetValue(controller)).interactable,"model unlocked during real generation");
        return message+" model="+controller.Report.model;
    }
    public static string ModelGuideStatus()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null,"test panel missing");
        var r=controller.Report; Assert(r!=null,"report missing");
        if(controller.IsGenerating) return r.model+" / running / draft bytes="+(r.rawOutput?.Length??0)+" / correction="+r.correctionAttempted;
        var file=LocalStoryTestArchive.List().Single(p=>LocalStoryTestArchive.Load(p).archiveId==r.archiveId);
        var loaded=LocalStoryTestArchive.Load(file);
        Assert(loaded.model==r.model && loaded.rawOutput==r.rawOutput && loaded.correctionRawOutput==r.correctionRawOutput,"saved model/output mismatch");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        Assert(((TMP_Text)typeof(LocalStoryTestPanel).GetField("generatedLabel",flags).GetValue(controller)).text==LocalStoryTestPanel.DisplayOutput(r),"raw output replaced/hidden");
        if(r.model==LocalStoryTestModels.Name(LocalStoryTestModel.Gemma))
        {
            Assert(Newtonsoft.Json.Linq.JArray.Parse(r.prompt).Count==1,"draft not adapted for Gemma");
            if(r.correctionPrompt!=null) Assert(Newtonsoft.Json.Linq.JArray.Parse(r.correctionPrompt).Count==1,"correction not adapted for Gemma");
        }
        return r.model+" / completed / draft="+r.draftSeconds.ToString("F1")+"s / correction="+r.correctionSeconds.ToString("F1")+"s / backend="+r.backend+
            " / correction backend="+r.correctionBackend+" / draft error="+r.error+" / correction error="+r.correctionError+" / file="+file;
    }
    public static string ModelSavedUI()
    {
        var p=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(p!=null && !p.IsGenerating,"idle panel required"); p.Open(); p.OpenSavedResults();
        var files=LocalStoryTestArchive.List(); Assert(files.Count>0,"no saved results");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var t=typeof(LocalStoryTestPanel);
        var output=(TMP_Text)t.GetField("generatedLabel",flags).GetValue(p);
        var model=(UnityEngine.UI.Button)t.GetField("modelButton",flags).GetValue(p);
        for(int i=0;i<files.Count;i++)
        {
            t.GetField("savedSelected",flags).SetValue(p,i); t.GetMethod("ShowSavedSelected",flags).Invoke(p,null);
            Assert(!model.interactable && output.text==LocalStoryTestPanel.DisplayOutput(p.Report),"saved result/raw view changed");
            string name=p.Report.model.IndexOf("Gemma",StringComparison.OrdinalIgnoreCase)>=0?"Gemma":"Qwen";
            Assert(model.GetComponentInChildren<TMP_Text>().text.Contains(name),"saved viewer shows selected rather than actual model");
            Assert(System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(files[i])).Length==3 && System.IO.File.Exists(LocalStoryTestArchive.EditPath(files[i])),"unexpected result sidecars");
            var edit=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(LocalStoryTestArchive.EditPath(files[i])));
            Assert(edit["draft"] is Newtonsoft.Json.Linq.JArray,"edit is not simple dialogue lines");
            foreach(var line in (Newtonsoft.Json.Linq.JArray)edit["draft"])
                Assert(line["speaker"]?.Type==Newtonsoft.Json.Linq.JTokenType.String && line["text"]?.Type==Newtonsoft.Json.Linq.JTokenType.String &&
                    line.Children<Newtonsoft.Json.Linq.JProperty>().All(q=>new[]{"speaker","text","options"}.Contains(q.Name)) &&
                    (line["options"]==null || line["options"] is Newtonsoft.Json.Linq.JArray choices && choices.All(q=>q.Type==Newtonsoft.Json.Linq.JTokenType.String)),"edit contains invalid dialogue/options fields");
            t.GetMethod("CycleInput",flags).Invoke(p,null);
            Assert(output.text==LocalStoryTestPanel.DisplayRawOutput(p.Report),"saved raw JSON altered");
            t.GetMethod("CycleInput",flags).Invoke(p,null);
        }
        return files.Count+" saved results verified: actual model label, raw/failed dialogue and JSON, read-only selection, three evidence files + numbered Edits, separate options.";
    }
    public static string WritingExamplesSavedCheck()
    {
        string file=LocalStoryTestArchive.List().Last();
        var report=LocalStoryTestArchive.Load(file);
        Assert(!string.IsNullOrEmpty(report.examplePrompt) && report.exampleSources?.Length>0,"no captured examples");
        Assert(Newtonsoft.Json.Linq.JArray.Parse(report.prompt).Any(m=>((string)m["content"]).Contains(report.examplePrompt)),"draft omitted captured examples");
        Assert(report.correctionAttempted && Newtonsoft.Json.Linq.JArray.Parse(report.correctionPrompt).Any(m=>((string)m["content"]).Contains(report.examplePrompt)),"correction omitted/differed from captured examples");
        var edit=Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(LocalStoryTestArchive.EditPath(file)));
        var raw=LocalStoryTestArchive.RawCards(report.rawOutput);
        Assert(raw!=null && ((Newtonsoft.Json.Linq.JArray)edit["draft"]).Count==raw.Count,"new grouped edit missing cards");
        for(int i=0;i<raw.Count;i++)
        {
            Assert((string)edit["draft"][i]["text"]==(string)raw[i]["text"],"new edit replaced raw text");
            if(((Newtonsoft.Json.Linq.JArray)raw[i]["options"]).Count>0)
                Assert(Newtonsoft.Json.Linq.JToken.DeepEquals(edit["draft"][i]["options"],raw[i]["options"]),"new edit changed/mixed options");
        }
        return report.exampleSummary+" / identical captured examples in both actual prompts / new raw dialogue and separate options preserved / file="+file;
    }
    public static string UI()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null,"test panel missing");
        var buttons=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        var generate=buttons.Single(b=>b.name=="로컬 생성");
        Assert(!buttons.Any(b=>new[]{"자연스러움: 좋음","다시 작성 필요","다음 회차에 적용"}.Contains(b.name)),"removed test controls still exist");
        var canvas=generate.GetComponentInParent<Canvas>();
        Canvas.ForceUpdateCanvases();
        var close=buttons.Single(b=>b.name=="닫기" && b.GetComponentInParent<Canvas>()==canvas);
        var rect=(RectTransform)close.transform;
        var pos=RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,rect.TransformPoint(rect.rect.center));
        var data=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){position=pos};
        var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        UnityEngine.EventSystems.EventSystem.current.RaycastAll(data,hits);
        Assert(hits.Count>0 && hits[0].gameObject.GetComponentInParent<Button>()==close,"modal close blocked by raycast: pos="+pos+" screen="+Screen.width+"x"+Screen.height+" hits="+string.Join(",",hits.Select(h=>h.gameObject.name+":"+h.gameObject.GetComponentInParent<Canvas>()?.name)));
        Canvas.ForceUpdateCanvases();
        var scroll=canvas.GetComponentsInChildren<ScrollRect>().Single(s=>s.name=="Original");
        Assert(scroll.content.rect.height>scroll.viewport.rect.height,"long original not scrollable");
        bool readingSaved=(bool)typeof(LocalStoryTestPanel).GetField("readingSaved",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(controller);
        Assert(generate.interactable==(!controller.IsGenerating && !readingSaved),"busy/viewer button mismatch");
        var input=(Button)typeof(LocalStoryTestPanel).GetField("inputButton",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(controller);
        Assert(input.interactable==!controller.IsGenerating,"Guide input switch busy mismatch");
        Assert(!buttons.Any(b=>b.GetComponentInParent<Canvas>()==canvas && b.name.StartsWith("설정:")),"obsolete sampling toggle remains");
        if(!controller.IsGenerating && controller.Report!=null && !string.IsNullOrEmpty(controller.Report.rawOutput))
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var output=(TMP_Text)typeof(LocalStoryTestPanel).GetField("generatedLabel",flags).GetValue(controller);
            Assert(output.text==LocalStoryTestPanel.DisplayOutput(controller.Report) && !output.richText,"model display differs beyond whitespace formatting");
        }
        return "UI passed: modal raycast, original scroll overflow, busy generate lock, three removed controls absent; output uses whitespace-only formatting.";
    }
    public static string CancelPanel()
    {
        UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(b=>b.name=="취소").onClick.Invoke();
        return "Cancel button invoked; no apply/save progression requested.";
    }
    public static string RawOutputOnlyTest()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null && !controller.IsGenerating,"Idle direct DecisionScene test required");
        controller.Open();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var reportField=typeof(LocalStoryTestPanel).GetField("<Report>k__BackingField",flags);
        var label=(TMP_Text)typeof(LocalStoryTestPanel).GetField("generatedLabel",flags).GetValue(controller);
        string previousLabel=label.text;
        var saveField=typeof(SaveIOService).GetField("_instance",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        var previousSave=saveField.GetValue(null);
        var ctor=typeof(SaveIOService).GetConstructor(flags,null,new[]{typeof(string)},null);
        string temp=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"OneWayFailedOutput_"+Guid.NewGuid().ToString("N"));
        saveField.SetValue(null,ctor.Invoke(new object[]{temp}));
        var report=new LocalStoryTestReport{valid=false,error="검사용 형식 실패",rawOutput="검사를 실패한 생성문도 그대로 보여야 한다.",seed=1,createdUtc=DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")};
        reportField.SetValue(controller,report);
        var buttons=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        var show=typeof(LocalStoryTestPanel).GetMethod("ShowOutput",flags);
        string source=ChapterContentCatalog.Asset("Story_Json_Data/Initial/Initial_01/Encounters/guide/Story").text;
        try
        {
            Assert(!buttons.Any(b=>new[]{"자연스러움: 좋음","다시 작성 필요","다음 회차에 적용"}.Contains(b.name)),"removed controls exist");
            show.Invoke(controller,new object[]{report});
            Assert(label.text==LocalStoryTestPanel.DisplayOutput(report),"invalid raw output replaced");
            report.valid=true;
            // A reconstructed candidate equal to the original must NOT replace actual raw output.
            report.generated=JsonUtility.FromJson<EncounterCardRoot>(source);
            report.rawOutput="{\"cards\":[{\"index\":0,\"text\":\"원문과 다른 모델 출력 그대로\",\"options\":[]}]}";
            show.Invoke(controller,new object[]{report});
            Assert(label.text==LocalStoryTestPanel.DisplayOutput(report),"valid output reconstructed or replaced with source");
            report.correctionAttempted=true; report.correctionRawOutput="교정도 실패한 원시 출력"; report.correctionError="검사용 교정 실패";
            show.Invoke(controller,new object[]{report});
            Assert(label.text==LocalStoryTestPanel.DisplayOutput(report) && label.text.Contains(report.correctionRawOutput),"failed correction hidden");
            Assert(controller.GetComponentsInChildren<Toggle>(true).Length==0,"review control retained");
            string saved=SaveIOService.Instance.SaveLocalStoryTest(report);
            var stored=JsonUtility.FromJson<LocalStoryTestReport>(System.IO.File.ReadAllText(saved));
            Assert(stored.rawOutput==report.rawOutput && !stored.saved,"raw test record incorrect");
            Assert(stored.correctionRawOutput==report.correctionRawOutput && stored.correctionError==report.correctionError,"correction raw/error not stored separately");
            Assert(!System.IO.Directory.Exists(System.IO.Path.Combine(temp,"GeneratedContent")),"game content written by evaluation");
            Assert(ChapterContentCatalog.Asset("Story_Json_Data/Initial/Initial_01/Encounters/guide/Story").text==source,"source modified");
            return "Passed: valid/invalid output displayed without substitution; raw/correction saved unchanged; review control absent; source and game content untouched.";
        }
        finally
        {
            saveField.SetValue(null,previousSave); reportField.SetValue(controller,null); label.text=previousLabel; controller.Close();
        }
    }
    public static string ClosePanel()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var board=(JourneyMapKit.JourneyBoardInput)typeof(LocalStoryTestPanel).GetField("board",flags).GetValue(controller);
        bool previous=(bool)typeof(LocalStoryTestPanel).GetField("inputWasEnabled",flags).GetValue(controller);
        controller.Close();
        Assert(board==null || board.InputEnabled==previous,"input did not restore");
        return "Close restored previous board input; no game saves changed.";
    }
    public static string PanelStatus()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        if(controller==null) return "no panel";
        var r=controller.Report;
        return r==null?$"generating={controller.IsGenerating}; report=none":$"generating={controller.IsGenerating}; seconds={r.seconds:F2}; draftSeconds={r.draftSeconds:F2}; correctionSeconds={r.correctionSeconds:F2}; backend={r.backend}; valid={r.valid}; error={r.error}; correctionAttempted={r.correctionAttempted}; correctionValid={r.correctionValid}; correctionError={r.correctionError}; changed={r.changedCards}; similarity={r.literalSimilarity:F3}\n{r.rawOutput}";
    }
    public static string SnapshotDisplay()
    {
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null && !controller.IsGenerating,"idle panel required");
        if(controller.Report==null) return "No current report to preserve.";
        SessionState.SetString("LocalStoryDisplaySnapshot",JsonUtility.ToJson(controller.Report));
        return "Current model output preserved in Editor session only; no save/content writes.";
    }
    public static string OutputLineBreaks()
    {
        string raw="{\"cards\":[{\"index\":0,\"text\":\"쉼표, 중괄호 },{와 \\\"따옴표\\\"도 유지\\n해.\",\"options\":[\"하나, 둘\",\"셋\"]},{\"index\":1,\"text\":\"다음 대사\",\"options\":[]}]}";
        string formatted=LocalStoryTestPanel.FormatModelOutput(raw);
        Assert(formatted.Contains("},\n\n{"),"cards not separated");
        Assert(formatted.Contains(",\n  \"text\":"),"dialogue not on its own line");
        Assert(formatted.Contains("\"하나, 둘\",\n    \"셋\""),"options not on separate lines");
        Assert(JsonUtility.ToJson(JsonUtility.FromJson<LocalStoryPatch>(raw))==JsonUtility.ToJson(JsonUtility.FromJson<LocalStoryPatch>(formatted)),"JSON data changed by display formatting");
        string incomplete=raw.Substring(0,raw.Length-8);
        Assert(StripOutsideWhitespace(LocalStoryTestPanel.FormatModelOutput(incomplete))==StripOutsideWhitespace(incomplete),"partial output modified or repaired");
        Assert(StripOutsideWhitespace(formatted)==StripOutsideWhitespace(raw),"non-whitespace tokens changed");
        Assert(LocalStoryTestPanel.FormatModelOutput("형식에 실패한 원시 출력")=="형식에 실패한 원시 출력","non-JSON output replaced");
        return "7 checks passed: card/dialogue/option line breaks; quoted content, raw tokens, partial and non-JSON output preserved.";
    }
    private static string StripOutsideWhitespace(string value)
    {
        var b=new System.Text.StringBuilder(); bool quoted=false, escaped=false;
        foreach(char c in value)
        {
            if(quoted || !char.IsWhiteSpace(c)) b.Append(c);
            if(quoted) { if(escaped) escaped=false; else if(c=='\\') escaped=true; else if(c=='"') quoted=false; }
            else if(c=='"') quoted=true;
        }
        return b.ToString();
    }
    public static string RestoreDisplay()
    {
        string snapshot=SessionState.GetString("LocalStoryDisplaySnapshot","");
        Assert(!string.IsNullOrEmpty(snapshot),"display snapshot missing");
        var report=JsonUtility.FromJson<LocalStoryTestReport>(snapshot);
        var controller=UnityEngine.Object.FindFirstObjectByType<LocalStoryTestPanel>();
        Assert(controller!=null && !controller.IsGenerating,"idle panel required");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var paths=(System.Collections.Generic.List<string>)typeof(LocalStoryTestPanel).GetField("paths",flags).GetValue(controller);
        typeof(LocalStoryTestPanel).GetField("selected",flags).SetValue(controller,paths.IndexOf(report.storyPath));
        int scenario=report.testScenario=="Synthetic / 첫 만남"?1:report.testScenario=="Synthetic / 우호 재회"?2:report.testScenario=="Synthetic / 모욕 재회"?3:0;
        typeof(LocalStoryTestPanel).GetField("inputScenario",flags).SetValue(controller,scenario);
        controller.Open();
        typeof(LocalStoryTestPanel).GetField("<Report>k__BackingField",flags).SetValue(controller,report);
        typeof(LocalStoryTestPanel).GetMethod("ShowOutput",flags).Invoke(controller,new object[]{report});
        ((TMP_Text)typeof(LocalStoryTestPanel).GetField("statusLabel",flags).GetValue(controller)).text=report.correctionAttempted?
            $"실제 모욕 재회 시험 / 초안 {report.draftSeconds:F1}초 / 교정 {report.correctionSeconds:F1}초 / {report.samplingProfile}\n초안·교정 형식 검사: {report.valid}/{report.correctionValid} / 의미·자연스러움은 직접 평가":
            $"기존 생성 결과 / {report.seconds:F1}초 / 줄바꿈 표시만 변경 / 재생성하지 않음";
        return "Same raw report restored for display only; no generation or player-save writes.";
    }
    public static string ShowTwoPass()
    {
        string report=SessionState.GetString("TwoPassReport","");
        Assert(!string.IsNullOrEmpty(report),"two pass result missing");
        SessionState.SetString("LocalStoryDisplaySnapshot",report);
        return RestoreDisplay();
    }
    public static object Inspect()
    {
        return string.Join("\n",Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i=>UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path)) + "\n" +
            string.Join("\n",UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(c=>$"CANVAS {c.name} {c.renderMode} {(c.transform as RectTransform)?.sizeDelta}")) + "\n" +
            string.Join("\n",UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(b=>b.name.Contains("Test")).Select(b=>$"BUTTON {b.name} {b.transform.parent.name} {(b.transform as RectTransform)?.anchoredPosition} {(b.transform as RectTransform)?.sizeDelta}")) + "\n" +
            string.Join("\n",UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(t=>$"FONT {t.name} {t.font?.name}").Take(20));
    }
}
