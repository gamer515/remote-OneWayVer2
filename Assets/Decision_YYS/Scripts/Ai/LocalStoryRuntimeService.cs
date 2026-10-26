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

public enum LocalStoryLogStage { Request, Working, Complete, Failed, Deferred, Review }

/// <summary>Colour is presentation only; stage labels remain searchable in plain player logs.</summary>
public static class LocalStoryLog
{
    public static string Format(LocalStoryLogStage stage, string message,string activity="생성")
    {
        string label, colour;
        switch(stage)
        {
            case LocalStoryLogStage.Request: label="생성 요청"; colour="#C99CFF"; break;
            case LocalStoryLogStage.Working: label="생성 중"; colour="#55D9FF"; break;
            case LocalStoryLogStage.Complete: label="생성 완료"; colour="#74E892"; break;
            case LocalStoryLogStage.Failed: label="생성 실패"; colour="#FF7777"; break;
            case LocalStoryLogStage.Review: label="생성 확인 필요"; colour="#FFD166"; break;
            default: label="생성 보류"; colour="#FFD166"; break;
        }
        label=activity+label.Substring(2);
        string text="[LocalStory]["+label+"] "+message;
#if UNITY_EDITOR
        // NPC/model/error text must not inject rich-text tags that hide or recolour the stage.
        return "<color="+colour+"><b>"+text.Replace("<","＜").Replace(">","＞")+"</b></color>";
#else
        return text;
#endif
    }
    public static void Write(LocalStoryLogStage stage, string message, UnityEngine.Object context=null,string activity="생성")
    {
        string text=Format(stage,message,activity);
        if(stage==LocalStoryLogStage.Failed||stage==LocalStoryLogStage.Review) Debug.LogWarning(text,context);
        else Debug.Log(text,context);
    }
}

