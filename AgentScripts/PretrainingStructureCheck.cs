using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OneWay.Decision.DirectLlm;
using UnityEngine;

// Returns evidence to the caller only. No reports or player save writes.
public static class PretrainingStructureCheck
{
    public static Task<object> PlaybackWithoutReports() => RunDialoguePlayback(false);

    public static Task<object> PlaybackWithGemmaFallback() => RunDialoguePlayback(true);

    private static async Task<object> RunDialoguePlayback(bool failAllApisForChoice)
    {
        if (Application.isPlaying || LocalStoryRuntimeService.IsBusy) throw new InvalidOperationException("Stop gameplay first.");
        const string path = "Chapter_1/Chapter_1_01/Encounters/brook";
        string root = Path.GetFullPath("tmp/DirectDialogueChecks/" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks++;
        }

        var source = JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Story(path));
        string longReply = "처음 겪는 일입니다." + Environment.NewLine + new string('가', 190);
        const string taxParaphrase = "안을 살펴보고 싶다면 나한테 먼저 건네.";
        const string numericReply = "거기엔 99명이 숨어 있어.";

        var store = (SaveIOService)Activator.CreateInstance(typeof(SaveIOService),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { root }, null);
        var instance = typeof(SaveIOService).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Single(field => field.FieldType == typeof(SaveIOService));
        object previous = instance.GetValue(null);
        var dialogueLogs = new System.Collections.Generic.List<string>();
        void CaptureDialogueLog(string message, string stack, LogType type)
        {
            if (message.Contains("[LocalStory][대사")) dialogueLogs.Add(message);
        }
        using var stop = new CancellationTokenSource();
        Application.logMessageReceived += CaptureDialogueLog;
        try
        {
            instance.SetValue(null, store);
            store.SaveProfile(new ProfileData { currentRun = 1 });
            var progress = new GameProgress { guideProgress = new GuideProgress() };
            progress.unlockedIds.Add("event:brook");
            int choice = source.MainStory.FindIndex(card => card.options?.Length > 0);
            foreach (int index in new[] { 0, 1, choice })
            {
                var card = source.MainStory[index];
                progress.guideProgress.runHistory.Add(new PlayedEncounterCardRecord
                {
                    placeId = "brook", encounterPath = path, cardIndex = index,
                    card = new Dialogue { text = card.text, speakerId = card.speakerId, eventIdStable = card.eventIdStable }
                });
            }
            store.SaveRunCheckpoint(1, "Progress", progress);
            LocalStoryDialogueQueue.EncounterEnded(store, 1, "brook");
            var waiting = LocalStoryDialogueQueue.Load(store, 1).jobs.Single();
            Check(waiting.status == "WaitingForRunEnd" && waiting.emotionLines.Count == 3, "Displayed NPC cards were not recorded.");
            Check(string.IsNullOrEmpty(waiting.emotion) && LocalStoryDialogueQueue.Next(store) == null, "Emotion assigned before run end.");

            progress.isCompleted = true;
            store.SaveRunCheckpoint(1, "Progress", progress);
            LocalStoryDialogueQueue.FinalizeRun(store, 1);
            var pending = LocalStoryDialogueQueue.Next(store);
            string emotion = pending.emotion;
            Check(!string.IsNullOrWhiteSpace(emotion), "No saved emotion was selected.");
            LocalStoryDialogueQueue.FinalizeRun(store, 1);
            Check(LocalStoryDialogueQueue.Next(store).emotion == emotion, "Finalization rerolled emotion.");

            int calls = 0;
            var failedProviders = new System.Collections.Generic.List<DirectLlmProvider>();
            Task<DirectLlmHttpResponse> Reply(DirectLlmHttpRequest request, CancellationToken token)
            {
                if (++calls == 2) stop.Cancel();
                token.ThrowIfCancellationRequested();
                var body = JObject.Parse(request.Body);
                string input = request.Provider == DirectLlmProvider.Gemini
                    ? (string)body["contents"][0]["parts"][0]["text"]
                    : (string)body["messages"][0]["content"];
                var data = JObject.Parse(input.Substring(input.IndexOf("\n\n", StringComparison.Ordinal) + 2));
                Check(data.Count == 3 && data["npc"] != null &&
                    (string)data["emotion"] == emotion && body["max_tokens"] == null,
                    "Prompt added a restriction or lost the saved emotion.");
                string original = (string)data["original"];
                if (failAllApisForChoice && original == source.MainStory[choice].text)
                {
                    failedProviders.Add(request.Provider);
                    return Task.FromResult(new DirectLlmHttpResponse { StatusCode = 503 });
                }
                if (failAllApisForChoice && original == source.MainStory[1].text && request.Provider == DirectLlmProvider.Gemini)
                    return Task.FromResult(new DirectLlmHttpResponse { StatusCode = 503 });
                string output = original == source.MainStory[0].text ? longReply :
                    original == source.MainStory[choice].text ? taxParaphrase : numericReply;
                string dialogueJson = new JObject { ["text"] = output }.ToString();
                var response = request.Provider == DirectLlmProvider.Gemini
                    ? new JObject
                    {
                        ["candidates"] = new JArray(new JObject
                        {
                            ["finishReason"] = "STOP",
                            ["content"] = new JObject { ["parts"] = new JArray(new JObject { ["text"] = dialogueJson }) }
                        })
                    }
                    : new JObject
                    {
                        ["choices"] = new JArray(new JObject
                        {
                            ["finish_reason"] = "stop",
                            ["message"] = new JObject { ["content"] = dialogueJson }
                        })
                    };
                return Task.FromResult(new DirectLlmHttpResponse
                {
                    StatusCode = 200,
                    TransportSucceeded = true,
                    Body = response.ToString()
                });
            }
            var options = new DirectLlmOptions { roundRobin = false };
            if (!failAllApisForChoice)
                foreach (var endpoint in options.endpoints) endpoint.enabled = endpoint.provider == DirectLlmProvider.Cloudflare;
            var generator = new DirectStoryGenerator(new DirectLlmClient(options, new DialogueTransport(Reply),
                _ => new DirectLlmCredentials("test-key", new string('a', 32))));
            try { await LocalStoryDialogueQueue.Process(store, pending, generator, stop.Token); }
            catch (OperationCanceledException) { }
            var stopped = LocalStoryDialogueQueue.Next(store);
            Check(stopped.emotion == emotion && stopped.emotionLines.Count(line => line.finished) == 1, "Checkpoint lost the finished line.");
            Check(!store.HasNarrativeContent(2, path), "Interrupted job published partial output.");

            store.SaveProfile(new ProfileData { currentRun = 2 });
            await LocalStoryDialogueQueue.Process(store, LocalStoryDialogueQueue.Next(store), generator, CancellationToken.None);

            var completed = LocalStoryDialogueQueue.Load(store, 1).jobs.Single();
            Check(completed.status == "Ready" && completed.emotionLines.Count(line => !string.IsNullOrEmpty(line.generatedText)) == 3,
                completed.error ?? "The API replies were not saved.");
            var choiceReply = completed.emotionLines.Single(line => line.cardIndex == choice);
            if (failAllApisForChoice)
            {
                Check(failedProviders.SequenceEqual(options.endpoints.Select(endpoint => endpoint.provider)),
                    "Gemma must run after every enabled API fails.");
                Check(choiceReply.apiProvider == "Gemma" && completed.generationWinner == "Direct HTTP + Gemma" &&
                    completed.emotionLines.Single(line => line.cardIndex == 0).apiProvider == "Gemini" &&
                    completed.emotionLines.Single(line => line.cardIndex == 1).apiProvider == "Groq",
                    "Successful API results or the fallback source were replaced.");
            }
            Check(new EncounterContentRepository(1).TryLoadCards(path, out var current, out string currentError), currentError);
            Check(new EncounterContentRepository(2).TryLoadCards(path, out var replay, out string replayError), replayError);
            Check(replay.MainStory[0].text == longReply && replay.MainStory[1].text == numericReply &&
                replay.MainStory[choice].text == (failAllApisForChoice ? choiceReply.generatedText : taxParaphrase) &&
                replay.MainStory[2].text == current.MainStory[2].text,
                "An API reply changed or an unplayed line was replaced.");

            Check(replay.MainStory[choice].options.SequenceEqual(current.MainStory[choice].options), "Player choices changed.");
            Check(JsonUtility.ToJson(replay.MainStory[choice].choiceActions[0]) ==
                JsonUtility.ToJson(current.MainStory[choice].choiceActions[0]), "Choice action changed.");

            string dialogueReport = Path.Combine(root, "LocalStoryTraining", "DialogueChanges.html");
            string html = System.Net.WebUtility.HtmlDecode(File.ReadAllText(dialogueReport));
            Check(html.Contains("<strong>원문</strong>" + current.MainStory[0].text) &&
                html.Contains("<strong>변경 대사</strong>" + replay.MainStory[0].text), "The saved dialogue pair was not shown.");
            Check(html.Split(new[] { "<article>" }, StringSplitOptions.None).Length == 4 &&
                html.Contains(numericReply), "The report did not include every API reply.");
            if (failAllApisForChoice) Check(html.Contains("Gemma / Gemma"), "The report did not label the Gemma result.");
            typeof(LocalStoryRuntimeService).GetMethod("LogDialogueStatus",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { store, 2 });
            Check(dialogueLogs.Any(log => log.Contains("[대사 생성 요청]")) && dialogueLogs.Any(log => log.Contains("[대사 생성 중]")) &&
                dialogueLogs.Any(log => log.Contains("[대사 생성 완료]")) && dialogueLogs.Any(log => log.Contains("[대사 적용]")),
                "Dialogue lifecycle logs were missing.");
            Check(dialogueLogs.Any(log => log.Contains("[대사 생성 상태]") && log.Contains("1개 이야기 / 3줄")) &&
                dialogueLogs.Any(log => log.Contains("[대사 비교 HTML]") && log.Contains(dialogueReport)), "Startup status or report path was missing.");
            Check(LocalStoryDialogueQueue.Next(store) == null, "Completed work requeued.");

            var terminal = new GameProgress { activePlaceId = "brook", isCompleted = true, guideProgress = new GuideProgress() };
            terminal.guideProgress.runHistory.Add(progress.guideProgress.runHistory[0]);
            store.SaveRunCheckpoint(2, "Progress", terminal);
            var legacy = new LocalStoryDialogueQueueData { sourceRun = 2, targetRun = 3, finalized = true };
            legacy.jobs.Add(new LocalStoryDialogueJob
            {
                sourceRun = 2, targetRun = 3, storyPath = path, jobId = path, characterId = "Brook",
                status = "Generating", emotionLines = new System.Collections.Generic.List<LocalStoryEmotionLine>()
            });
            LocalStoryDialogueQueue.Save(store, legacy);
            LocalStoryDialogueQueue.RecoverFinishedRuns(store);
            var recovered = LocalStoryDialogueQueue.Next(store);
            Check(recovered?.targetRun == 3 && recovered.storyPath == path, "A terminal partial encounter was lost.");
            Check(recovered.emotionLines.Count == 1 && recovered.emotionLines[0].cardIndex == 0,
                "An unfinished encounter queued unseen cards.");
            return new { passed = true, checks, emotion, isolatedSave = root, apiReplies = failAllApisForChoice ? 2 : 3,
                gemmaReply = failAllApisForChoice ? choiceReply.generatedText : null };
        }
        finally
        {
            Application.logMessageReceived -= CaptureDialogueLog;
            instance.SetValue(null, previous);
        }
    }

    private sealed class DialogueTransport : IDirectLlmTransport
    {
        private readonly Func<DirectLlmHttpRequest, CancellationToken, Task<DirectLlmHttpResponse>> send;

        public DialogueTransport(Func<DirectLlmHttpRequest, CancellationToken, Task<DirectLlmHttpResponse>> send) => this.send = send;

        public Task<DirectLlmHttpResponse> SendAsync(DirectLlmHttpRequest request, CancellationToken token) => send(request, token);
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
