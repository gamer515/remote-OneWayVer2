using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Race entire stories, never interleave providers' cards. Only the caller commits the winner.</summary>
public static class StoryGenerationRace
{
    private sealed class Completion<T>
    {
        public T value;
        public Exception error;
        public int order;
    }

    public static async Task<T> FirstApplicable<T>(Func<CancellationToken, Task<T>> first,
        Func<CancellationToken, Task<T>> second, Func<T, bool> applicable, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        int order = 0;
        async Task<Completion<T>> Run(Func<CancellationToken, Task<T>> generate)
        {
            var result = new Completion<T>();
            try { result.value = await generate(cancellation.Token); }
            catch (Exception e) { result.error = e; }
            result.order = Interlocked.Increment(ref order);
            return result;
        }
        var all = new[] { Run(first), Run(second) };
        var pending = all.ToList();
        var errors = new List<Exception>();
        var interrupted = new TaskCompletionSource<bool>();
        using var registration = token.Register(() => interrupted.TrySetResult(true));
        try
        {
            while (pending.Count > 0)
            {
                await Task.WhenAny(pending.Cast<Task>().Append(interrupted.Task));
                token.ThrowIfCancellationRequested();
                var completed = pending.Where(t => t.IsCompleted).OrderBy(t => t.Result.order).First();
                pending.Remove(completed);
                var result = await completed;
                if (result.error != null) { errors.Add(result.error); continue; }
                try { if (applicable(result.value)) return result.value; }
                catch (Exception e) { errors.Add(e); continue; }
                errors.Add(new InvalidOperationException("완성된 적용 가능 이야기가 없어 다른 생성기를 기다립니다."));
            }
            throw new AggregateException("두 생성기 모두 이야기를 완성하지 못했습니다.", errors);
        }
        finally
        {
            cancellation.Cancel();
            // Native shutdown may take longer than HTTP cancellation. Observe it without delaying the winning commit.
            // Do not join a Unity-context continuation in the synchronous assembly-reload guard.
            // Native workers already register themselves with LocalStoryLifetime.
            async Task Drain() { try { await Task.WhenAll(all).ConfigureAwait(false); } finally { cancellation.Dispose(); } }
            _ = Drain();
        }
    }

    public static Task GenerateRuntime(LocalStoryTestReport report, CancellationToken token) =>
        GenerateRuntime(report, token, LocalStoryRuntimePipeline.Infer);

    public static async Task GenerateRuntime(LocalStoryTestReport report, CancellationToken token,
        Func<LocalStoryTestReport, CancellationToken, Task> infer)
    {
        var config = StoryGenerationSettings.Read();
        if (config.mode == "Embedded" || LocalStoryTestModels.ModelFor(report) == LocalStoryTestModel.OmniRoute)
        { await infer(report, token); return; }
        if (config.mode == "OmniRoute")
        { report.model = LocalStoryTestModels.Name(LocalStoryTestModel.OmniRoute); await infer(report, token); return; }

        bool open = true;
        var local = JsonUtility.FromJson<LocalStoryTestReport>(JsonUtility.ToJson(report));
        var remote = JsonUtility.FromJson<LocalStoryTestReport>(JsonUtility.ToJson(report));
        local.sectionCheckpoint = report.sectionCheckpoint;
        remote.sectionCheckpoint = report.omniSectionCheckpoint;
        remote.model = LocalStoryTestModels.Name(LocalStoryTestModel.OmniRoute);
        local.sectionStillCurrent = remote.sectionStillCurrent = () => open && (report.sectionStillCurrent?.Invoke() ?? true);
        local.saveSectionCheckpoint = c => { if (open) report.saveSectionCheckpoint?.Invoke(c); };
        remote.saveSectionCheckpoint = c => { if (open) report.saveOmniSectionCheckpoint?.Invoke(c); };
        local.sectionProgress = (s,c,i,n,p) => { if (open) report.sectionProgress?.Invoke(s,c,i,n,"내장형 / " + p); };
        remote.sectionProgress = (s,c,i,n,p) => { if (open) report.sectionProgress?.Invoke(s,c,i,n,"OmniRoute / " + p); };
        LocalStoryLog.Write(LocalStoryLogStage.Request,report.storyPath+" / 동시 생성 시작 / "+LocalStoryLog.Source(local)+" / "+LocalStoryLog.Source(remote));
        var failures = new List<string>();
        async Task<LocalStoryTestReport> Generate(LocalStoryTestReport candidate, CancellationToken ct)
        {
            try { await infer(candidate, ct); return candidate; }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                string message = candidate.model + ": " + e.Message;
                failures.Add(message);
                if (open) LocalStoryLog.Generation(LocalStoryLogStage.Deferred,candidate,e.Message+" / 다른 생성기 결과 대기");
                throw;
            }
        }
        try
        {
            var winner = await FirstApplicable(
                ct => Generate(local, ct), ct => Generate(remote, ct),
                Applicable, token);
            open = false;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(winner), report);
            // JsonUtility intentionally excludes this diagnostic candidate from serialized archives.
            report.reviewRawOutput = winner.reviewRawOutput;
            report.generationWinner = winner.model;
            report.generationFailures = failures.ToArray();
            LocalStoryLog.Generation(LocalStoryLogStage.Complete,report,"최종 채택 / 먼저 완성된 이야기"+
                (string.IsNullOrWhiteSpace(winner.backend)?"":" / "+winner.backend));
        }
        finally { open = false; }
    }

    public static bool Applicable(LocalStoryTestReport report)
    {
        if (report == null || report.original?.MainStory == null || report.sectionOutputs == null ||
            report.sectionOutputs.Sum(s => s.count) != report.original.MainStory.Count) return false;
        if (report.correctionAttempted ? report.correctionValid && report.corrected != null : report.valid && report.generated != null) return true;
        if (!report.allowFailedOutput) return false;
        try { return LocalStoryGameApply.BuildFailedPreview(report, out _) != null; }
        catch (FormatException) { return false; }
    }
}
