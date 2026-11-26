using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OneWay.Decision.DirectLlm;
using UnityEngine;

[Serializable] public sealed class LocalStoryDreamMemory
{
    public string ruleId, characterId, eventKey, impression;
    public int sourceRun;
}
[Serializable] public sealed class LocalStoryDialogueJob
{
    public LocalStorySectionCheckpoint omniSectionCheckpoint;
    public string generationWinner;
    public string jobId, chapterId, episodeId, characterId, sceneId, sectionId, storyPath, inputHash, contentHash, error, reportPath;
    public string status="Pending";
    public int sourceRun, targetRun, revision;
    public int relationshipValue, relationshipChange;
    public string generationPart, model;
    public double seconds;
    public bool auditArchived;
    public LocalStorySectionCheckpoint sectionCheckpoint;
    public LocalStoryRevisionRequest snapshot;
    public string sourceHash, emotion, legacyEmotionContentHash, previousDialogueContentHash;
    public string placeId;
    public int sequence;
    public List<LocalStoryEmotionLine> emotionLines;
    public bool authoredPack;
    public string authoredStoryPackMode;
}
[Serializable] public sealed class LocalStoryDialogueQueueData
{
    public int sourceRun, targetRun;
    public bool finalized;
    public string authoredStoryPackMode;
    public List<LocalStoryDialogueJob> jobs=new List<LocalStoryDialogueJob>();
}

/// <summary>Persistent next-run work. No in-progress dialogue replacement and no implicit memory promotion.</summary>
public static partial class LocalStoryDialogueQueue
{
    internal static string AuthoredJobId(string storyPath, string npcId) => storyPath + "|" + npcId;

    internal static string AuthoredMode(LocalStoryDialogueJob job, string fallback) =>
        string.IsNullOrEmpty(job.authoredStoryPackMode) ? fallback : job.authoredStoryPackMode;

    private static bool RecordAuthoredStories(LocalStoryDialogueQueueData queue,
        PlayedEncounterCardRecord[] played, string mode)
    {
        if (!played.Any(record => !string.IsNullOrEmpty(record.card?.authoredLinePath))) return false;
        bool changed = false;
        int sequence = queue.jobs.Select(job => job.sequence).DefaultIfEmpty(0).Max();
        var people = MineLearningStation.ReadContent("Relationships/NPC_전체목록")["npcs"]
            .ToDictionary(npc => (string)npc["npc_id"]);
        foreach (var record in played)
        {
            var card = record?.card;
            if (string.IsNullOrEmpty(card?.authoredLinePath) || string.IsNullOrEmpty(card.authoredOriginalText) ||
                (!string.IsNullOrEmpty(card.authoredStoryPackMode) && card.authoredStoryPackMode != mode) ||
                string.IsNullOrEmpty(card.authoredNpcId) || !people.TryGetValue(card.authoredNpcId, out var npc) ||
                record.encounterPath?.StartsWith(MineAuthoredStory.Prefix, StringComparison.Ordinal) != true) continue;
            string key = AuthoredJobId(record.encounterPath, card.authoredNpcId);
            var job = queue.jobs.FirstOrDefault(candidate => candidate.authoredPack &&
                AuthoredJobId(candidate.storyPath, candidate.characterId) == key &&
                AuthoredMode(candidate, string.IsNullOrEmpty(queue.authoredStoryPackMode) ? mode : queue.authoredStoryPackMode) == mode);
            if (job == null)
            {
                job = new LocalStoryDialogueJob { authoredPack = true, jobId = key + "|" + mode, storyPath = record.encounterPath,
                    authoredStoryPackMode = mode, sequence = ++sequence,
                    chapterId = "Chapter_1", episodeId = "Chapter_1_01", characterId = card.authoredNpcId, placeId = record.placeId,
                    sourceRun = queue.sourceRun, targetRun = queue.targetRun, revision = 1, model = DirectStoryGenerator.ModelName,
                    status = "WaitingForRunEnd", emotionLines = new List<LocalStoryEmotionLine>() };
                queue.jobs.Add(job);
            }
            if (!CanAddLines(job) || job.emotionLines.Any(line =>
                line.authoredLinePath == card.authoredLinePath && line.originalText == card.authoredOriginalText)) continue;
            job.emotionLines.Add(new LocalStoryEmotionLine
            {
                cardIndex = job.emotionLines.Count, authoredLinePath = card.authoredLinePath, originalText = card.authoredOriginalText,
                speakerId = card.authoredNpcId, name = (string)npc["name"], role = (string)npc["role"],
                speechStyle = (string)npc["speech_style"], personality = (string)npc["personality"],
                publicFacts = npc["character_traits"]?.Values<string>().ToArray()
            });
            if (job.status == "Ready") job.status = "Pending";
            changed = true;
        }
        return changed;
    }

