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
    public static string Source(LocalStoryTestReport report)
    {
        string model=report?.model;
        if(string.IsNullOrWhiteSpace(model)) return "[생성 방식 미확인]";
        return (model.StartsWith("OmniRoute",StringComparison.OrdinalIgnoreCase)?"[외부 통신] ":"[내장형] ")+model;
    }
    public static void Generation(LocalStoryLogStage stage,LocalStoryTestReport report,string message)
    {
        Write(stage,Source(report)+" / "+report?.storyPath+" / "+message);
    }
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

    internal static void LogDialogueStatus(SaveIOService storage, int run)
    {
        try
        {
            var previous = LocalStoryDialogueQueue.Load(storage, run - 1)?.jobs ?? new List<LocalStoryDialogueJob>();
            var current = LocalStoryDialogueQueue.Load(storage, run)?.jobs ?? new List<LocalStoryDialogueJob>();
            int waiting = current.Count(job => job.status == "WaitingForRunEnd");
            int pending = previous.Concat(current).Count(job => job.status == "Pending" || job.status == "Generating");
            int ready = previous.Count(job => job.status == "Ready");
            int lines = previous.Where(job => job.status == "Ready").Sum(job =>
                job.emotionLines?.Count(line => !string.IsNullOrEmpty(line.generatedText)) ?? 0);
            int rejected = previous.Count(job => job.status == "Rejected");
            Debug.Log($"[LocalStory][대사 생성 상태] {run}회차 시작 · 이번 회차용 저장 {ready}개 이야기 / {lines}줄" +
                $" · 검사 탈락 {rejected}개 이야기 · 미완료 {pending}개 · 이번 회차 종료 대기 {waiting}개");
        }
        catch (Exception error)
        {
            Debug.LogWarning("[LocalStory][대사 생성 상태] 저장된 상태를 읽지 못했습니다. " + error.Message);
        }
        Debug.Log("[LocalStory][대사 비교 HTML] " + storage.DialogueReportPath);
    }

    private void Update()
    {
        if (!IsBusy) LocalStoryInference.ReleaseIfIdle(TimeSpan.FromSeconds((int)LocalStoryPrompt.Policy["inference"]["idleSeconds"]));
    }
    public static void PrepareConfiguredModel()
    {
        if (IsPreparing || !Application.isPlaying) return;
        try
        {
            string root = Path.Combine(Application.streamingAssetsPath,"LocalStory");
            bool needed = ChapterContentCatalog.Chapters.Any(chapter => ChapterContentCatalog.Characters((string)chapter["id"])
                .Any(npc => (bool?)npc["autoGenerate"] != false));
            if (!needed) { PreparationStatus="Gemma 자동 생성 꺼짐"; LastPreparation=null; return; }
            EnsureInstance();
            instance.Prepare(LocalStoryTestModels.PathFor(root,LocalStoryTestModel.Gemma));
        }
        catch(Exception e) { PreparationStatus="검사 보류"; LocalStoryLog.Write(LocalStoryLogStage.Deferred,"[내장형] Gemma 사전 검사: "+e.Message,activity:"모델 검사"); }
    }
    private async void Prepare(string modelPath)
    {
        preparation = new CancellationTokenSource();
        PreparationStatus="Gemma 파일 검사 중"; LastPreparation=null;
        LocalStoryLog.Write(LocalStoryLogStage.Working,"[내장형] Gemma 사전 검사 시작 / 입력은 계속 가능",activity:"모델 검사");
        try
        {
            LastPreparation=await LocalStoryInference.PrepareGemma(modelPath,preparation.Token);
            PreparationStatus="Gemma 파일 검사 완료";
            LocalStoryLog.Write(LocalStoryLogStage.Complete,"[내장형] Gemma 사전 검사 / "+LastPreparation.seconds.ToString("F2")+
                "초 / "+LastPreparation.provider+" / "+(LastPreparation.cached?"같은 파일 검사 재사용":LastPreparation.bytesHashed+" bytes 전체 검사")+" / 모델 메모리 적재는 생성 시 수행",activity:"모델 검사");
        }
        catch(OperationCanceledException) { PreparationStatus="검사 중단"; }
        catch(Exception e) { PreparationStatus="검사 실패 — 원문으로 플레이 가능"; LocalStoryLog.Write(LocalStoryLogStage.Deferred,"[내장형] Gemma 사전 검사: "+e.Message,activity:"모델 검사"); }
        finally { preparation.Dispose(); preparation=null; }
    }
    public static void TryResumePending()
    {
        if (IsBusy || !Application.isPlaying) return;
        try
        {
            var storage = SaveIOService.Instance;
            LocalStoryDialogueQueue.RecoverFinishedRuns(storage);
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
                await LocalStoryDialogueQueue.Process(storage, job, LocalStoryEmotion.Infer, token);
            }
        }
        catch(OperationCanceledException) { LocalStoryLog.Write(LocalStoryLogStage.Deferred,"작업 중단. 저장된 요청은 다음 실행에서 재개합니다.",activity:"대사 생성"); }
        catch(Exception e) { LocalStoryLog.Write(LocalStoryLogStage.Failed,"요청 큐 처리: "+e.Message,activity:"대사 생성"); }
        finally
        {
            Status="회차 종료 감정 대사 대기";
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
    public static async Task Infer(LocalStoryTestReport report,CancellationToken token)
    {
        string root=Path.Combine(Application.streamingAssetsPath,"LocalStory");
        var model=LocalStoryTestModels.ModelFor(report);
        var omni=model==LocalStoryTestModel.OmniRoute?StoryGenerationSettings.Read():null;
        string modelPath=null;
        report.runtimePath=omni!=null?omni.apiUrl:Path.GetFullPath(Path.Combine(root,"runtimes","win-x64","native"));
        report.generationIdentity=omni!=null?omni.apiUrl+"|"+omni.combo:"Embedded|"+model;
        report.samplingProfile="짧은 구간 작성 / 필요한 구간만 교정";
        LocalStoryLog.Generation(LocalStoryLogStage.Working,report,omni!=null?"이야기 생성 시작 / HTTP 요청":"이야기 생성 시작 / 로컬 모델 추론");
        try
        {
            await LocalStorySections.Generate(report,token,(input,correction,ct,progress) =>
            {
                if(omni!=null)
                {
                    string messages=correction?input.correctionPrompt:input.prompt;
                    var parameters=OmniRouteFreeStackConverter.RequestBody(messages,input.maxTokens,input.seed,omni.combo);
                    parameters.Remove("messages");
                    if(correction) input.correctionParameters=report.correctionParameters=parameters.ToString(Newtonsoft.Json.Formatting.None);
                    else input.draftParameters=report.draftParameters=parameters.ToString(Newtonsoft.Json.Formatting.None);
                    return OmniRouteFreeStackConverter.Generate(messages,input.maxTokens,input.seed,ct,omni);
                }
                if(modelPath==null) {modelPath=LocalStoryTestModels.PathFor(root,model);report.modelPath=Path.GetFullPath(modelPath);}
                return LocalStoryInference.Generate(root,correction?input.correctionPrompt:input.prompt,input.grammar,input.seed,ct,progress,
                    (correction?LocalStorySampling.Proofreading():LocalStorySampling.Comparison()).WithTokenLimit(input.maxTokens),modelPath);
            });
            bool valid=report.correctionAttempted?report.correctionValid:report.valid;
            LocalStoryLog.Generation(valid?LocalStoryLogStage.Complete:report.reviewRequired?LocalStoryLogStage.Review:LocalStoryLogStage.Failed,
                report,"이야기 생성 종료 / "+(valid?"검사 통과":report.reviewError??report.correctionError??report.error??"검사 실패")+
                (string.IsNullOrWhiteSpace(report.backend)?"":" / "+report.backend));
        }
        catch(OperationCanceledException)
        {
            LocalStoryLog.Generation(LocalStoryLogStage.Deferred,report,"생성 취소");
            throw;
        }
    }
}
