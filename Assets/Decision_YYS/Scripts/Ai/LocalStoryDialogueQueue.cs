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
    public string sourceHash, emotion, legacyEmotionContentHash;
    public int sequence;
    public List<LocalStoryEmotionLine> emotionLines;
    public bool authoredPack;
}
[Serializable] public sealed class LocalStoryDialogueQueueData
{
    public int sourceRun, targetRun;
    public bool finalized;
    public string authoredStoryPackMode;
    public List<LocalStoryDialogueJob> jobs=new List<LocalStoryDialogueJob>();
}

/// <summary>Persistent next-run work. No in-progress dialogue replacement and no implicit memory promotion.</summary>
public static class LocalStoryDialogueQueue
{
    internal static string AuthoredJobId(string storyPath, string npcId) => storyPath + "|" + npcId;

    private static void RecordAuthoredStories(LocalStoryDialogueQueueData queue, GuideProgress progress)
    {
        var played = progress?.runHistory;
        if (played == null) return;
        string mode = MineAuthoredStory.ModeId(progress.authoredMine?.shortStoryPack == true);
        var people = MineLearningStation.ReadContent("Relationships/NPC_전체목록")["npcs"]
            .ToDictionary(npc => (string)npc["npc_id"]);
        foreach (var record in played)
        {
            var card = record?.card;
            if (string.IsNullOrEmpty(card?.authoredLinePath) || string.IsNullOrEmpty(card.authoredOriginalText) ||
                (!string.IsNullOrEmpty(card.authoredStoryPackMode) && card.authoredStoryPackMode != mode) ||
                string.IsNullOrEmpty(card.authoredNpcId) || !people.TryGetValue(card.authoredNpcId, out var npc) ||
                record.encounterPath?.StartsWith(MineAuthoredStory.Prefix, StringComparison.Ordinal) != true) continue;
            queue.authoredStoryPackMode = mode;
            string key = AuthoredJobId(record.encounterPath, card.authoredNpcId);
            var job = queue.jobs.FirstOrDefault(candidate => candidate.jobId == key);
            if (job == null)
            {
                job = new LocalStoryDialogueJob { authoredPack = true, jobId = key, storyPath = record.encounterPath,
                    chapterId = "Chapter_1", episodeId = "Chapter_1_01", characterId = card.authoredNpcId,
                    sourceRun = queue.sourceRun, targetRun = queue.targetRun, revision = 1, model = DirectStoryGenerator.ModelName,
                    status = "WaitingForRunEnd", emotionLines = new List<LocalStoryEmotionLine>() };
                queue.jobs.Add(job);
            }
            if (job.status != "WaitingForRunEnd" || job.emotionLines.Any(line =>
                line.authoredLinePath == card.authoredLinePath && line.originalText == card.authoredOriginalText)) continue;
            job.emotionLines.Add(new LocalStoryEmotionLine
            {
                cardIndex = job.emotionLines.Count, authoredLinePath = card.authoredLinePath, originalText = card.authoredOriginalText,
                speakerId = card.authoredNpcId, name = (string)npc["name"], role = (string)npc["role"],
                speechStyle = (string)npc["speech_style"], personality = (string)npc["personality"],
                publicFacts = npc["character_traits"]?.Values<string>().ToArray()
            });
        }
    }
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
        RecordPlayedStories(store, run, progress.guideProgress?.runHistory?.Where(card => card.placeId == placeId));
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

        // Recover any encounter saved just before quitting, including the displayed part of a terminal encounter.
        var completed = new HashSet<string>(progress.unlockedIds ?? new List<string>());
        RecordPlayedStories(store, run, progress.guideProgress?.runHistory?.Where(card =>
            card != null && (completed.Contains("event:" + card.placeId) || card.placeId == progress.activePlaceId) &&
            (previous?.finalized != true || legacy.Contains(card.encounterPath))), previous?.finalized == true);