    private static bool CanAddLines(LocalStoryDialogueJob job) =>
        job.status == "WaitingForRunEnd" || job.status == "Pending" || job.status == "Generating" || job.status == "Ready";

    public const string FileName="LocalStoryDialogueQueue";
    public static string Hash(string text)
    {
        using var sha=SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
    }
    public static LocalStoryDialogueQueueData Load(SaveIOService store,int run) =>
        store.LoadNarrativeQueue(run);
    public static void Save(SaveIOService store,LocalStoryDialogueQueueData queue)
    {
        store.SaveNarrativeQueue(queue);
    }
    public static void EncounterEnded(SaveIOService store, int run, string placeId)
    {
        var progress = store.LoadRunData<GameProgress>(run, "Progress");
        if (progress?.unlockedIds?.Contains("event:" + placeId) != true) return;
        RecordCompletedStories(store, run, progress, card => card.placeId == placeId);
    }

    public static void FinalizeRun(SaveIOService store, int run)
    {
        var progress = store.LoadRunData<GameProgress>(run, "Progress");
        if (progress?.isCompleted != true) return;
        var previous = Load(store, run);
        var legacy = previous?.jobs.Where(job => !job.authoredPack && string.IsNullOrEmpty(job.sourceHash) &&
            (job.status == "Pending" || job.status == "Generating")).Select(job => job.storyPath).ToHashSet();
        if (previous?.finalized == true && legacy.Count == 0 &&
            !previous.jobs.Any(job => job.status == "WaitingForRunEnd")) return;

        // Recover the last displayed dialogue when a terminal encounter ends the run.
        var completed = new HashSet<string>(progress.unlockedIds ?? new List<string>());
        RecordCompletedStories(store, run, progress, card =>
            (completed.Contains("event:" + card.placeId) || card.placeId == progress.activePlaceId) &&
            (previous?.finalized != true || legacy.Contains(card.encounterPath)));

        var queue = Load(store, run);
        if (queue == null) return;
        foreach (var job in queue.jobs.Where(job => !job.authoredPack && string.IsNullOrEmpty(job.sourceHash) &&
            (job.status == "Pending" || job.status == "Generating")))
        {
            job.status = "Preserved";
            job.error = "기존 요청의 실제 재생 카드가 없어 작업 기록을 보존합니다.";
        }
        queue.finalized = true;
        Save(store, queue);
    }

    public static void RecoverCompletedStories(SaveIOService store)
    {
        int current = CurrentRun(store);
        if (current > 1) FinalizeRun(store, current - 1);
        if (current < 1) return;
        var progress = store.LoadRunData<GameProgress>(current, "Progress");
        if (progress?.isCompleted == true) FinalizeRun(store, current);
        else if (progress?.unlockedIds != null)
        {
            var completed = new HashSet<string>(progress.unlockedIds);
            RecordCompletedStories(store, current, progress, card => completed.Contains("event:" + card.placeId));
        }
    }

    private static void RecordCompletedStories(SaveIOService store, int run, GameProgress progress,
        Func<PlayedEncounterCardRecord, bool> include)
    {
        var queue = Load(store, run) ?? new LocalStoryDialogueQueueData { sourceRun = run, targetRun = run + 1 };
        var played = progress.guideProgress?.runHistory?.Where(card => card != null && include(card)).ToArray()
            ?? Array.Empty<PlayedEncounterCardRecord>();
        bool changed = RecordPlayedStories(queue, played);
        changed |= RecordAuthoredStories(queue, played, MineAuthoredStory.ModeId(progress.guideProgress?.authoredMine?.shortStoryPack == true));

        // Older waiting jobs were already recorded at completed locations.
        var waiting = queue.jobs.Where(job => job.status == "WaitingForRunEnd").ToArray();
        if (waiting.Length > 0)
        {
            var random = new System.Random(Guid.NewGuid().GetHashCode());
            var authoredEmotions = LocalStoryEmotion.DefaultEmotions;
            string shared = queue.jobs.FirstOrDefault(job => job.authoredPack && !string.IsNullOrWhiteSpace(job.emotion))?.emotion
                ?? authoredEmotions[random.Next(authoredEmotions.Length)];
            foreach (var job in waiting)
            {
                var emotions = LocalStoryEmotion.EmotionsFor(job.storyPath);
                job.emotion = job.authoredPack ? shared : emotions[random.Next(emotions.Length)];
                job.status = "Pending";
                job.generationPart = "감정 · " + job.emotion;
            }
            changed = true;
        }
        if (!changed) return;
        Save(store, queue);
        int pending = queue.jobs.Count(job => job.status == "Pending");
        LocalStoryLog.Write(LocalStoryLogStage.Request,
            $"장소 이야기 종료 · {pending}개 대기 / {run + 1}회차용", activity: "대사 생성");
    }