/// <summary>Dialogue-end background work, applied only to the next run. Never edits Resources.</summary>
public sealed class LocalStoryRuntimeService : MonoBehaviour
{
    private static LocalStoryRuntimeService instance;
    private CancellationTokenSource cancellation;
    private CancellationTokenSource preparation;
    public static bool IsBusy => instance != null && instance.cancellation != null;
    public static bool IsPreparing => instance != null && instance.preparation != null;
    public static string PreparationStatus { get; private set; } = "미검사";
    public static LocalStoryModelVerification LastPreparation { get; private set; }
    public static string Status { get; private set; } = "";
    private void Update()
    {
        if (!IsBusy) LocalStoryInference.ReleaseIfIdle(TimeSpan.FromSeconds((int)LocalStoryPrompt.Policy["inference"]["idleSeconds"]));
    }
    public static void PrepareConfiguredModel()
    {
        if (IsPreparing || !Application.isPlaying) return;
        try
        {
            var storage = SaveIOService.Instance;
            string root = Path.Combine(Application.streamingAssetsPath,"LocalStory");
            bool needed = LocalStoryNotebook.ConfiguredContexts(storage,Array.Empty<string>(),false).Any(n=>n.autoGenerate && n.model=="Gemma");
            if (!needed) { PreparationStatus="Gemma 자동 생성 꺼짐"; LastPreparation=null; return; }
            EnsureInstance();
            instance.Prepare(LocalStoryTestModels.PathFor(root,LocalStoryTestModel.Gemma));
        }
        catch(Exception e) { PreparationStatus="검사 보류"; LocalStoryLog.Write(LocalStoryLogStage.Deferred,"Gemma 사전 검사: "+e.Message,activity:"모델 검사"); }
    }
    private async void Prepare(string modelPath)
    {
        preparation = new CancellationTokenSource();
        PreparationStatus="Gemma 파일 검사 중"; LastPreparation=null;
        LocalStoryLog.Write(LocalStoryLogStage.Working,"Gemma 사전 검사 시작 / 입력은 계속 가능",activity:"모델 검사");
        try
        {
            LastPreparation=await LocalStoryInference.PrepareGemma(modelPath,preparation.Token);
            PreparationStatus="Gemma 파일 검사 완료";
            LocalStoryLog.Write(LocalStoryLogStage.Complete,"Gemma 사전 검사 / "+LastPreparation.seconds.ToString("F2")+
                "초 / "+LastPreparation.provider+" / "+(LastPreparation.cached?"같은 파일 검사 재사용":LastPreparation.bytesHashed+" bytes 전체 검사")+" / 모델 메모리 적재는 생성 시 수행",activity:"모델 검사");
        }
        catch(OperationCanceledException) { PreparationStatus="검사 중단"; }
        catch(Exception e) { PreparationStatus="검사 실패 — 원문으로 플레이 가능"; LocalStoryLog.Write(LocalStoryLogStage.Deferred,"Gemma 사전 검사: "+e.Message,activity:"모델 검사"); }
        finally { preparation.Dispose(); preparation=null; }
    }
    public static void TryResumePending()
    {
        if (IsBusy || !Application.isPlaying) return;
        try
        {
            var storage = SaveIOService.Instance;
            if(LocalStoryDialogueQueue.Next(storage)==null) return;
            EnsureInstance();
            instance.Pump(storage);
        }
        catch(Exception e) { LocalStoryLog.Write(LocalStoryLogStage.Deferred,"저장된 생성 요청 재개 보류: "+e.Message); }
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
    private void Cancel() { cancellation?.Cancel(); preparation?.Cancel(); }
    private void OnDestroy() { Cancel(); if(instance==this) instance=null; }
    private void OnApplicationQuit() => Cancel();
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
            bool encountered=false;
            foreach(var npc in request.npcContexts.Where(c=>c.autoGenerate))
            {
                var played=(request.playedCards??new List<PlayedEncounterCardRecord>()).Select(c=>c.encounterPath)
                    .Concat((request.events??new List<StoryEventRecord>()).Where(e=>e.characterId==npc.characterId).Select(e=>e.storyPath)).Where(p=>!string.IsNullOrEmpty(p)).ToHashSet();
                var paths=ChapterContentCatalog.Stories.Where(s=>(string)s["characterId"]==npc.characterId &&
                    (request.scenarioPaths??Array.Empty<string>()).Any(p=>((string)s["path"]).StartsWith(p+"/",StringComparison.Ordinal)) &&
                    (played.Contains((string)s["path"]) || (played.Count==0 && !((string)s["key"]).Contains('/') &&
                        (request.events??new List<StoryEventRecord>()).Any(e=>e.characterId==npc.characterId)))).Select(s=>(string)s["path"]).ToArray();
                if(played.Count==0 && paths.Length>1)paths=Array.Empty<string>();
                foreach(string path in paths)
                {
                    encountered=true;
                    if(!request.generationResults.Any(r=>r.storyPath==path&&r.status=="Ready")&&!storage.HasNarrativeContent(request.targetRun,path))
                        await Produce(request,npc,path,storage,generate,token);
                    else if(!request.generationResults.Any(r=>r.storyPath==path))
                        request.generationResults.Add(new LocalStoryRuntimeResult {storyPath=path,model=npc.model,status="Preserved",error="기존 적용/비활성 생성본 보존"});
                }
            }
            request.status=request.generationResults.Any(r => r.status=="Failed")?"Failed":!encountered?"Skipped":
                request.generationResults.Any(r=>r.status=="ReviewRequired")?"ReviewRequired":"Ready";
            request.generationError=string.Join(" / ",request.generationResults.Where(r => r.status=="Failed"||r.status=="ReviewRequired").Select(r => r.error));
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
            createdUtc=DateTime.UtcNow.ToString("o"),seed=12345+request.sourceRun,writingPolicy=LocalStoryRewrite.WritingPolicy,allowFailedOutput=LocalStoryPrompt.AllowFailedOutput};
        var result=new LocalStoryRuntimeResult {storyPath=path,model=npc.model,status="Failed"};
        var watch=Stopwatch.StartNew();
        canCommit ??= ()=>CanProcess(request,storage);
        if(request.generationTrigger=="DialogueEnded")
        {
            report.interactionSource="실제 대화 종료 기록(감사 자료) / 조건으로 선별된 꿈 인상만 모델에 전달";
            report.testScenario="ActualGameplay / DialogueEnded / "+request.sourceRun+"→"+request.targetRun;
        }
        string logTarget=request.sourceRun+"회차 기록 → "+request.targetRun+"회차용 / "+npc.name+" / "+npc.model+" / "+path;
        LocalStoryLog.Write(LocalStoryLogStage.Working,logTarget+" / 모델 로딩·초안 작성 시작");
        try
        {
            if(!Enum.TryParse(npc.model,out LocalStoryTestModel model)) throw new InvalidOperationException("알 수 없는 모델");
            report.model=LocalStoryTestModels.Name(model);
            string source=NarrativeInteractions.StorySource(path)??throw new InvalidDataException("원문 이야기가 없습니다: "+path);
            report.original=JsonUtility.FromJson<EncounterCardRoot>(source);
            report.examplePrompt=RuntimeExamples(path,storage,out report.exampleSummary,out report.exampleSources,report.seed);
            if(request.generationTrigger=="DialogueEnded")
            {
                report.sectionCheckpoint=LocalStoryDialogueQueue.Load(storage,request.sourceRun)?.jobs
                    .FirstOrDefault(j=>j.storyPath==path && j.revision==request.dialogueRevision)?.sectionCheckpoint;
                report.sectionStillCurrent=canCommit;
                report.sectionProgress=(start,count,index,total,phase)=>
                {
                    if(!canCommit()) return;
                    var queue=LocalStoryDialogueQueue.Load(storage,request.sourceRun);
                    var current=queue?.jobs.FirstOrDefault(j=>j.storyPath==path && j.revision==request.dialogueRevision);
                    if(current==null) return;
                    string events=string.Join(", ",report.original.MainStory.Skip(start).Take(count).Select(c=>c.eventIdStable));
                    var guide=LocalStorySections.WritingGuide(path);
                    string parts=string.Join(" / ",report.original.MainStory.Skip(start).Take(count).Select(c=>
                        guide?.frames?.FirstOrDefault(f=>f.eventId==c.eventIdStable)?.purpose ?? c.coreMeaning ?? c.text));
                    current.generationPart=index+"/"+total+" 구간 / 카드 "+(start+1)+"~"+(start+count)+" / "+phase+" / "+parts+" ("+events+")";
                    LocalStoryDialogueQueue.Save(storage,queue);
                };
                report.saveSectionCheckpoint=checkpointState=>
                {
                    if(!canCommit()) return;
                    var queue=LocalStoryDialogueQueue.Load(storage,request.sourceRun);
                    var current=queue?.jobs.FirstOrDefault(j=>j.storyPath==path && j.revision==request.dialogueRevision);
                    if(current==null) return;
                    current.sectionCheckpoint=checkpointState;
                    LocalStoryDialogueQueue.Save(storage,queue);
                };
            }
            string messages=LocalStoryRewrite.Messages(path,report.original,request);
            report.prompt=LocalStoryTestModels.Messages(model,LocalStoryTestArchive.AttachWritingExamples(messages,report.examplePrompt));
            report.generationGrammar=LocalStoryRewrite.Grammar(report.original);
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromMinutes(5));
            await generate(report,budget.Token);
            token.ThrowIfCancellationRequested();
            var built=report.correctionAttempted ? (report.correctionValid?report.corrected:null) : (report.valid?report.generated:null);
            if(built==null && report.allowFailedOutput)
            {
                try{built=LocalStoryGameApply.BuildFailedPreview(report,out report.reviewError);report.reviewRequired=true;}
                catch(FormatException){} // Broken structure stays in the report rather than replacing game actions.
            }
            if(built==null) throw new InvalidDataException(report.correctionError??report.error??"적용 검사 실패");
            // Wording is new; core meaning/actions stay attached to cloned canonical source cards.
            report.runtimeStage=report.reviewRequired?"failed-output":report.correctionValid?"correction":"draft";
            report.verbatimReferenceLines=ReferenceCopies(report.examplePrompt,built);
            if(!canCommit())
            {
                result.status="Preserved";
                throw new InvalidOperationException("새 요청/회차 변경/수동 수정으로 이전 생성본 적용을 보류합니다.");
            }
            report.seconds=watch.Elapsed.TotalSeconds;
            result.reportPath=storage.SaveLocalStoryTest(report);
            if(LocalStoryPrompt.SaveDiagnosticReports && result.reportPath==null) throw new IOException("원출력/입력 기록을 저장하지 못해 게임 적용을 보류합니다.");
            bool saved=report.reviewRequired?storage.SaveGeneratedContentForReview(request.targetRun,"Encounters",path+"/Story",built,report.reviewError):
                storage.SaveGeneratedContent(request.targetRun,"Encounters",path+"/Story",built);
            if(!saved) throw new IOException("다음 회차 저장 실패");
            report.saved=true; result.status=report.reviewRequired?"ReviewRequired":"Ready";result.error=report.reviewError;
            storage.UpdateGeneratedEpisodeStatus(request.sourceRun,request.targetRun,path,report.reviewRequired?ContentGenerationStatus.ReviewRequired:ContentGenerationStatus.Ready,result.error);
        }
        catch(OperationCanceledException) { report.runtimeError=token.IsCancellationRequested?"생성 취소":"네이티브 취소 또는 5분 시간 제한"; result.error=report.runtimeError; }
        catch(Exception e) { report.runtimeError=e.Message; report.runtimeFailureDetails=e.ToString(); result.error=e.Message; }
        finally
        {
            report.seconds=watch.Elapsed.TotalSeconds;
            if(LocalStoryPrompt.SaveDiagnosticReports)
            {
                if(result.reportPath==null) result.reportPath=storage.SaveLocalStoryTest(report);
                else LocalStoryTestArchive.RecordRuntimeApplication(Path.Combine(storage.StorageRoot,"LocalStoryTests"),result.reportPath,report);
            }
            request.generationResults.RemoveAll(r => r.storyPath==path);
            request.generationResults.Add(result);
            if(result.status!="Ready" && result.status!="ReviewRequired" && canCommit()) storage.UpdateGeneratedEpisodeStatus(request.sourceRun,request.targetRun,path,ContentGenerationStatus.Failed,result.error);
            if(checkpoint) storage.SaveRunCheckpoint(request.sourceRun,"LocalStoryRevisionRequest",request);
            LocalStoryLog.Write(result.status=="ReviewRequired"?LocalStoryLogStage.Review:result.status=="Ready"?LocalStoryLogStage.Complete:
                token.IsCancellationRequested || result.status=="Preserved"?LocalStoryLogStage.Deferred:LocalStoryLogStage.Failed,
                logTarget+" / "+report.seconds.ToString("F1")+"초 / "+
                (result.status=="Ready"?"다음 회차 저장 완료":result.status=="ReviewRequired"?"검사 실패본 저장·다음 회차 반영 / "+result.error:result.error??"완료하지 못함")+
                (LocalStoryPrompt.SaveDiagnosticReports?" / 결과: "+(result.reportPath??"저장되지 않음"):""));
        }
        return result;
    }
    // Runtime can use a partially edited scene, after validating the whole file's source/speaker/action bindings.
    public static string RuntimeExamples(string path,SaveIOService storage,out string summary,out string[] sources,int? seed=null)
    {
        if((bool?)LocalStoryPrompt.Policy["composition"]["useEditedExamples"]!=true)
        {summary="JSON 모듈 예시 사용";sources=Array.Empty<string>();return "";}
        return LocalStoryTestArchive.CaptureRuntimeWritingExamples(path,out summary,out sources,
            Path.Combine(storage.StorageRoot,"LocalStoryTests"),seed);
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
        string modelPath=null;
        report.runtimePath=Path.GetFullPath(Path.Combine(root,"runtimes","win-x64","native"));
        report.samplingProfile="짧은 구간 작성 / 필요한 구간만 교정";
        await LocalStorySections.Generate(report,token,(input,correction,ct,progress) =>
        {
            if(modelPath==null) {modelPath=LocalStoryTestModels.PathFor(root,model);report.modelPath=Path.GetFullPath(modelPath);}
            return LocalStoryInference.Generate(root,correction?input.correctionPrompt:input.prompt,input.grammar,input.seed,ct,progress,
                (correction?LocalStorySampling.Proofreading():LocalStorySampling.Comparison()).WithTokenLimit(input.maxTokens),modelPath);
        });
    }
}
