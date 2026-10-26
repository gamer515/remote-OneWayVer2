using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Deterministic race/lifecycle checks use isolated saves. RemoteSmoke makes one real story request, never promotes it.
public static class OmniBattleIntegrationCheck
{
    static int assertions;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); assertions++; }
    static SaveIOService Storage(string root) => (SaveIOService)typeof(SaveIOService).GetConstructor(
        BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null).Invoke(new object[] { root });
    static string Patch(EncounterCardRoot root) => new JObject { ["cards"] = new JArray(root.MainStory.Select((c,i) =>
        new JObject { ["index"] = i, ["text"] = c.text, ["options"] = new JArray(c.options ?? Array.Empty<string>()) })) }.ToString(Newtonsoft.Json.Formatting.None);

    public static async Task<object> CheckAll()
    {
        assertions = 0;
        var remote = new TaskCompletionSource<string>();
        var local = new TaskCompletionSource<string>();
        var race = StoryGenerationRace.FirstApplicable(_ => local.Task, _ => remote.Task, s => s == "remote" || s == "local", CancellationToken.None);
        remote.SetResult("remote");
        Check(await race == "remote", "Remote-first result not selected");
        local.SetResult("local");
        Check(await race == "remote", "Late native result replaced remote winner");

        var waiting = new TaskCompletionSource<string>();
        race = StoryGenerationRace.FirstApplicable(_ => Task.FromResult("invalid"), _ => waiting.Task, s => s == "valid", CancellationToken.None);
        Check(!race.IsCompleted, "Invalid first response stopped the valid candidate");
        waiting.SetResult("valid");
        Check(await race == "valid", "Valid second response was not accepted");
        Check(await StoryGenerationRace.FirstApplicable(_ => Task.FromException<string>(new Exception("HTTP failure")),
            _ => Task.FromResult("native"), _ => true, CancellationToken.None) == "native", "HTTP failure blocked native completion");
        Check(await StoryGenerationRace.FirstApplicable(_ => Task.FromResult("native"),
            _ => Task.FromException<string>(new Exception("HTTP failure")), _ => true, CancellationToken.None) == "native", "Native-first result not accepted");
        bool bothFailed = false;
        try { await StoryGenerationRace.FirstApplicable<string>(_ => Task.FromException<string>(new Exception("a")),
            _ => Task.FromResult("invalid"), _ => false, CancellationToken.None); }
        catch (AggregateException) { bothFailed = true; }
        Check(bothFailed, "Both failures were not reported");
        using (var cancel = new CancellationTokenSource())
        {
            async Task<string> Forever(CancellationToken ct) { await Task.Delay(Timeout.Infinite, ct); return "never"; }
            race = StoryGenerationRace.FirstApplicable(Forever, Forever, _ => true, cancel.Token);
            cancel.Cancel();
            bool cancelled = false;
            try { await race; } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "Parent cancellation did not stop the race");
        }
        using (var alreadyCancelled = new CancellationTokenSource())
        {
            alreadyCancelled.Cancel(); int started = 0;
            Task<string> Start(CancellationToken _) { started++; return Task.FromResult("unexpected"); }
            try { await StoryGenerationRace.FirstApplicable(Start, Start, _ => true, alreadyCancelled.Token); }
            catch (OperationCanceledException) { }
            Check(started == 0, "Already cancelled race dispatched inference");
        }

        const string path = LocalStoryTestInputs.GuidePath;
        var source = JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
        var plan = LocalStoryPrompt.Plan(path, source);
        string messages = LocalStoryPrompt.Messages(path, source, 0, plan[0].count, new List<EncounterCard>(), null);
        var body = OmniRouteFreeStackConverter.RequestBody(messages, 256, 42, "explain");
        Check(JToken.DeepEquals(body["messages"], JArray.Parse(messages)), "HTTP adapter changed common prompt content");
        string native = (string)JArray.Parse(LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma, messages))[0]["content"];
        Check(native == (string)body["messages"][0]["content"] + "\n\n" + (string)body["messages"][1]["content"], "Embedded/HTTP instructions differ");
        string patch = Patch(source);
        string envelope = new JObject { ["choices"] = new JArray(new JObject { ["message"] = new JObject { ["content"] = "```json\n" + patch + "\n```" } }) }.ToString();
        Check(JToken.DeepEquals(JObject.Parse(OmniRouteFreeStackConverter.Content(envelope)), JObject.Parse(patch)), "JSON fence handling broke cards");
        var incomplete = new LocalStoryTestReport { original = source, valid = true, generated = source,
            sectionOutputs = new List<LocalStorySectionOutput> { new LocalStorySectionOutput { count = 1 } } };
        Check(!StoryGenerationRace.Applicable(incomplete), "Partial story won the whole-story race");

        var target = new LocalStoryTestReport { storyPath = path, original = source, model = LocalStoryTestModels.Name(LocalStoryTestModel.Gemma) };
        var localCheckpoint = new LocalStorySectionCheckpoint { key = "local" };
        var remoteCheckpoint = new LocalStorySectionCheckpoint { key = "remote" };
        target.sectionCheckpoint = localCheckpoint; target.omniSectionCheckpoint = remoteCheckpoint;
        int localWrites = 0, remoteWrites = 0;
        target.saveSectionCheckpoint = _ => localWrites++;
        target.saveOmniSectionCheckpoint = _ => remoteWrites++;
        var localGate = new TaskCompletionSource<bool>();
        LocalStoryTestReport late = null;
        async Task Fake(LocalStoryTestReport candidate, CancellationToken ct)
        {
            bool isRemote = LocalStoryTestModels.ModelFor(candidate) == LocalStoryTestModel.OmniRoute;
            Check(candidate.sectionCheckpoint.key == (isRemote ? "remote" : "local"), "Provider checkpoints mixed");
            candidate.saveSectionCheckpoint?.Invoke(candidate.sectionCheckpoint);
            if (!isRemote) { late = candidate; await localGate.Task; }
            candidate.valid = true; candidate.generated = candidate.original;
            candidate.rawOutput = patch;
            candidate.sectionOutputs = new List<LocalStorySectionOutput> { new LocalStorySectionOutput { count = source.MainStory.Count, valid = true } };
        }
        await StoryGenerationRace.GenerateRuntime(target, CancellationToken.None, Fake);
        Check(target.generationWinner.StartsWith("OmniRoute"), "Runtime report did not preserve winning provider");
        Check(localWrites == 1 && remoteWrites == 1, "Separate checkpoints were not saved");
        localGate.SetResult(true); await Task.Yield();
        late.saveSectionCheckpoint?.Invoke(localCheckpoint);
        Check(localWrites == 1, "Losing checkpoint overwrote committed state");
        Check(target.model.StartsWith("OmniRoute") && target.valid, "Late result changed selected report");

        var field = typeof(SaveIOService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = field.GetValue(null);
        string testRoot = Path.Combine(Environment.CurrentDirectory, "tmp", "OmniBattleChecks", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (bool won in new[] { false, true })
            {
                var store = Storage(Path.Combine(testRoot, won ? "victory" : "death")); field.SetValue(null, store);
                var saves = new SaveManager();
                saves.LoadProgress().storedItemIds.Add("LokiMark");
                saves.BeginFinalBattle(1);
                Check(saves.CurrentRun == 1 && !saves.LoadProgress().isCompleted, "Battle entry advanced/completed run prematurely");
                saves = new SaveManager();
                Check(saves.LoadProgress().finalBattlePending && saves.LoadProgress().finalBattleStage == 1, "Battle checkpoint lost after reload");
                int nextCalls = 0, menuCalls = 0;
                var completion = new DecisionRunCompletionService(() => { saves.RecordFinalBattleOutcome(won); saves.CompleteCurrentRun(); },
                    () => { nextCalls++; return saves.StartNextRun(); }, () => menuCalls++);
                completion.Complete(); completion.Complete();
                Check(saves.CurrentRun == 2 && nextCalls == 1 && menuCalls == 1, "Battle outcome advanced the run more than once");
                var ended = store.LoadRunData<GameProgress>(1, "Progress");
                Check(ended.isCompleted && ended.finalBattleWon == won && ended.runEndReason.Contains(won ? "처치" : "사망"), "Battle outcome was not saved");
                Check(!saves.LoadProgress().finalBattlePending && saves.LoadProgress().chapterIndex == 0, "Next run did not reset journey/battle state");
                Check(saves.LoadProgress().storedItemIds.Contains("LokiMark"), "Battle completion lost owned inventory");
            }
            var queueStore = Storage(Path.Combine(testRoot, "queue")); field.SetValue(null, queueStore);
            var queued = new LocalStoryDialogueJob { jobId = "winner-fixture", sourceRun = 1, targetRun = 2, characterId = "Guide", storyPath = path,
                sectionCheckpoint = new LocalStorySectionCheckpoint { key = "native-key" },
                omniSectionCheckpoint = new LocalStorySectionCheckpoint { key = "http-key" } };
            var queue = new LocalStoryDialogueQueueData { sourceRun = 1, targetRun = 2, jobs = new List<LocalStoryDialogueJob> { queued } };
            queueStore.SaveNarrativeQueue(queue);
            var restored = queueStore.LoadNarrativeQueue(1).jobs.Single();
            Check(restored.sectionCheckpoint.key == "native-key" && restored.omniSectionCheckpoint.key == "http-key", "Independent resume checkpoints did not survive storage");
            queued.status = "Ready"; queued.generationWinner = "OmniRoute / explain";
            queueStore.SaveNarrativeQueue(queue);
            Check(queueStore.LoadNarrativeQueue(1).jobs.Single().generationWinner == queued.generationWinner, "Winning provider missing from queue");
            var features = JObject.Parse(File.ReadAllText(Path.Combine(queueStore.GeneratedNpcDirectory(2, path), "Features.json")));
            Check((string)features["stories"][0]["generationWinner"] == queued.generationWinner, "NPC feature view lost winning provider");
        }
        finally { field.SetValue(null, previous); }
        var result = new { assertions, race = "remote/local first, invalid, failure, cancellation, checkpoint isolation", lifecycle = "victory and death persist and advance once",
            sharedPrompts = true, realPlayerSavesTouched = false, fixtureRoot = testRoot };
        Directory.CreateDirectory(testRoot);
        File.WriteAllText(Path.Combine(testRoot, "Result.json"), JObject.FromObject(result).ToString());
        return result;
    }

    public static async Task<object> RemoteSmoke()
    {
        string path = "Initial/Initial_01/Encounters/guide/Farewell";
        var report = new LocalStoryTestReport { storyPath = path, original = JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path)),
            model = LocalStoryTestModels.Name(LocalStoryTestModel.OmniRoute), seed = 12346, allowFailedOutput = LocalStoryPrompt.AllowFailedOutput };
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await LocalStoryRuntimePipeline.Infer(report, budget.Token);
        string root = Path.Combine(Environment.CurrentDirectory, "tmp", "OmniBattleChecks", "RemoteSmoke");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Report.json"), JsonUtility.ToJson(report, true));
        return new { report.model, report.backend, report.valid, report.reviewRequired, report.error, report.reviewError,
            report.sectionsGenerated, completeStoryApplicable = StoryGenerationRace.Applicable(report), cards = report.original.MainStory.Count,
            apiKeyWrittenToReport = false, nextRunSaveTouched = false, report = Path.Combine(root, "Report.json") };
    }

    public static async Task<object> RuntimeSmoke()
    {
        string root = Path.Combine(Environment.CurrentDirectory, "tmp", "OmniBattleChecks", "RuntimeSmoke", Guid.NewGuid().ToString("N"));
        var store = Storage(Path.Combine(root, "Saves"));
        var field = typeof(SaveIOService).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = field.GetValue(null);
        try
        {
            field.SetValue(null, store);
            store.SaveProfile(new ProfileData { currentRun = 1 });
            const string path = "Initial/Initial_01/Encounters/guide/Farewell";
            var npc = new LocalStoryNpcContext { characterId = "Guide", name = "로키", model = "Gemma", autoGenerate = true };
            var request = new LocalStoryRevisionRequest { sourceRun = 1, targetRun = 2, chapterId = "Initial", memoryMode = "None",
                npcContexts = new[] { npc } };
            LocalStoryTestReport captured = null;
            using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var result = await LocalStoryRuntimePipeline.ProduceDialogue(request, npc, path, store,
                async (report, token) => { captured = report; report.testScenario = "Synthetic / real provider race / isolated saves";
                    await StoryGenerationRace.GenerateRuntime(report, token); }, budget.Token, () => true);
            if (result.status != "Ready" && result.status != "ReviewRequired") throw new Exception("Winning result not saved: " + result.error);
            if (!store.TryLoadGeneratedContent<EncounterCardRoot>(2, "Encounters", path + "/Story", out var loaded))
                throw new Exception("Next-run content could not be loaded");
            if (!LocalStoryRewrite.SameBindings(captured.original, loaded)) throw new Exception("Next-run game bindings changed");
            string hash = store.NarrativeContentHash(2, path);
            await Task.Delay(300);
            if (hash != store.NarrativeContentHash(2, path)) throw new Exception("Late candidate overwrote winner");
            File.WriteAllText(Path.Combine(root, "Winner.json"), JsonUtility.ToJson(captured, true));
            var summary = new { winner = result.model, captured.backend, result.status, captured.valid, captured.reviewRequired,
                nextRunReloaded = true, gameBindingsPreserved = true, realPlayerSavesTouched = false, isolatedRoot = root };
            File.WriteAllText(Path.Combine(root, "Result.json"), JObject.FromObject(summary).ToString());
            return summary;
        }
        finally { field.SetValue(null, previous); }
    }
}