        var queue = Load(store, run);
        queue ??= new LocalStoryDialogueQueueData { sourceRun = run, targetRun = run + 1 };
        RecordAuthoredStories(queue, progress.guideProgress);
        if (queue.jobs.Count == 0) return;
        foreach (var job in queue.jobs.Where(job => !job.authoredPack && string.IsNullOrEmpty(job.sourceHash) &&
            (job.status == "Pending" || job.status == "Generating")))
        {
            job.status = "Preserved";
            job.error = "기존 요청의 실제 재생 카드가 없어 작업 기록을 보존합니다.";
        }
        var random = new System.Random(Guid.NewGuid().GetHashCode());
        var shared = LocalStoryEmotion.DefaultEmotions[random.Next(LocalStoryEmotion.DefaultEmotions.Length)];
        foreach (var job in queue.jobs.Where(job => job.status == "WaitingForRunEnd"))
        {
            var emotions = LocalStoryEmotion.EmotionsFor(job.storyPath);
            job.emotion = job.authoredPack ? shared : emotions[random.Next(emotions.Length)];
            job.status = "Pending";
            job.generationPart = "감정 · " + job.emotion;
        }
        queue.finalized = true;
        Save(store, queue);
        int pending = queue.jobs.Count(job => job.status == "Pending");
        LocalStoryLog.Write(LocalStoryLogStage.Request,
            $"{run}회차 종료 · {pending}개 이야기 / {run + 1}회차용", activity: "대사 생성");
    }

    public static void RecoverFinishedRuns(SaveIOService store)
    {
        int current = CurrentRun(store);
        if (current > 1) FinalizeRun(store, current - 1);
        if (current > 0) FinalizeRun(store, current);
    }

    private static void RecordPlayedStories(SaveIOService store, int run, IEnumerable<PlayedEncounterCardRecord> played,
        bool migrateFinishedQueue = false)
    {
        if (played == null) return;
        var queue = Load(store, run) ?? new LocalStoryDialogueQueueData { sourceRun = run, targetRun = run + 1 };
        if (queue.finalized && !migrateFinishedQueue) return;
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
            if (isEmotionJob && job.sourceHash != sourceHash) continue;

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
                    jobId = path, storyPath = path, characterId = (string)ChapterContentCatalog.Info(path)["characterId"],
                    sourceRun = run, targetRun = run + 1, revision = 1, sequence = ++sequence, sourceHash = sourceHash,
                    inputHash = Hash(sourceHash + "|" + run + "|" + path), model = DirectStoryGenerator.ModelName, status = "WaitingForRunEnd",
                    emotionLines = lines
                };
                queue.jobs.Add(job);
            }
            changed = true;
        }
        if (changed) Save(store, queue);
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
        return (current == job.sourceRun || current == job.targetRun) &&
            Load(store, job.sourceRun)?.finalized == true &&
            store.LoadRunData<GameProgress>(job.sourceRun, "Progress")?.isCompleted == true;
    }

    public static LocalStoryDialogueJob Next(SaveIOService store) => store.NextNarrativeJob();

    internal static void RequeueLegacyDialogue(SaveIOService store)
    {
        int current = CurrentRun(store);
        foreach (int run in new[] { current - 1, current })
        {
            var queue = Load(store, run);
            if (queue?.finalized != true) continue;
            var legacy = queue.jobs.Where(job => job.model != DirectStoryGenerator.ModelName &&
                (job.status == "Ready" || job.status == "Rejected" || job.status == "Generating" || job.status == "Pending") &&
                CanProcess(store, job)).ToArray();
            if (legacy.Length == 0) continue;
            foreach (var job in legacy)
            {
                job.legacyEmotionContentHash = job.contentHash;
                job.contentHash = null;
                job.model = DirectStoryGenerator.ModelName;
                job.status = "Pending";
                job.generationWinner = null;
                foreach (var line in job.emotionLines)
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
                $"{job.storyPath} / 대사 {line.cardIndex + 1} / API {error.Attempts.Count}곳 실패 / 내장 Gemma 사용",
                activity: "대사 생성");
            string messages = LocalStoryGemmaDialogue.Messages(line, job.emotion, original);
            int seed = unchecked(job.sourceRun * 1000003 + line.cardIndex);
            var result = await LocalStoryGemmaDialogue.Generate(messages, seed, token);
            return (result.Text, LocalStoryGemmaDialogue.ProviderName, LocalStoryTestModels.Name(LocalStoryTestModel.Gemma));
        }
    }

    public static async Task Process(SaveIOService store, LocalStoryDialogueJob job,
        DirectStoryGenerator generator, CancellationToken token)
    {
        if (!CanProcess(store, job)) return;
        var queue = Load(store, job.sourceRun);
        job = queue.jobs.First(candidate => candidate.jobId == job.jobId);
        if (job.status != "Pending" && job.status != "Generating") return;

        string source = job.authoredPack ? null : ChapterContentCatalog.Story(job.storyPath);
        if (!job.authoredPack && (Hash(source) != job.sourceHash || !ChapterContentCatalog.CanRewrite(job.storyPath) ||
            ChapterContentCatalog.ResolveStoryPath(job.storyPath, job.targetRun) != job.storyPath ||
            !store.CanReplaceEmotionDialogue(job)))
        {
            job.status = "Preserved";
            job.error = "원문이 바뀌었거나 기존 적용본이 있습니다.";
            Save(store, queue);
            return;
        }

        job.status = "Generating";
        job.error = null;
        job.model = DirectStoryGenerator.ModelName;
        Save(store, queue);
        int remaining = job.emotionLines.Count(line => !line.finished);
        LocalStoryLog.Write(LocalStoryLogStage.Working,
            $"{job.storyPath} / {job.emotion} / {remaining}줄 남음 / {job.targetRun}회차용", activity: "대사 생성");
        try
        {
            var cards = job.authoredPack ? null : JsonUtility.FromJson<EncounterCardRoot>(source);
            foreach (var line in job.emotionLines.Where(line => !line.finished))
            {
                token.ThrowIfCancellationRequested();
                var card = job.authoredPack ? new EncounterCard { text = line.originalText } : cards.MainStory[line.cardIndex];
                var result = await GenerateLine(generator, job, line, card.text, token);
                token.ThrowIfCancellationRequested();
                line.generatedText = result.Text;
                line.apiProvider = result.Provider;
                line.apiModel = result.Model;
                line.originalText = card.text;
                line.finished = true;
                Save(store, queue);
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
                Save(store, queue); // Recover an interrupted commit using the same expected content hash.
                store.SaveEmotionDialogue(job, cards, accepted);
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
            Save(store, queue);
        }
    }
}
