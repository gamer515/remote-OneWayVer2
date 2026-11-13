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
        if (Application.isPlaying || LocalStoryRuntimeService.IsBusy) throw new InvalidOperationException("Stop gameplay first.");
        const string path = "Chapter_1/Chapter_1_01/Encounters/brook";
        string root = Path.GetFullPath("tmp/GemmaEmotionChecks/" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks++;
        }

        var source = JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Story(path));
        var validChange = (Func<string, string, bool>)typeof(LocalStoryDialogueQueue).Assembly.GetType("LocalStoryEmotion")
            .GetMethod("ValidChange", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .CreateDelegate(typeof(Func<string, string, bool>));
        Check(!validChange(source.MainStory[0].text,
            "어휴, 웬일이신지? 어제까지만 해도 이 곡괭이가 이렇게 묵직했어야 하는데. 으으, 뭔가 이상한데요?"),
            "A real Gemma output that reversed the weight fact passed.");
        Check(!validChange(source.MainStory[7].text, "들어갈 땐 통행세 낼 거구먼. 쯧, 챙겨야지."),
            "A real Gemma output that weakened the demand passed.");
        Check(!validChange(source.MainStory[7].text, "들어가려면 통행세부터 지불해야 합니다."),
            "A real Gemma output that changed the NPC register passed.");
        const string weightParaphrase = "이상해. 어제까지만 해도 이 곡괭이가 이 정도로 무겁진 않았는데.";
        const string taxParaphrase = "안으로 가고 싶으면 먼저 통행세를 내라고!";
        Check(validChange(source.MainStory[0].text, weightParaphrase) && validChange(source.MainStory[7].text, taxParaphrase),
            "A paraphrase that preserved the fact or demand was blocked.");
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
            Check(new[] { "화", "겁", "슬픔", "기쁨", "당황", "안도" }.Contains(emotion), "Emotion outside the pool.");
            LocalStoryDialogueQueue.FinalizeRun(store, 1);
            Check(LocalStoryDialogueQueue.Next(store).emotion == emotion, "Finalization rerolled emotion.");

            int calls = 0;
            Task<string> Interrupt(string messages, string grammar, int seed, CancellationToken token)
            {
                if (++calls == 3) stop.Cancel();
                token.ThrowIfCancellationRequested();
                return Reply(messages, grammar, seed, token);
            }
            Task<string> Reply(string messages, string grammar, int seed, CancellationToken token)
            {
                string input = (string)JArray.Parse(messages)[0]["content"];
                var data = JObject.Parse(input.Substring(input.IndexOf("\n\n", StringComparison.Ordinal) + 2));
                Check(data["options"] == null && data["candidate"]?.Type != JTokenType.Array &&
                    (string)data["emotion"] == emotion, "Prompt exposed choices or lost its saved emotion.");
                if (data["candidate"] != null) return Task.FromResult("{\"usable\":true}");
                string original = (string)data["original"];
                string text = original == source.MainStory[0].text ? weightParaphrase :
                    original == source.MainStory[choice].text ? taxParaphrase : "거기엔 99명이 숨어 있어.";
                return Task.FromResult(new JObject { ["text"] = text }.ToString());
            }
            try { await LocalStoryDialogueQueue.Process(store, pending, Interrupt, stop.Token); }
            catch (OperationCanceledException) { }
            var stopped = LocalStoryDialogueQueue.Next(store);
            Check(stopped.emotion == emotion && stopped.emotionLines.Count(line => line.finished) == 1, "Checkpoint lost the finished line.");
            Check(!store.HasNarrativeContent(2, path), "Interrupted job published partial output.");

            store.SaveProfile(new ProfileData { currentRun = 2 });
            await LocalStoryDialogueQueue.Process(store, LocalStoryDialogueQueue.Next(store), Reply, CancellationToken.None);
            var completed = LocalStoryDialogueQueue.Load(store, 1).jobs.Single();
            Check(completed.status == "Ready" && completed.emotionLines.Count(line => !string.IsNullOrEmpty(line.generatedText)) == 2,
                completed.error ?? "Expected only valid NPC lines to be accepted.");
            Check(string.IsNullOrEmpty(completed.emotionLines[1].generatedText) &&
                !string.IsNullOrEmpty(completed.emotionLines[1].rejection), "Numeric change passed validation.");
            Check(new EncounterContentRepository(1).TryLoadCards(path, out var current, out string currentError), currentError);
            Check(new EncounterContentRepository(2).TryLoadCards(path, out var replay, out string replayError), replayError);
            Check(replay.MainStory[0].text != current.MainStory[0].text && replay.MainStory[1].text == current.MainStory[1].text &&
                replay.MainStory[2].text == current.MainStory[2].text, "Run scope, rejected or unplayed text changed.");
            Check(replay.MainStory[choice].options.SequenceEqual(current.MainStory[choice].options), "Player choices changed.");
            Check(JsonUtility.ToJson(replay.MainStory[choice].choiceActions[0]) ==
                JsonUtility.ToJson(current.MainStory[choice].choiceActions[0]), "Choice action changed.");

            string training = Directory.GetFiles(Path.Combine(root, "LocalStoryTraining"), "*.jsonl", SearchOption.AllDirectories).Single();
            var examples = File.ReadAllLines(training).Select(JObject.Parse).ToArray();
            Check(examples.Length == 2 && examples.All(example => (string)example["messages"][1]["role"] == "assistant"),
                "Training candidates were missing or included rejected output.");
            string dialogueReport = Path.Combine(root, "LocalStoryTraining", "DialogueChanges.html");
            string html = System.Net.WebUtility.HtmlDecode(File.ReadAllText(dialogueReport));
            Check(html.Contains("<strong>원문</strong>" + current.MainStory[0].text) &&
                html.Contains("<strong>변경 대사</strong>" + replay.MainStory[0].text), "The saved dialogue pair was not shown.");
            Check(html.Split(new[] { "<article>" }, StringSplitOptions.None).Length == 3 &&
                !html.Contains("거기엔 99명이 숨어 있어."), "The report included a rejected line or missed an accepted line.");
            typeof(LocalStoryRuntimeService).GetMethod("LogDialogueStatus",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { store, 2 });
            Check(dialogueLogs.Any(log => log.Contains("[대사 생성 요청]")) && dialogueLogs.Any(log => log.Contains("[대사 생성 중]")) &&
                dialogueLogs.Any(log => log.Contains("[대사 생성 완료]")) && dialogueLogs.Any(log => log.Contains("[대사 적용]")),
                "Dialogue lifecycle logs were missing.");
            Check(dialogueLogs.Any(log => log.Contains("[대사 생성 상태]") && log.Contains("1개 이야기 / 2줄")) &&
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
            return new { passed = true, checks, emotion, isolatedSave = root, trainingExamples = examples.Length };
        }
        finally
        {
            Application.logMessageReceived -= CaptureDialogueLog;
            instance.SetValue(null, previous);
        }
    }

    public static async Task<object> CheckParaphrasePrompt()
    {
        const string path = "Chapter_1/Chapter_1_01/Encounters/brook";
        var card = JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Story(path)).MainStory[7];
        var line = new LocalStoryEmotionLine { cardIndex = 7, speakerId = "Brook", name = "브룩" };
        var job = new LocalStoryDialogueJob { storyPath = path, emotion = "화", sourceRun = 1 };
        const string paraphrase = "안으로 가고 싶으면 먼저 통행세를 내라고!";
        int drafts = 0, reviews = 0;
        Task<string> Reply(string messages, string grammar, int seed, CancellationToken token)
        {
            string content = (string)JArray.Parse(messages)[0]["content"];
            var input = JObject.Parse(content.Substring(content.IndexOf("\n\n", StringComparison.Ordinal) + 2));
            if (input["candidate"] != null)
            {
                reviews++;
                if ((string)input["candidate"] != paraphrase) throw new InvalidOperationException("A surface-only change reached review.");
                return Task.FromResult("{\"usable\":true}");
            }
            if (input["options"] != null || !content.Contains("어휘나 어순"))
                throw new InvalidOperationException("Wrong dialogue prompt.");
            return Task.FromResult(new JObject { ["text"] = ++drafts == 1 ? "쯧, " + card.text : paraphrase }.ToString());
        }
        Func<string, string, int, CancellationToken, Task<string>> infer = Reply;
        var engine = typeof(LocalStoryDialogueQueue).Assembly.GetType("LocalStoryEmotion");
        await (Task)engine.GetMethod("RewriteLine", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { job, line, card, infer, CancellationToken.None });
        if (drafts != 2 || reviews != 1 || line.generatedText != paraphrase || !line.paraphrased)
            throw new InvalidOperationException("The rewrite did not retry and accept the paraphrase.");
        return new { passed = true, drafts, reviews };
    }

    public static string StartEmotionGemmaSample()
    {
        string directory = Path.GetFullPath("tmp/GemmaEmotionChecks");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "Gemma-" + Guid.NewGuid().ToString("N") + ".json");
        _ = WriteEmotionGemmaSample(file);
        return file;
    }

    private static async Task WriteEmotionGemmaSample(string file)
    {
        object result;
        try { result = await EmotionGemmaSample(); }
        catch (Exception error) { result = new { error = error.ToString() }; }
        File.WriteAllText(file, JObject.FromObject(result).ToString());
    }

    private static async Task<object> EmotionGemmaSample()
    {
        if (LocalStoryRuntimeService.IsBusy || LocalStoryRuntimeService.IsPreparing)
            throw new InvalidOperationException("Wait for the current Gemma work to finish.");
        const string path = "Chapter_1/Chapter_1_01/Encounters/brook";
        var emotion = typeof(LocalStoryDialogueQueue).Assembly.GetType("LocalStoryEmotion");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var infer = (Func<string, string, int, CancellationToken, Task<string>>)emotion.GetMethod("Infer", flags)
            .CreateDelegate(typeof(Func<string, string, int, CancellationToken, Task<string>>));
        var cards = JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Story(path)).MainStory;
        var results = new JArray();
        foreach (var sample in new[] { (index: 0, keyword: "겁"), (index: 7, keyword: "화"), (index: 4, keyword: "당황") })
        {
            var card = cards[sample.index];
            var line = (LocalStoryEmotionLine)emotion.GetMethod("CreateLine", flags)
                .Invoke(null, new object[] { "Chapter_1", card, sample.index });
            var job = new LocalStoryDialogueJob { storyPath = path, emotion = sample.keyword, sourceRun = 1 };
            var replies = new JArray();
            async Task<string> Observe(string messages, string grammar, int seed, CancellationToken token)
            {
                string output = await infer(messages, grammar, seed, token);
                replies.Add(output);
                return output;
            }
            Func<string, string, int, CancellationToken, Task<string>> observe = Observe;
            await (Task)emotion.GetMethod("RewriteLine", flags).Invoke(null, new object[] { job, line, card, observe, CancellationToken.None });
            results.Add(new JObject { ["original"] = card.text, ["emotion"] = job.emotion,
                ["generated"] = line.generatedText, ["rejection"] = line.rejection, ["replies"] = replies });
        }
        return new { samples = results };
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
