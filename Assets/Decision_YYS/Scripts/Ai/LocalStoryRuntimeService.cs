using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

[Serializable] public sealed class LocalStoryRuntimeResult
{
    public string storyPath, model, status, error, reportPath;
}

public enum LocalStoryLogStage { Request, Working, Complete, Failed, Deferred }

/// <summary>Colour is presentation only; stage labels remain searchable in plain player logs.</summary>
public static class LocalStoryLog
{
    public static string Format(LocalStoryLogStage stage, string message)
    {
        string label, colour;
        switch(stage)
        {
            case LocalStoryLogStage.Request: label="생성 요청"; colour="#C99CFF"; break;
            case LocalStoryLogStage.Working: label="생성 중"; colour="#55D9FF"; break;
            case LocalStoryLogStage.Complete: label="생성 완료"; colour="#74E892"; break;
            case LocalStoryLogStage.Failed: label="생성 실패"; colour="#FF7777"; break;
            default: label="생성 보류"; colour="#FFD166"; break;
        }
        string text="[LocalStory]["+label+"] "+message;
#if UNITY_EDITOR
        // NPC/model/error text must not inject rich-text tags that hide or recolour the stage.
        return "<color="+colour+"><b>"+text.Replace("<","＜").Replace(">","＞")+"</b></color>";
#else
        return text;
#endif
    }
    public static void Write(LocalStoryLogStage stage, string message, UnityEngine.Object context=null)
    {
        string text=Format(stage,message);
        if(stage==LocalStoryLogStage.Failed) Debug.LogWarning(text,context);
        else Debug.Log(text,context);
    }
}

/// <summary>Dialogue-end background work, applied only to the next run. Never edits Resources.</summary>
public sealed class LocalStoryRuntimeService : MonoBehaviour
{
    private static LocalStoryRuntimeService instance;
    private CancellationTokenSource cancellation;
    public static bool IsBusy => instance != null && instance.cancellation != null;
    public static bool BlocksCurrentRun => LocalStoryDialogueQueue.BlocksRun(SaveIOService.Instance,
        SaveIOService.Instance.LoadOrCreateProfile().currentRun);
    public static string Status { get; private set; } = "";
    public static void TryResumePending()
    {
        if (IsBusy || !Application.isPlaying) return;
        var storage = SaveIOService.Instance;
        if(LocalStoryDialogueQueue.Next(storage)!=null)
        {
            EnsureInstance();
            instance.Pump(storage);
            return;
        }
        var request = storage.LoadLatestLocalStoryRevision();
        // Old completed-run requests are legacy only; dialogue queues own all new requests.
        if(request?.generationTrigger=="DialogueEnded") return;
        if (request == null || (request.status != "PendingLocalModel" && request.status != "Generating")) return;
        if (!LocalStoryRuntimePipeline.CanProcess(request,storage)) return;
        EnsureInstance();
        instance.Begin(request,storage);
    }
    private static void EnsureInstance()
    {
        if(instance!=null) return;
        var go=new GameObject("LocalStoryRuntime");
        instance=go.AddComponent<LocalStoryRuntimeService>();
        DontDestroyOnLoad(go);
    }
    private async void Pump(SaveIOService storage)
    {
        cancellation=new CancellationTokenSource();
        var token=cancellation.Token;
        try
        {
            LocalStoryDialogueJob job;
            while(!token.IsCancellationRequested && (job=LocalStoryDialogueQueue.Next(storage))!=null)
            {
                Status=job.sourceRun+"회차 "+job.characterId+" → "+job.targetRun+"회차용";
                await LocalStoryDialogueQueue.Process(storage,job,LocalStoryRuntimePipeline.Infer,token);
            }
        }
        catch(OperationCanceledException) { LocalStoryLog.Write(LocalStoryLogStage.Deferred,"생성 작업 중단. 저장된 요청은 다음 실행에서 재개합니다."); }
        catch(Exception e) { LocalStoryLog.Write(LocalStoryLogStage.Failed,"요청 큐 처리: "+e.Message); }
        finally
        {
            Status="대화 종료 생성 큐 대기";
            cancellation.Dispose(); cancellation=null;
            LocalStoryNotebook.Refresh(storage);
        }
    }
    private async void Begin(LocalStoryRevisionRequest request, SaveIOService storage)
    {
        cancellation = new CancellationTokenSource();
        Status = request.sourceRun+" → "+request.targetRun+"회차 이야기 생성 중";
        LocalStoryLog.Write(LocalStoryLogStage.Working,Status+". 실제 플레이 기록 / 내부 모델 / 다음 회차만 적용.");
        try { await LocalStoryRuntimePipeline.Execute(request,storage,LocalStoryRuntimePipeline.Infer,cancellation.Token); }
        catch(Exception e) { LocalStoryLog.Write(LocalStoryLogStage.Failed,"생성 중단: "+e.Message); }
        finally
        {
            var finished = storage.LoadRunData<LocalStoryRevisionRequest>(request.sourceRun,"LocalStoryRevisionRequest") ?? request;
            Status = "이야기 생성: "+finished.status+(string.IsNullOrEmpty(finished.generationError)?"":" / "+finished.generationError);
            cancellation.Dispose(); cancellation=null;
            var stage=finished.status=="Failed"?LocalStoryLogStage.Failed:
                finished.status=="Ready"?LocalStoryLogStage.Complete:LocalStoryLogStage.Deferred;
            LocalStoryLog.Write(stage,request.sourceRun+"회차 기록 → "+request.targetRun+"회차용 / "+Status);
        }
    }
    private void OnDestroy() { cancellation?.Cancel(); if(instance==this) instance=null; }
    private void OnApplicationQuit() => cancellation?.Cancel();
}