    private static bool RecordPlayedStories(LocalStoryDialogueQueueData queue, PlayedEncounterCardRecord[] played)
    {
        int run = queue.sourceRun;
        int sequence = queue.jobs.Select(job => job.sequence).DefaultIfEmpty(0).Max();
        bool changed = false;
        foreach (var group in played.Where(card => !string.IsNullOrEmpty(card?.encounterPath)).GroupBy(card => card.encounterPath))
        {
            string path = group.Key;
            if (ChapterContentCatalog.Info(path) == null || !ChapterContentCatalog.CanRewrite(path) ||
                ChapterContentCatalog.ResolveStoryPath(path, run + 1) != path) continue;

            string source = ChapterContentCatalog.Story(path);
            string sourceHash = Hash(source);
            var job = queue.jobs.FirstOrDefault(candidate => candidate.jobId == path);
            bool isEmotionJob = !string.IsNullOrEmpty(job?.sourceHash);
            if (job != null && !isEmotionJob && job.status != "Pending" && job.status != "Generating")
                continue; // Keep a completed or manually preserved legacy result.
            if (isEmotionJob && (job.sourceHash != sourceHash || !CanAddLines(job))) continue;

            var cards = JsonUtility.FromJson<EncounterCardRoot>(source).MainStory;
            string chapterId = path.Split('/')[0];
            var lines = isEmotionJob ? job.emotionLines : new List<LocalStoryEmotionLine>();
            int previousCount = lines.Count;
            foreach (var record in group)
            {
                if (record.cardIndex < 0 || record.cardIndex >= cards.Count ||
                    lines.Any(line => line.cardIndex == record.cardIndex)) continue;
                var card = cards[record.cardIndex];
                if (record.card?.speakerId != card.speakerId || record.card?.eventIdStable != card.eventIdStable)
                    continue;
                var line = LocalStoryEmotion.CreateLine(chapterId, card, record.cardIndex);
                if (line != null) lines.Add(line);
            }
            if (previousCount == lines.Count) continue;

            if (!isEmotionJob)
            {
                if (job != null) queue.jobs.Remove(job);
                job = new LocalStoryDialogueJob
                {
                    jobId = path, storyPath = path, placeId = group.First().placeId,
                    characterId = (string)ChapterContentCatalog.Info(path)["characterId"],
                    sourceRun = run, targetRun = run + 1, revision = 1, sequence = ++sequence, sourceHash = sourceHash,
                    inputHash = Hash(sourceHash + "|" + run + "|" + path), model = DirectStoryGenerator.ModelName, status = "WaitingForRunEnd",
                    emotionLines = lines
                };
                queue.jobs.Add(job);
            }
            if (job.status == "Ready")
            {
                job.previousDialogueContentHash = job.contentHash;
                job.status = "Pending";
            }
            changed = true;
        }
        return changed;
    }

    private static int CurrentRun(SaveIOService store)
    {
        string profile = Path.Combine(store.StorageRoot, "Profile.json");
        return File.Exists(profile) ? JsonUtility.FromJson<ProfileData>(File.ReadAllText(profile))?.currentRun ?? 0 : 0;
    }

    public static bool CanProcess(SaveIOService store, LocalStoryDialogueJob job)
    {
        if (job?.emotionLines?.Count is null or 0 || string.IsNullOrWhiteSpace(job.emotion) ||
            job.targetRun != job.sourceRun + 1 || (!job.authoredPack && ChapterContentCatalog.Info(job.storyPath) == null)) return false;
        int current = CurrentRun(store);
        return current == job.sourceRun || current == job.targetRun;
    }

