using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable] public sealed class NarrativeStoryEntry
{
    public int targetRun;
    public string chapterId, episodeId, characterId, sceneId, sectionId, storyPath;
    public string sourceHash, contentHash, contentJson, origin, updatedUtc;
    public string generationPolicyHash;
    public bool reviewRequired;
    public string validationError;
    public string previousContentJson;
    public bool previousReviewRequired;
    public string previousValidationError;
    public string preservedPackJson;
    public bool enabled = true;
}

[Serializable] public sealed class NarrativeChapterSave
{
    public int schemaVersion = 1;
    public string chapterId;
    public List<NarrativeStoryEntry> stories = new List<NarrativeStoryEntry>();
    public List<LocalStoryDialogueQueueData> queues = new List<LocalStoryDialogueQueueData>();
}

/// <summary>Runtime facade for NPC packs. Previous chapter documents are read only during migration.</summary>
public partial class SaveIOService
{
    public const int NarrativeRetainedRuns = 4;
    private readonly object narrativeGate = new object();
    public string NarrativeDirectory => Path.Combine(BasePath, "Narrative", "Chapters");
    private static bool SafePart(string value) => !string.IsNullOrEmpty(value) && Regex.IsMatch(value, @"^[A-Za-z0-9_-]+$");
    private static bool SafeStoryPath(string value) => !string.IsNullOrEmpty(value) && value.Split('/').All(SafePart);
    private static bool IsNarrativeKey(int run, string type, string path) => run > 1 && type == "Encounters" &&
        SafeStoryPath(path) && path.EndsWith("/Story", StringComparison.Ordinal);
    private static string StoryPath(string key) => key.Substring(0, key.Length - "/Story".Length);
    public string NarrativeChapterPath(string chapter)
    {
        if (!SafePart(chapter)) throw new ArgumentException("잘못된 챕터 ID");
        return Path.Combine(NarrativeDirectory, chapter + ".json");
    }
    private NarrativeChapterSave ReadNarrativeChapter(string chapter)
    {
        return ReadPackedChapter(chapter);
    }
    private static NarrativeChapterSave CloneNarrative(NarrativeChapterSave value) =>
        JsonUtility.FromJson<NarrativeChapterSave>(JsonUtility.ToJson(value));
    private NarrativeChapterSave EditNarrativeChapter(string chapter) => CloneNarrative(ReadNarrativeChapter(chapter));
    private static NarrativeChapterSave ParseNarrative(string json, string chapter)
    {
        var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
        if ((int?)obj["schemaVersion"] != 1 || (string)obj["chapterId"] != chapter ||
            !(obj["stories"] is Newtonsoft.Json.Linq.JArray) || !(obj["queues"] is Newtonsoft.Json.Linq.JArray))
            throw new InvalidDataException("챕터 저장 형식/버전 불일치");
        // Read the former field without creating a second policy file or rewriting player data on load.
        foreach(var story in (Newtonsoft.Json.Linq.JArray)obj["stories"])
            if(story["generationPolicyHash"]==null && story["authoredPolicyHash"]!=null)
                story["generationPolicyHash"]=story["authoredPolicyHash"].DeepClone();
        var value = JsonUtility.FromJson<NarrativeChapterSave>(obj.ToString(Newtonsoft.Json.Formatting.None));
        if (value.stories.Any(e => e == null || e.chapterId != chapter || e.targetRun < 2 || !SafeStoryPath(e.storyPath) ||
            !e.storyPath.StartsWith(chapter + "/", StringComparison.Ordinal)) ||
            value.stories.GroupBy(e => e.targetRun + ":" + e.storyPath).Any(g => g.Count() != 1) ||
            value.queues.Any(q => q == null || q.sourceRun < 1 || q.targetRun != q.sourceRun + 1 || q.jobs == null ||
                q.jobs.Any(j => j == null || j.sourceRun != q.sourceRun || j.targetRun != q.targetRun ||
                    !SafeStoryPath(j.storyPath) || !j.storyPath.StartsWith(chapter + "/", StringComparison.Ordinal))))
            throw new InvalidDataException("챕터 저장 항목 ID 불일치");
        return value;
    }
    private void WriteNarrativeChapter(NarrativeChapterSave chapter)
    {
        int current = File.Exists(ProfilePath) ? ReadJson<ProfileData>(ProfilePath)?.currentRun ?? 1 : 1;
        int cutoff = Math.Max(2, current - NarrativeRetainedRuns + 1);
        // Old automatic variants are reproducible. Human/imported content and all actual play saves are retained.
        chapter.stories.RemoveAll(e => e.origin == "Runtime" && e.targetRun < cutoff);
        chapter.queues.RemoveAll(q => q.targetRun < cutoff);
        string file = NarrativeChapterPath(chapter.chapterId);
        ParseNarrative(JsonUtility.ToJson(chapter), chapter.chapterId);
        WritePackedChapter(chapter);
    }
    private static NarrativeStoryEntry Entry(int run, string path, string json, string origin, bool enabled,bool reviewRequired=false,string validationError=null)
    {
        string[] parts = path.Split('/');
        var scene = NarrativeInteractions.Chapter(parts[0])?.scenes?.SingleOrDefault(s => s.storyPath == path);
        var root = JsonUtility.FromJson<EncounterCardRoot>(json);
        if (root?.MainStory == null || root.MainStory.Count == 0) throw new InvalidDataException("이야기 카드가 없습니다.");
        string sourceText = NarrativeInteractions.StorySource(path);
        var canonical = sourceText == null ? null : JsonUtility.FromJson<EncounterCardRoot>(sourceText);
        if (origin == "Runtime" || reviewRequired)
        {
            if(!LocalStoryRewrite.SameBindings(canonical,root))
                throw new InvalidDataException("생성 이야기의 카드/선택지 수 또는 게임 행동 연결이 원문과 다릅니다.");
            var previous = new List<EncounterCard>();
            for (int i = 0; i < canonical.MainStory.Count; i++)
            {
                if (root.MainStory[i] == null) throw new InvalidDataException("생성 카드가 비어 있습니다.");
                if(string.IsNullOrWhiteSpace(root.MainStory[i].text)||(root.MainStory[i].options??Array.Empty<string>()).Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("표시할 대사/선택지가 비어 있습니다.");
                if(reviewRequired)continue;
                if(!LocalStoryRewrite.ValidateCardWording(canonical.MainStory[i],root.MainStory[i],out string wordingError))
                    throw new InvalidDataException(wordingError);
                if (!LocalStorySections.ValidateSituation(path,canonical.MainStory[i],root.MainStory[i].text,previous,out string situationError))
                    throw new InvalidDataException(situationError);
                previous.Add(root.MainStory[i]);
            }
        }
        string npc = scene?.characterId ?? root.MainStory.FirstOrDefault(c => !string.IsNullOrEmpty(c?.speakerId))?.speakerId;
        return new NarrativeStoryEntry {
            targetRun = run, chapterId = parts[0], episodeId = parts.Length > 1 ? parts[1] : "",
            characterId = npc ?? "", sceneId = scene?.sceneId ?? string.Join("/", parts.Take(Math.Min(4, parts.Length))),
            sectionId = scene?.sectionId ?? (parts.Length <= 4 ? "opening" : string.Join("/", parts.Skip(4))),
            storyPath = path, sourceHash = LocalStoryDialogueQueue.Hash(sourceText ?? ""),
            contentHash = LocalStoryDialogueQueue.Hash(json), contentJson = json, origin = origin, enabled = enabled,
            generationPolicyHash = origin == "Runtime" ? LocalStorySections.GenerationPolicyHash(path) : "",
            reviewRequired=reviewRequired,validationError=validationError,
            updatedUtc = DateTime.UtcNow.ToString("o")
        };
    }
    private NarrativeStoryEntry FindNarrative(int run, string path)
    {
        ImportLegacyNarrative(run, path);
        return ReadNarrativeChapter(path.Split('/')[0]).stories.SingleOrDefault(e => e.targetRun == run && e.storyPath == path);
    }
    private bool SaveNarrativeContent<T>(int run, string key, T data, bool reviewed,bool reviewRequired=false,string validationError=null)
    {
        if (ReferenceEquals(data, null)) return false;
        lock (narrativeGate)
        {
            try
            {
                string path = StoryPath(key);
                var previous = FindNarrative(run, path);
                if (previous != null && string.IsNullOrEmpty(previous.preservedPackJson) && previous.contentHash != LocalStoryDialogueQueue.Hash(previous.contentJson)) return false;
                if (!reviewed && previous != null && (!previous.enabled || previous.origin == "Review")) return false;
                if(previous!=null && !string.IsNullOrEmpty(previous.preservedPackJson))
                {
                    string oldFile=Path.Combine(NpcDirectory(run,path),"Story.json");
                    if(File.Exists(oldFile)) ArchiveLegacyNarrativeFile(oldFile,false);
                    chapterPacks.Remove(path.Split('/')[0]);
                }
                var chapter = EditNarrativeChapter(path.Split('/')[0]);
                var entry = Entry(run, path, JsonUtility.ToJson(data, true), reviewed ? "Review" : "Runtime", true,reviewRequired,validationError);
                entry.previousContentJson = reviewed ? previous?.contentJson : previous?.previousContentJson;
                entry.previousReviewRequired=reviewed?previous?.reviewRequired??false:previous?.previousReviewRequired??false;
                entry.previousValidationError=reviewed?previous?.validationError:previous?.previousValidationError;
                chapter.stories.RemoveAll(e => e.targetRun == run && e.storyPath == path);
                chapter.stories.Add(entry);
                WriteNarrativeChapter(chapter);
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[Narrative] 생성 이야기 저장 실패: " + e.Message); return false; }
        }
    }
    private bool TryLoadNarrativeContent<T>(int run, string key, out T data)
    {
        data = default;
        lock (narrativeGate)
        {
            try
            {
                var entry = FindNarrative(run, StoryPath(key));
                if (entry == null || !entry.enabled || !string.IsNullOrEmpty(entry.preservedPackJson) || entry.contentHash != LocalStoryDialogueQueue.Hash(entry.contentJson)) return false;
                if(entry.reviewRequired && !LocalStoryPrompt.AllowFailedOutput)return false;
                // A prompt/settings edit affects future requests; it does not expire completed stories.
                string source = NarrativeInteractions.StorySource(entry.storyPath);
                if (source == null || entry.sourceHash != LocalStoryDialogueQueue.Hash(source)) return false;
                data = JsonUtility.FromJson<T>(entry.contentJson);
                return !ReferenceEquals(data, null);
            }
            catch (Exception e) { Debug.LogWarning("[Narrative] 생성본 대신 원문 사용: " + e.Message); return false; }
        }
    }
    public string NarrativeContentHash(int run, string path)
    {
        if (run < 2 || !SafeStoryPath(path)) return null;
        lock (narrativeGate)
        {
            var entry = FindNarrative(run, path);
            return entry == null ? null : LocalStoryDialogueQueue.Hash(entry.contentJson);
        }
    }
    public bool NarrativeRequiresReview(int run,string path,out string reason)
    {
        reason=null;if(run<2||!SafeStoryPath(path))return false;
        lock(narrativeGate){var entry=FindNarrative(run,path);reason=entry?.validationError;return entry?.reviewRequired==true;}
    }
    public bool HasNarrativeContent(int run, string path)
    {
        if (run < 2 || !SafeStoryPath(path)) return false;
        lock (narrativeGate) return FindNarrative(run, path) != null;
    }
    public bool CanReplaceNarrativeContent(int run, string path, string expectedHash)
    {
        if (run < 2 || !SafeStoryPath(path)) return false;
        lock (narrativeGate)
        {
            try
            {
                var entry = FindNarrative(run, path);
                return entry == null || (entry.enabled && entry.origin != "Review" && !string.IsNullOrEmpty(expectedHash) &&
                    entry.contentHash == expectedHash && (!string.IsNullOrEmpty(entry.preservedPackJson) || LocalStoryDialogueQueue.Hash(entry.contentJson) == expectedHash));
            }
            catch (Exception) { return false; }
        }
    }
    public bool SetNarrativeContentEnabled(int run, string path, bool enabled, bool restorePrevious = false)
    {
        if (run < 2 || !SafeStoryPath(path)) return false;
        lock (narrativeGate)
        {
            var entry = FindNarrative(run, path);
            if (entry == null) return false;
            var chapter = EditNarrativeChapter(path.Split('/')[0]);
            entry = chapter.stories.Single(e => e.targetRun == run && e.storyPath == path);
            if (restorePrevious)
            {
                if (string.IsNullOrEmpty(entry.previousContentJson)) return false;
                string current = entry.contentJson;
                entry.contentJson = entry.previousContentJson;
                entry.previousContentJson = current;
                bool currentReview=entry.reviewRequired;string currentError=entry.validationError;
                entry.reviewRequired=entry.previousReviewRequired;entry.validationError=entry.previousValidationError;
                entry.previousReviewRequired=currentReview;entry.previousValidationError=currentError;
                entry.contentHash = LocalStoryDialogueQueue.Hash(entry.contentJson);
                entry.origin = "Review";
            }
            entry.enabled = enabled;
            WriteNarrativeChapter(chapter);
            return true;
        }
    }
    public LocalStoryDialogueQueueData LoadNarrativeQueue(int run)
    {
        if (run < 1) return null;
        lock (narrativeGate)
        {
            foreach(string chapter in NarrativeChapters()) ReadNarrativeChapter(chapter); // Import old packs once.
            var queues = new List<LocalStoryDialogueQueueData>();
            var packed=ReadPackedQueue(run);if(packed!=null) queues.Add(packed);
            var legacy = LoadRunData<LocalStoryDialogueQueueData>(run, LocalStoryDialogueQueue.FileName);
            if (legacy != null) queues.Add(legacy);
            if (queues.Count == 0) return null;
            // A partial multi-chapter write must not hide the yet-unmigrated portion of an old queue.
            var combined = new LocalStoryDialogueQueueData { sourceRun = run, targetRun = run + 1, finalized = queues.All(q => q.finalized),
                jobs = queues.SelectMany(q => q.jobs).GroupBy(j => j.jobId).Select(g => g.OrderByDescending(j => j.revision).First())
                    .OrderBy(j => j.storyPath, StringComparer.Ordinal).ToList() };
            return JsonUtility.FromJson<LocalStoryDialogueQueueData>(JsonUtility.ToJson(combined));
        }
    }
    public void SaveNarrativeQueue(LocalStoryDialogueQueueData queue)
    {
        lock(narrativeGate)
        {
            var saved=JsonUtility.FromJson<LocalStoryDialogueQueueData>(JsonUtility.ToJson(queue));
            foreach(var job in saved.jobs)
            {
                if(job.snapshot?.generationTrigger=="DialogueEnded")
                {job.snapshot.playEvents=null;job.snapshot.playedCards=null;}
                var info=ChapterContentCatalog.Info(job.storyPath);
                if(info==null) {job.status="Preserved";job.error="원문이 없는 이전 조우의 작업 기록을 보존합니다.";continue;}
                job.chapterId=(string)info["chapterId"];job.episodeId=(string)info["episodeId"];
                job.sceneId=(string)info["sceneId"]??(string)info["placeId"];
                job.sectionId=(string)info["sectionId"]??(string)info["key"];
                if((job.status=="Ready" || job.status=="ReviewRequired" || job.status=="Failed") && job.snapshot!=null && !string.IsNullOrEmpty(job.reportPath) && File.Exists(job.reportPath))
                {
                    job.snapshot.playEvents=null;job.snapshot.playedCards=null;
                    job.auditArchived=true;job.sectionCheckpoint=null;job.omniSectionCheckpoint=null;
                }
                if(!LocalStoryPrompt.SaveDiagnosticReports && (job.status=="Ready" || job.status=="ReviewRequired" || job.status=="Failed"))
                {
                    job.sectionCheckpoint=null;job.omniSectionCheckpoint=null;
                    job.reportPath=null;job.auditArchived=false;
                }
            }
            WritePackedQueue(saved);
            string legacy=GetRunDataPath(queue.sourceRun,LocalStoryDialogueQueue.FileName);
            if(File.Exists(legacy)) ArchiveLegacyNarrativeFile(legacy);
        }
    }
    private string[] NarrativeChapters() => ChapterContentCatalog.Chapters.Select(c=>(string)c["id"]).ToArray();
    public int ApplySavedFailedOutputs()
    {
        if(!LocalStoryPrompt.AllowFailedOutput)return 0;
        int applied=0;
        int current=File.Exists(ProfilePath)?ReadJson<ProfileData>(ProfilePath)?.currentRun??1:1;
        foreach(int run in new[]{Math.Max(1,current-1),current}.Distinct())
        {
            var queue=LoadNarrativeQueue(run);if(queue==null)continue;bool changed=false;
            foreach(var job in queue.jobs.Where(j=>j.status=="Failed"&&!j.failedOutputChecked&&!string.IsNullOrEmpty(j.reportPath)&&File.Exists(j.reportPath)))
            {
                if(!LocalStoryDialogueQueue.CanProcess(this,job)||!LocalStoryDialogueQueue.CanReplace(this,job))continue;
                job.failedOutputChecked=true;changed=true;
                try
                {
                    var report=LocalStoryTestArchive.Load(job.reportPath);
                    var built=LocalStoryGameApply.BuildFailedPreview(report,out string warning);
                    if(!SaveGeneratedContentForReview(job.targetRun,"Encounters",job.storyPath+"/Story",built,warning))throw new IOException("실패 문구 저장 실패");
                    report.saved=true;report.runtimeStage="failed-output";report.reviewRequired=true;report.reviewError=warning;
                    LocalStoryTestArchive.RecordRuntimeApplication(Path.Combine(StorageRoot,"LocalStoryTests"),job.reportPath,report);
                    job.status="ReviewRequired";job.error=warning;job.contentHash=NarrativeContentHash(job.targetRun,job.storyPath);
                    job.generationPart="기존 실패 문구 반영 / "+warning;applied++;
                    LocalStoryLog.Write(LocalStoryLogStage.Review,job.targetRun+"회차용 / "+job.storyPath+" / 저장된 실패 문구 반영 / "+warning);
                }
                catch(Exception e) when(e is IOException||e is FormatException||e is ArgumentException||e is Newtonsoft.Json.JsonException)
                {job.error="실패 원출력은 보관, 게임 표시 불가: "+e.Message;LocalStoryLog.Write(LocalStoryLogStage.Failed,job.storyPath+" / "+job.error);}
            }
            if(changed)SaveNarrativeQueue(queue);
        }
        return applied;
    }
    public LocalStoryDialogueJob NextNarrativeJob()
    {
        ApplySavedFailedOutputs();
        int current = File.Exists(ProfilePath) ? ReadJson<ProfileData>(ProfilePath)?.currentRun ?? 1 : 1;
        // Only current and just-completed runs can produce usable work. No scan over the lifetime of Runs.
        foreach(int run in new[] {Math.Max(1,current-1),current}.Distinct())
        {
            var queue=LoadNarrativeQueue(run); if(queue==null) continue;
            bool updated=false;
            foreach(var job in queue.jobs.Where(j=>(j.status=="Pending" || j.status=="Generating") &&
                ChapterContentCatalog.ResolveStoryPath(j.storyPath,j.targetRun)!=j.storyPath))
            {
                job.status="Preserved";job.error="대상 회차는 반복 설명을 생략합니다. 기존 작업을 보관하고 회차용 원문을 사용합니다.";updated=true;
            }
            // Recover a legacy requeued job from its already stored result. Never re-run the
            // model just because the next run started or a prompt/configuration changed.
            foreach(var job in queue.jobs.Where(j=>j.status=="Pending" || j.status=="Generating"))
            {
                NarrativeStoryEntry entry;
                lock(narrativeGate) entry=FindNarrative(job.targetRun,job.storyPath);
                if(entry!=null)
                {
                    job.status=!entry.enabled || entry.origin!="Runtime" || string.IsNullOrEmpty(entry.contentJson)?"Preserved":entry.reviewRequired?"ReviewRequired":"Ready";
                    job.contentHash=entry.contentHash;job.sectionCheckpoint=null;job.auditArchived=true;
                    job.error=entry.reviewRequired?entry.validationError:null;
                    if(entry.origin=="Runtime" && !string.IsNullOrEmpty(entry.contentJson))
                        job.reportPath=FindAppliedReport(job,entry)??job.reportPath;
                    job.generationPart="저장된 결과 사용";updated=true;continue;
                }
                if(string.IsNullOrEmpty(job.reportPath) || !File.Exists(job.reportPath))continue;
                try
                {
                    var report=LocalStoryTestArchive.Load(job.reportPath);
                    if(report.storyPath!=job.storyPath || report.sourceRun!=job.sourceRun || report.targetRun!=job.targetRun)continue;
                    if(job.status=="Pending" && job.sectionCheckpoint?.accepted?.Count>0 && report.runtimeError=="생성 취소")continue;
                    if(string.IsNullOrWhiteSpace(report.rawOutput) && string.IsNullOrWhiteSpace(report.correctionRawOutput))continue;
                    // A report means this attempt already finished. Reuse/inspect it, including
                    // failures. Only an interrupted section checkpoint without a report resumes.
                    job.status="Failed";job.failedOutputChecked=false;job.sectionCheckpoint=null;
                    job.error=report.runtimeError??report.correctionError??report.error??"저장된 결과 확인";
                    job.generationPart="저장된 결과 확인";updated=true;
                }
                catch(Exception e) when(e is IOException || e is Newtonsoft.Json.JsonException || e is ArgumentException)
                {job.status="Failed";job.error=e.Message;updated=true;}
            }
            foreach(var job in queue.jobs.Where(j=>j.status=="Ready" && !string.IsNullOrEmpty(j.error)))
            {
                NarrativeStoryEntry entry;lock(narrativeGate)entry=FindNarrative(job.targetRun,job.storyPath);
                if(entry?.origin!="Runtime" || entry.reviewRequired)continue;
                job.error=null;job.reportPath=FindAppliedReport(job,entry)??job.reportPath;updated=true;
            }
            if(updated) {SaveNarrativeQueue(queue);ApplySavedFailedOutputs();queue=LoadNarrativeQueue(run);}
            var next=queue.jobs.FirstOrDefault(j=>(j.status=="Pending"||j.status=="Generating")&&LocalStoryDialogueQueue.CanProcess(this,j));
            if(next!=null) return next;
        }
        return null;
    }
    private string FindAppliedReport(LocalStoryDialogueJob job,NarrativeStoryEntry entry)
    {
        foreach(string file in LocalStoryTestArchive.List(Path.Combine(StorageRoot,"LocalStoryTests")).OrderByDescending(p=>p,StringComparer.Ordinal))
        {
            try
            {
                var report=LocalStoryTestArchive.Load(file);
                if(report.sourceRun!=job.sourceRun || report.targetRun!=job.targetRun || report.storyPath!=job.storyPath)continue;
                var canonical=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(job.storyPath));
                foreach(string raw in new[]{report.correctionRawOutput,report.rawOutput}.Where(s=>!string.IsNullOrWhiteSpace(s)))
                    if(LocalStoryRewrite.TryBuildForReview(raw,canonical,out var candidate,out _,true) &&
                        LocalStoryDialogueQueue.Hash(JsonUtility.ToJson(candidate,true))==entry.contentHash)return file;
            }
            catch(Exception e) when(e is IOException || e is Newtonsoft.Json.JsonException || e is FormatException || e is ArgumentException)
            {Debug.LogWarning("[Narrative] 결과 연결 확인 보류: "+e.Message);}
        }
        return null;
    }
    private LocalStoryRevisionRequest LoadLatestNarrativeRequest()
    {
        foreach(string chapter in NarrativeChapters()) ReadNarrativeChapter(chapter);
        var queues = Directory.Exists(GeneratedRoot)?Directory.GetFiles(GeneratedRoot,"Queue.json",SearchOption.AllDirectories)
            .Select(f=>JsonUtility.FromJson<LocalStoryDialogueQueueData>(File.ReadAllText(f))).OrderByDescending(q=>q.sourceRun).ToArray():Array.Empty<LocalStoryDialogueQueueData>();
        if (queues.Length == 0) return null;
        var queue = LoadNarrativeQueue(queues[0].sourceRun);
        var summary = queue?.jobs.LastOrDefault()?.snapshot;
        if (summary == null) return null;
        var latestJob = queue.jobs.Last();
        if (latestJob.auditArchived && !string.IsNullOrEmpty(latestJob.reportPath) && File.Exists(latestJob.reportPath))
        {
            var archived = LocalStoryTestArchive.Load(latestJob.reportPath)?.interactionMemory;
            if (archived != null) summary = archived;
        }
        summary = JsonUtility.FromJson<LocalStoryRevisionRequest>(JsonUtility.ToJson(summary));
        var played=LoadRunData<GameProgress>(summary.sourceRun,"Progress");
        summary.playEvents=played?.storyEvents;
        summary.playedCards=played?.guideProgress?.runHistory;
        summary.status = queue.jobs.Any(j => j.status == "Generating") ? "GeneratingDialogues" :
            queue.jobs.Any(j => j.status == "Pending") ? "PendingDialogues" : queue.jobs.Any(j => j.status == "Failed") ? "Failed" :
            queue.jobs.Any(j=>j.status=="ReviewRequired")?"ReviewRequired":"Ready";
        summary.generationResults = queue.jobs.Select(j => new LocalStoryRuntimeResult { storyPath = j.storyPath, status = j.status,
            error = j.error, reportPath = j.reportPath, model = j.snapshot?.npcContexts?.FirstOrDefault(c => c.characterId == j.characterId)?.model }).ToList();
        return summary;
    }
    private void ImportLegacyNarrative(int run, string path)
    {
        if (!TryGetGeneratedContentPath(run, "Encounters", path + "/Story", out string active)) return;
        if(!File.Exists(active))
        {
            string old=active.Replace("For_Run_"+run.ToString("D2"),"For_Run_"+run.ToString("D4"));
            if(File.Exists(old) || File.Exists(Path.Combine(Path.GetDirectoryName(old),"Story.disabled.json"))) active=old;
        }
        string disabled = Path.Combine(Path.GetDirectoryName(active), "Story.disabled.json");
        string file = File.Exists(disabled) ? disabled : File.Exists(active) ? active : null;
        if (file == null) return;
        string json = File.ReadAllText(file);
        var chapter = EditNarrativeChapter(path.Split('/')[0]);
        var previous = chapter.stories.SingleOrDefault(e => e.targetRun == run && e.storyPath == path);
        var imported = Entry(run, path, json, "Legacy", file == active);
        imported.previousContentJson = previous?.contentJson;
        // An old external file is a deliberate override. It never becomes runtime-owned merely by importing.
        chapter.stories.RemoveAll(e => e.targetRun == run && e.storyPath == path);
        chapter.stories.Add(imported);
        WriteNarrativeChapter(chapter);
        var verified = ReadNarrativeChapter(chapter.chapterId)
            .stories.Single(e => e.targetRun == run && e.storyPath == path);
        var expected=UnpackEntry(PackEntry(imported));
        if (verified.contentHash != expected.contentHash || verified.contentHash != LocalStoryDialogueQueue.Hash(verified.contentJson) ||
            verified.enabled != imported.enabled) throw new IOException("기존 생성본 이관 검증 실패");
        ArchiveLegacyNarrativeFile(file);
        // If both forms existed, preserve the other bytes as well; disabled remains the effective state.
        if (file == disabled && File.Exists(active)) ArchiveLegacyNarrativeFile(active);
    }
    private void ArchiveLegacyNarrativeFile(string file,bool remove=true)
    {
        string root = Path.GetFullPath(BasePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(file);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("이관 경로가 저장소 밖에 있습니다.");
        byte[] bytes = File.ReadAllBytes(full);
        string hash = LocalStoryDialogueQueue.Hash(Convert.ToBase64String(bytes));
        string name = full.Substring(root.Length).Replace('\\', '/') + "." + hash;
        string archive = Path.Combine(BasePath, "Migrations", "LegacyContent.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archive));
        using (var stream = new FileStream(archive, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            if (zip.GetEntry(name) == null)
            {
                using var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal).Open();
                entry.Write(bytes, 0, bytes.Length);
            }
        }
        using (var stream = File.OpenRead(archive))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        using (var copy = new MemoryStream())
        {
            using (var entry = zip.GetEntry(name).Open()) entry.CopyTo(copy);
            if (!bytes.SequenceEqual(copy.ToArray()) || !bytes.SequenceEqual(File.ReadAllBytes(full)))
                throw new IOException("기존 파일 보관 검증 실패. 원본을 보존합니다.");
        }
        if(remove) File.Delete(full);
    }
}