public static class LocalStoryRuntimePipeline
{
    // No profile creation, fake completion, or synthetic-memory promotion is permitted here.
    public static bool CanProcess(LocalStoryRevisionRequest request, SaveIOService storage)
    {
        if(request == null || request.sourceRun<1 || request.targetRun!=request.sourceRun+1) return false;
        string profile=Path.Combine(storage.StorageRoot,"Profile.json");
        return File.Exists(profile) && JsonUtility.FromJson<ProfileData>(File.ReadAllText(profile)).currentRun==request.targetRun &&
            storage.LoadRunData<GameProgress>(request.sourceRun,"Progress")?.isCompleted==true;
    }
    public static async Task Execute(LocalStoryRevisionRequest request, SaveIOService storage,
        Func<LocalStoryTestReport,CancellationToken,Task> generate, CancellationToken token)
    {
        if(!CanProcess(request,storage)) throw new InvalidOperationException("완료된 실제 이전 회차와 다음 회차 프로필이 필요합니다.");
        // Freeze what was actually saved at completion, not an ongoing mutable DecisionManager list.
        request=JsonUtility.FromJson<LocalStoryRevisionRequest>(JsonUtility.ToJson(request));
        request.status="Generating"; request.generationError=null;
        storage.SaveRunCheckpoint(request.sourceRun,"LocalStoryRevisionRequest",request);
        try
        {
            if(request.earnedUnlocks==null || request.earnedUnlocks.Length==0)
                request.earnedUnlocks=LocalStoryNotebook.EarnedUnlocks(storage,request.sourceRun);
            if(request.npcContexts==null || request.npcContexts.Length==0)
                request.npcContexts=LocalStoryNotebook.Contexts(storage,request.earnedUnlocks);
            request.generationResults ??= new List<LocalStoryRuntimeResult>();
            // First live pilot: the existing Guide introduction. Other NPCs are journalled, not silently enabled.
            string path=LocalStoryTestInputs.GuidePath;
            var npc=request.npcContexts.FirstOrDefault(c => c.characterId=="Guide" && c.autoGenerate);
            bool encountered=(request.events??new List<StoryEventRecord>()).Any(e => e.characterId=="Guide") ||
                (request.playedCards??new List<PlayedEncounterCardRecord>()).Any(c => c.encounterPath==path);
            if(npc != null && encountered && (request.scenarioPaths??Array.Empty<string>()).Any(p => path.StartsWith(p+"/",StringComparison.Ordinal)))
            {
                // Resume only unfinished work. Manual applications/disabled generated files are never overwritten.
                string directory=Path.Combine(storage.StorageRoot,"GeneratedContent","For_Run_"+request.targetRun.ToString("D4"),"Encounters",path.Replace('/',Path.DirectorySeparatorChar));
                if(!request.generationResults.Any(r => r.storyPath==path && r.status=="Ready") &&
                    !File.Exists(Path.Combine(directory,"Story.json")) && !File.Exists(Path.Combine(directory,"Story.disabled.json")))
                    await Produce(request,npc,path,storage,generate,token);
                else if(!request.generationResults.Any(r => r.storyPath==path))
                    request.generationResults.Add(new LocalStoryRuntimeResult {storyPath=path,model=npc.model,status="Preserved",error="기존 적용/disabled 파일 보존"});
            }
            request.status=request.generationResults.Any(r => r.status=="Failed")?"Failed":npc==null || !encountered?"Skipped":"Ready";
            request.generationError=string.Join(" / ",request.generationResults.Where(r => r.status=="Failed").Select(r => r.error));
        }
        catch(OperationCanceledException) { request.status="PendingLocalModel"; request.generationError="중단됨 — 다음 실행에서 재개"; }
        catch(Exception e) { request.status="Failed"; request.generationError=e.Message; }
        finally
        {
            storage.SaveRunCheckpoint(request.sourceRun,"LocalStoryRevisionRequest",request);
            LocalStoryNotebook.Refresh(storage);
        }
    }
    public static Task<LocalStoryRuntimeResult> ProduceDialogue(LocalStoryRevisionRequest request,LocalStoryNpcContext npc,
        string path,SaveIOService storage,Func<LocalStoryTestReport,CancellationToken,Task> generate,
        CancellationToken token,Func<bool> canCommit) => Produce(request,npc,path,storage,generate,token,canCommit,false);
    private static async Task<LocalStoryRuntimeResult> Produce(LocalStoryRevisionRequest request, LocalStoryNpcContext npc, string path, SaveIOService storage,
        Func<LocalStoryTestReport,CancellationToken,Task> generate, CancellationToken token,Func<bool> canCommit=null,bool checkpoint=true)
    {
        var report=new LocalStoryTestReport {storyPath=path,sourceRun=request.sourceRun,targetRun=request.targetRun,
            interactionMemory=request,interactionSource="실제 완료 회차의 선택·만남·해금 기록",testScenario="ActualGameplay / 회차-"+request.sourceRun+"-다음-"+request.targetRun,
            createdUtc=DateTime.UtcNow.ToString("o"),seed=12345+request.sourceRun,writingPolicy=LocalStoryRewrite.WritingPolicy};
        var result=new LocalStoryRuntimeResult {storyPath=path,model=npc.model,status="Failed"};
        var watch=Stopwatch.StartNew();
        canCommit ??= ()=>CanProcess(request,storage);
        if(request.generationTrigger=="DialogueEnded")
        {
            report.interactionSource="실제 대화 종료 기록(감사 자료) / 조건으로 선별된 꿈 인상만 모델에 전달";
            report.testScenario="ActualGameplay / DialogueEnded / 개정-"+request.dialogueRevision;
        }
        string logTarget=request.sourceRun+"회차 기록 → "+request.targetRun+"회차용 / "+npc.name+" / "+npc.model+" / "+path;
        LocalStoryLog.Write(LocalStoryLogStage.Working,logTarget+" / 모델 로딩·초안 작성 시작");
        try
        {
            if(!Enum.TryParse(npc.model,out LocalStoryTestModel model)) throw new InvalidOperationException("알 수 없는 모델");
            report.model=LocalStoryTestModels.Name(model);
            var asset=Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story");
            report.original=JsonUtility.FromJson<EncounterCardRoot>(asset.text);
            report.examplePrompt=RuntimeExamples(path,storage,out report.exampleSummary,out report.exampleSources);
            string messages=AttachContext(LocalStoryRewrite.Messages(path,report.original,request),LocalStoryNotebook.PromptContext(request,npc.characterId),request.memoryMode=="Dream");
            report.prompt=LocalStoryTestModels.Messages(model,LocalStoryTestArchive.AttachWritingExamples(messages,report.examplePrompt));
            report.generationGrammar=LocalStoryRewrite.Grammar(report.original);
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromMinutes(5));
            await generate(report,budget.Token);
            token.ThrowIfCancellationRequested();
            var built=report.correctionAttempted ? (report.correctionValid?report.corrected:null) : (report.valid?report.generated:null);
            if(built==null) throw new InvalidDataException(report.correctionError??report.error??"적용 검사 실패");
            // Wording is new; core meaning/actions stay attached to cloned canonical source cards.
            report.runtimeStage=report.correctionValid?"correction":"draft";
            report.verbatimReferenceLines=ReferenceCopies(report.examplePrompt,built);
            if(!canCommit())
            {
                result.status="Preserved";
                throw new InvalidOperationException("새 요청/회차 변경/수동 수정으로 이전 생성본 적용을 보류합니다.");
            }
            report.seconds=watch.Elapsed.TotalSeconds;
            result.reportPath=storage.SaveLocalStoryTest(report);
            if(result.reportPath==null) throw new IOException("원출력/입력 기록을 저장하지 못해 게임 적용을 보류합니다.");
            if(!storage.SaveGeneratedContent(request.targetRun,"Encounters",path+"/Story",built)) throw new IOException("다음 회차 저장 실패");
            report.saved=true; result.status="Ready";
            storage.UpdateGeneratedEpisodeStatus(request.sourceRun,request.targetRun,path,ContentGenerationStatus.Ready);
        }
        catch(OperationCanceledException) { report.runtimeError=token.IsCancellationRequested?"생성 취소":"네이티브 취소 또는 5분 시간 제한"; result.error=report.runtimeError; }
        catch(Exception e) { report.runtimeError=e.Message; report.runtimeFailureDetails=e.ToString(); result.error=e.Message; }
        finally
        {
            report.seconds=watch.Elapsed.TotalSeconds;
            if(result.reportPath==null) result.reportPath=storage.SaveLocalStoryTest(report);
            else LocalStoryTestArchive.RecordRuntimeApplication(Path.Combine(storage.StorageRoot,"LocalStoryTests"),result.reportPath,report);
            request.generationResults.RemoveAll(r => r.storyPath==path);
            request.generationResults.Add(result);
            if(result.status!="Ready" && canCommit()) storage.UpdateGeneratedEpisodeStatus(request.sourceRun,request.targetRun,path,ContentGenerationStatus.Failed,result.error);
            if(checkpoint) storage.SaveRunCheckpoint(request.sourceRun,"LocalStoryRevisionRequest",request);
            LocalStoryLog.Write(result.status=="Ready"?LocalStoryLogStage.Complete:
                token.IsCancellationRequested || result.status=="Preserved"?LocalStoryLogStage.Deferred:LocalStoryLogStage.Failed,
                logTarget+" / "+report.seconds.ToString("F1")+"초 / "+
                (result.status=="Ready"?"다음 회차 저장 완료":result.error??"완료하지 못함")+
                " / 결과: "+(result.reportPath??"저장되지 않음"));
        }
        return result;
    }
    private static string AttachContext(string messages,string context,bool dream=false)
    {
        var parsed=JArray.Parse(messages);
        parsed.Last["content"]=((string)parsed.Last["content"]).Replace("현재 능력치(체력/민첩/지식/매력):",
            dream?"생성 요청 시점 능력치(체력/민첩/지식/매력):":"지난 회차 종료 능력치(체력/민첩/지식/매력):")+
            context+"\n기록된 능력치는 과거 자료이며 다음 회차의 현재 수치로 단정하지 않는다.";
        return parsed.ToString(Newtonsoft.Json.Formatting.None);
    }
    // Keep the established human reference, but don't hand the model a complete nine-slot answer to copy.
    public static string RuntimeExamples(string path,SaveIOService storage,out string summary,out string[] sources)
    {
        string captured=LocalStoryTestArchive.CaptureWritingExamples(path,out summary,out sources,Path.Combine(storage.StorageRoot,"LocalStoryTests"));
        int start=captured.LastIndexOf("{\"cards\":",StringComparison.Ordinal);
        if(start<0) return captured;
        var cards=(JArray)JObject.Parse(captured.Substring(start))["cards"];
        var examples=new JArray(cards.Where(c => ((int)c["index"])!=4).Take(3).Select(c => new JObject {["text"]=c["text"].DeepClone()}));
        summary="저장된 Edit 말투 참고 "+examples.Count+"문장 / 완성 답안으로 대체하지 않음";
        return "사람이 수정한 직접 대사의 말투/맞춤법 참고다. 과거 사실이나 이번 대사 정답이 아니다. 그대로 복사하지 말고 현재 기억과 핵심 사건에 맞게 새로 쓴다.\n"+examples.ToString(Newtonsoft.Json.Formatting.Indented);
    }
    private static int ReferenceCopies(string examples,EncounterCardRoot built)
    {
        int start=(examples??"").IndexOf('['); if(start<0) return 0;
        try { var texts=JArray.Parse(examples.Substring(start)).Select(e => (string)e["text"]).Where(t => t!=null).ToArray();
            return built.MainStory.Count(c => texts.Contains(c.text)); } catch { return 0; }
    }
    public static async Task Infer(LocalStoryTestReport report,CancellationToken token)
    {
        string root=Path.Combine(Application.streamingAssetsPath,"LocalStory");
        var model=report.model.Contains("Gemma")?LocalStoryTestModel.Gemma:LocalStoryTestModel.Qwen;
        string modelPath=LocalStoryTestModels.PathFor(root,model);
        report.modelPath=Path.GetFullPath(modelPath);
        report.runtimePath=Path.GetFullPath(Path.Combine(root,"runtimes","win-x64","native"));
        string partial=""; var watch=Stopwatch.StartNew();
        var sampling=LocalStorySampling.Comparison(); report.samplingProfile=sampling.Name; report.draftParameters=sampling.Parameters(report.seed);
        try
        {
            var draft=await LocalStoryInference.Generate(root,report.prompt,report.generationGrammar,report.seed,token,s => partial=s,sampling,modelPath);
            report.rawOutput=draft.json; report.backend=draft.backend; report.fallback=draft.fallback;
            LocalStoryRewrite.EvaluateDraft(report,report.original,report.interactionMemory);
        }
        finally { report.draftSeconds=watch.Elapsed.TotalSeconds; report.rawOutput??=partial; }
        report.correctionAttempted=true; partial=""; watch.Restart();
        LocalStoryLog.Write(LocalStoryLogStage.Working,report.targetRun+"회차용 / "+report.model+" / "+report.storyPath+" / 한국어 교정 시작");
        report.correctionPrompt=LocalStoryTestModels.Messages(model,LocalStoryTestArchive.AttachWritingExamples(
            AttachContext(LocalStoryRewrite.CorrectionMessages(report.storyPath,report.original,report.interactionMemory,report.rawOutput),
                LocalStoryNotebook.PromptContext(report.interactionMemory,report.interactionMemory.characterId??"Guide"),
                report.interactionMemory.memoryMode=="Dream"),report.examplePrompt));
        var proofreading=LocalStorySampling.Proofreading(); report.correctionParameters=proofreading.Parameters(report.seed);
        try
        {
            var correction=await LocalStoryInference.Generate(root,report.correctionPrompt,report.generationGrammar,report.seed,token,s => partial=s,proofreading,modelPath);
            report.correctionRawOutput=correction.json; report.correctionBackend=correction.backend; report.correctionFallback=correction.fallback;
            report.correctionValid=LocalStoryRewrite.TryBuild(correction.json,report.original,report.interactionMemory,out report.corrected,out report.correctionError);
        }
        catch(Exception e) { report.correctionError=e.Message; if(e is OperationCanceledException) throw; }
        finally { report.correctionSeconds=watch.Elapsed.TotalSeconds; report.correctionRawOutput??=partial; }
    }
}