    public static LocalStoryDialogueJob Next(SaveIOService store, ISet<(int Run, string Job)> skipped = null) =>
        store.NextNarrativeJob(skipped);

    internal static void RequeueLegacyDialogue(SaveIOService store)
    {
        int current = CurrentRun(store);
        foreach (int run in new[] { current - 1, current })
        {
            var queue = Load(store, run);
            if (queue?.finalized != true) continue;
            var legacy = queue.jobs.Where(job => job.model != DirectStoryGenerator.ModelName &&
                (job.status == "Generating" || job.status == "Pending") &&
                job.emotionLines?.Any(line => !line.finished) == true &&
                CanProcess(store, job)).ToArray();
            if (legacy.Length == 0) continue;
            foreach (var job in legacy)
            {
                job.legacyEmotionContentHash = job.contentHash;
                job.contentHash = null;
                job.model = DirectStoryGenerator.ModelName;
                job.status = "Pending";
                job.generationWinner = null;
                foreach (var line in job.emotionLines.Where(line => !line.finished))
                {
                    line.generatedText = null;
                    line.rejection = null;
                    line.emotionFallback = false;
                    line.finished = false;
                }
            }
            Save(store, queue);
        }
    }

    private static async Task<(string Text, string Provider, string Model)> GenerateLine(
        DirectStoryGenerator generator, LocalStoryDialogueJob job, LocalStoryEmotionLine line,
        string original, CancellationToken token)
    {
        try
        {
            var result = await generator.RewriteDialogueAsync(line, job.emotion, original, token);
            return (result.Text, result.Provider.ToString(), result.Model);
        }
        catch (DirectLlmGenerationException error)
        {
            token.ThrowIfCancellationRequested();
            LocalStoryLog.Write(LocalStoryLogStage.Working,
                $"{job.storyPath} / 대사 {line.cardIndex + 1} / " +
                string.Join(", ", error.Attempts.Select(attempt => $"{attempt.Provider}:{attempt.Code}")) +
                " / 내장 Gemma 사용",
                activity: "대사 생성");
            string messages = LocalStoryGemmaDialogue.Messages(line, job.emotion, original, nextRun: true);
            int seed = unchecked(job.sourceRun * 1000003 + line.cardIndex);
            string model = LocalStoryTestModels.Name(LocalStoryTestModel.Gemma);
            try
            {
                var result = await LocalStoryGemmaDialogue.Generate(messages, seed, token, nextRun: true);
                return (result.Text, LocalStoryGemmaDialogue.ProviderName, model);
            }
            catch (DirectLlmFailure)
            {
                return (null, LocalStoryGemmaDialogue.ProviderName, model);
            }
        }
    }

    private static void CompleteLine(LocalStoryEmotionLine line, string original,
        (string Text, string Provider, string Model) result)
    {
        line.emotionFallback = LocalStoryDialogueRules.Rejection(original, result.Text) != null;
        // An absent override uses the canonical dialogue both for authored and stored stories.
        line.generatedText = line.emotionFallback ? null : result.Text;
        line.rejection = line.emotionFallback ? "유효한 대사가 없어 원문을 사용합니다." : null;
        line.apiProvider = result.Provider;
        line.apiModel = result.Model;
        line.originalText = original;
        line.finished = true;
    }

    public static async Task Process(SaveIOService store, LocalStoryDialogueJob job,
        DirectStoryGenerator generator, CancellationToken token)
    {
        if (!CanProcess(store, job)) return;
        job = Load(store, job.sourceRun)?.jobs.FirstOrDefault(candidate => candidate.jobId == job.jobId);
        if (job == null) return;
        if (job.status != "Pending" && job.status != "Generating") return;

        string source = job.authoredPack ? null : ChapterContentCatalog.Story(job.storyPath);
        if (!job.authoredPack && (Hash(source) != job.sourceHash || !ChapterContentCatalog.CanRewrite(job.storyPath) ||
            ChapterContentCatalog.ResolveStoryPath(job.storyPath, job.targetRun) != job.storyPath ||
            !store.CanReplaceEmotionDialogue(job)))
        {
            job.status = "Preserved";
            job.error = "원문이 바뀌었거나 기존 적용본이 있습니다.";
            SaveJob(store, job);
            return;
        }

        job.status = "Generating";
        job.error = null;
        job.model = DirectStoryGenerator.ModelName;
        SaveJob(store, job);
        int remaining = job.emotionLines.Count(line => !line.finished);
        LocalStoryLog.Write(LocalStoryLogStage.Working,
            $"{job.storyPath} / {job.emotion} / {remaining}줄 남음 / {job.targetRun}회차용", activity: "대사 생성");
        try
        {
            var cards = job.authoredPack ? null : JsonUtility.FromJson<EncounterCardRoot>(source);
            await GenerateLocationBatch(store, job, generator, source, token);
            foreach (var line in job.emotionLines.Where(line => !line.finished))
            {
                token.ThrowIfCancellationRequested();
                var card = job.authoredPack ? new EncounterCard { text = line.originalText } : cards.MainStory[line.cardIndex];
                var result = await GenerateLine(generator, job, line, card.text, token);
                token.ThrowIfCancellationRequested();
                CompleteLine(line, card.text, result);
                SaveJob(store, job);
            }

            token.ThrowIfCancellationRequested();
            if (!CanProcess(store, job) || (!job.authoredPack &&
                (Hash(ChapterContentCatalog.Story(job.storyPath)) != job.sourceHash ||
                 !store.CanReplaceEmotionDialogue(job))))
            {
                job.status = "Preserved";
                job.error = "생성 도중 적용 조건이 바뀌었습니다.";
                return;
            }

            var accepted = job.emotionLines.Where(line => !string.IsNullOrEmpty(line.generatedText)).ToArray();
            if (accepted.Length == 0)
            {
                job.status = "Rejected";
                job.error = "저장할 NPC 응답이 없습니다.";
                return;
            }

            if (!job.authoredPack)
            {
                foreach (var line in accepted) cards.MainStory[line.cardIndex].text = line.generatedText;
                job.contentHash = Hash(JsonUtility.ToJson(cards, true));
                SaveJob(store, job); // Recover an interrupted commit using the same expected content hash.
                store.SaveEmotionDialogue(job, cards, accepted);
                job.previousDialogueContentHash = null;
            }
            job.status = "Ready";
            int gemmaLines = accepted.Count(line => line.apiProvider == LocalStoryGemmaDialogue.ProviderName);
            job.generationWinner = gemmaLines == 0 ? job.model :
                gemmaLines == accepted.Length ? LocalStoryGemmaDialogue.ProviderName : "Direct HTTP + Gemma";
            job.generationPart = job.emotion + " · " + accepted.Length + "/" + job.emotionLines.Count + "줄";
            LocalStoryLog.Write(LocalStoryLogStage.Complete,
                $"{job.storyPath} / {job.generationPart} / {job.targetRun}회차용", activity: "대사 생성");
        }
        catch (OperationCanceledException)
        {
            job.status = "Pending";
            job.error = "다음 실행에서 남은 문장부터 재개";
            throw;
        }
        catch (InvalidDataException error)
        {
            job.status = "Rejected";
            job.error = error.Message;
            LocalStoryLog.Write(LocalStoryLogStage.Failed, job.storyPath + " / " + error.Message, activity: "대사 생성");
        }
        catch (Exception error)
        {
            job.status = "Pending";
            job.error = error.Message;
            throw;
        }
        finally
        {
            SaveJob(store, job);
        }
    }

    private static void SaveJob(SaveIOService store, LocalStoryDialogueJob job) => SaveJobs(store, job.sourceRun, new[] { job });

    private static void SaveJobs(SaveIOService store, int run, IEnumerable<LocalStoryDialogueJob> jobs)
    {
        var queue = Load(store, run);
        foreach (var job in jobs) MergeJob(queue, job);
        Save(store, queue);
    }

    private static void MergeJob(LocalStoryDialogueQueueData queue, LocalStoryDialogueJob job)
    {
        int index = queue.jobs.FindIndex(candidate => candidate.jobId == job.jobId);
        if (index < 0) return;
        var stored = queue.jobs[index];
        var update = job;
        if (stored.emotionLines.Count > job.emotionLines.Count)
        {
            // Keep the in-flight input snapshot stable. New lines run after this batch.
            update = JsonUtility.FromJson<LocalStoryDialogueJob>(JsonUtility.ToJson(job));
            update.emotionLines.AddRange(stored.emotionLines.Skip(job.emotionLines.Count));
            if (job.status == "Ready")
            {
                update.previousDialogueContentHash = job.contentHash;
                update.status = "Pending";
            }
        }
        queue.jobs[index] = update;
    }
}
