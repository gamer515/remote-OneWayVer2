using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[Serializable] public sealed class LocalStoryDreamRule
{
    public string ruleId, characterId, choiceId, kind, result, impression, choiceRole;
    public string[] requiresUnlocks;
    public bool enabled, runCompletedOnly, requireRelationshipChoice;
}
[Serializable] public sealed class LocalStoryDreamRules { public LocalStoryDreamRule[] rules; }
[Serializable] public sealed class LocalStoryDreamMemory
{
    public string ruleId, characterId, eventKey, impression;
    public int sourceRun;
}
[Serializable] public sealed class LocalStoryDialogueJob
{
    public string jobId, characterId, storyPath, inputHash, contentHash, error, reportPath;
    public string status="Pending";
    public int sourceRun, targetRun, revision;
    public double seconds;
    public LocalStoryRevisionRequest snapshot;
}
[Serializable] public sealed class LocalStoryDialogueQueueData
{
    public int sourceRun, targetRun;
    public bool finalized;
    public List<LocalStoryDialogueJob> jobs=new List<LocalStoryDialogueJob>();
}

/// <summary>Persistent next-run work. No in-progress dialogue replacement and no implicit memory promotion.</summary>
public static class LocalStoryDialogueQueue
{
    public const string FileName="LocalStoryDialogueQueue";
    public static string ChoiceRole(int slot) => StoryChoiceRoles.Expected(slot);
    public static string Hash(string text)
    {
        using var sha=SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
    }
    public static string ContentPath(SaveIOService store,LocalStoryDialogueJob job) =>
        Path.Combine(store.StorageRoot,"GeneratedContent","For_Run_"+job.targetRun.ToString("D4"),"Encounters",job.storyPath.Replace('/',Path.DirectorySeparatorChar),"Story.json");
    public static LocalStoryDialogueQueueData Load(SaveIOService store,int run) =>
        store.LoadRunData<LocalStoryDialogueQueueData>(run,FileName);
    public static void Save(SaveIOService store,LocalStoryDialogueQueueData queue)
    {
        store.SaveRunCheckpoint(queue.sourceRun,FileName,queue);
        var summary=queue.jobs.LastOrDefault()?.snapshot;
        if(summary==null) return;
        summary=JsonUtility.FromJson<LocalStoryRevisionRequest>(JsonUtility.ToJson(summary));
        summary.status=queue.jobs.Any(j=>j.status=="Generating")?"GeneratingDialogues":
            queue.jobs.Any(j=>j.status=="Pending")?"PendingDialogues":
            queue.jobs.Any(j=>j.status=="Failed")?"Failed":"Ready";
        summary.generationResults=queue.jobs.Select(j=>new LocalStoryRuntimeResult {
            storyPath=j.storyPath,model=j.snapshot.npcContexts.FirstOrDefault(c=>c.characterId==j.characterId)?.model,
            status=j.status,error=j.error,reportPath=j.reportPath }).ToList();
        summary.generationError=string.Join(" / ",queue.jobs.Where(j=>j.status=="Failed").Select(j=>j.error));
        store.SaveRunCheckpoint(queue.sourceRun,"LocalStoryRevisionRequest",summary);
    }
    public static List<LocalStoryDreamMemory> SelectDreamMemories(SaveIOService store,int throughRun,
        LocalStoryDreamRule[] rules)
    {
        var result=new List<LocalStoryDreamMemory>();
        foreach(var rule in rules??Array.Empty<LocalStoryDreamRule>())
        {
            if(!rule.enabled) continue;
            if(string.IsNullOrWhiteSpace(rule.ruleId) || string.IsNullOrWhiteSpace(rule.characterId) ||
                string.IsNullOrWhiteSpace(rule.impression) ||
                (string.IsNullOrWhiteSpace(rule.choiceId) && string.IsNullOrWhiteSpace(rule.kind)))
                throw new InvalidDataException("꿈 기억 규칙에는 ID/NPC/인상과 실제 선택ID 또는 사건종류가 필요합니다.");
            for(int run=1;run<=throughRun;run++)
            {
                var progress=store.LoadRunData<GameProgress>(run,"Progress");
                if(progress==null || (run<throughRun && !progress.isCompleted) ||
                    (rule.runCompletedOnly && !progress.isCompleted)) continue;
                if((rule.requiresUnlocks??Array.Empty<string>()).Any(id =>
                    string.IsNullOrWhiteSpace(id) || !(progress.unlockedIds??new List<string>()).Contains(id))) continue;
                foreach(var e in progress.storyEvents??new List<StoryEventRecord>())
                {
                    if((rule.characterId!="*" && e.characterId!=rule.characterId) || (!string.IsNullOrEmpty(rule.choiceId) && e.choiceId!=rule.choiceId) ||
                        (!string.IsNullOrEmpty(rule.kind) && e.kind!=rule.kind) ||
                        (!string.IsNullOrEmpty(rule.result) && e.reason!=rule.result)) continue;
                    if(rule.requireRelationshipChoice && string.IsNullOrEmpty(e.relationshipId)) continue;
                    string role=StoryChoiceRoles.ForMemory(e);
                    if(!string.IsNullOrEmpty(rule.choiceRole) && role!=rule.choiceRole) continue;
                    result.Add(new LocalStoryDreamMemory {ruleId=rule.ruleId,characterId=e.characterId,
                        sourceRun=run,eventKey=e.eventKey,impression=rule.impression});
                }
            }
        }
        return result.GroupBy(m=>m.sourceRun+":"+m.ruleId+":"+m.eventKey).Select(g=>g.First()).ToList();
    }
    private static LocalStoryDreamRule[] Rules()
    {
        var asset=Resources.Load<TextAsset>("LocalStory/DreamMemoryRules");
        return asset==null?Array.Empty<LocalStoryDreamRule>():
            JsonUtility.FromJson<LocalStoryDreamRules>(asset.text)?.rules??Array.Empty<LocalStoryDreamRule>();
    }
    public static void EncounterEnded(SaveIOService store,int run,string placeId,string npc,string basePath)
    {
        var progress=store.LoadRunData<GameProgress>(run,"Progress");
        // The hook is after the completed encounter checkpoint. Opening a dialogue alone is not enough.
        if(progress==null || !(progress.unlockedIds??new List<string>()).Contains("event:"+placeId)) return;
        var paths=(progress.guideProgress?.runHistory??new List<PlayedEncounterCardRecord>())
            .Where(c=>c.placeId==placeId && (c.encounterPath==basePath || c.encounterPath.StartsWith(basePath+"/",StringComparison.Ordinal)))
            .Select(c=>c.encounterPath).Distinct().ToArray();
        QueuePaths(store,run,npc,paths,"대화 종료: "+placeId);
    }
    public static void FinalizeRun(SaveIOService store,int run)
    {
        var progress=store.LoadRunData<GameProgress>(run,"Progress");
        if(progress?.isCompleted!=true) return;
        // Death/run termination can end the active conversation without the normal Explore checkpoint.
        // Only enqueue actually displayed paths of that active NPC, never unseen stories.
        if(!string.IsNullOrEmpty(progress.activePlaceId))
        {
            var displayed=(progress.guideProgress?.runHistory??new List<PlayedEncounterCardRecord>())
                .Where(c=>c.placeId==progress.activePlaceId && c.card!=null && !string.IsNullOrEmpty(c.card.speakerId));
            foreach(var group in displayed.GroupBy(c=>c.card.speakerId))
                QueuePaths(store,run,group.Key,group.Select(c=>c.encounterPath).Distinct().ToArray(),"회차 종료로 대화 마침");
        }
        var queue=Load(store,run);
        if(queue==null) return; // No fabricated encounters / legacy saves are not retroactively played.
        foreach(var group in queue.jobs.GroupBy(j=>j.characterId).ToArray())
            QueuePaths(store,run,group.Key,group.Select(j=>j.storyPath).ToArray(),"회차 종료 조건 확정");
        queue=Load(store,run); queue.finalized=true; Save(store,queue);
    }
    public static void QueuePaths(SaveIOService store,int run,string npc,string[] paths,string reason)
    {
        var progress=store.LoadRunData<GameProgress>(run,"Progress");
        string profile=Path.Combine(store.StorageRoot,"Profile.json");
        if(progress==null || !File.Exists(profile) ||
            JsonUtility.FromJson<ProfileData>(File.ReadAllText(profile)).currentRun!=run) return;
        string[] earned=LocalStoryNotebook.EarnedUnlocks(store,run);
        var contexts=LocalStoryNotebook.Contexts(store,earned);
        var person=contexts.FirstOrDefault(c=>c.characterId==npc && c.autoGenerate);
        if(person==null)
        {
            LocalStoryLog.Write(LocalStoryLogStage.Deferred,npc+" / 자동생성 설정 꺼짐. 플레이 기록만 유지.");
            return;
        }
        var dreams=SelectDreamMemories(store,run,Rules()).Where(m=>m.characterId==npc).ToList();
        var queue=Load(store,run)??new LocalStoryDialogueQueueData {sourceRun=run,targetRun=run+1};
        foreach(string path in paths??Array.Empty<string>())
        {
            var asset=Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story");
            if(!LocalStoryNotebook.IsStoryPath(path) || asset==null || path.Contains("/Variants/")) continue;
            var original=JsonUtility.FromJson<EncounterCardRoot>(asset.text);
            if(original?.MainStory==null || original.MainStory.Count==0 ||
                !original.MainStory.Any(c=>c.speakerId==npc)) continue;
            // Played cards are evidence/audit only. They are not all NPC memories or a model-written answer.
            var request=new LocalStoryRevisionRequest {
                sourceRun=run,targetRun=run+1,characterId=npc,generationTrigger="DialogueEnded",memoryMode="Dream",
                chapterId=path.Split('/')[0],scenarioPaths=new[]{string.Join("/",path.Split('/').Take(2))},
                reason=reason,finalStats=store.LoadRunData<PlayerStats>(run,"Stats")?.stats,
                events=new List<StoryEventRecord>(),playEvents=progress.storyEvents??new List<StoryEventRecord>(),
                dreamMemories=dreams,earnedUnlocks=earned,npcContexts=contexts,
                playedCards=progress.guideProgress?.runHistory??new List<PlayedEncounterCardRecord>()
            };
            string hash=Hash(asset.text+"\n"+JsonUtility.ToJson(person)+"\n"+JsonUtility.ToJson(new DreamEnvelope {memories=dreams}));
            var job=queue.jobs.FirstOrDefault(j=>j.storyPath==path);
            if(job!=null && job.inputHash==hash) continue; // Mere extra encounters are not cross-run memory changes.
            if(job==null) { job=new LocalStoryDialogueJob {jobId=path,storyPath=path,characterId=npc,sourceRun=run,targetRun=run+1}; queue.jobs.Add(job); }
            job.revision++; job.inputHash=hash; job.status="Pending"; job.error=null;
            request.dialogueRevision=job.revision; job.snapshot=request;
            LocalStoryLog.Write(LocalStoryLogStage.Request,run+"회차 "+npc+" 대화 종료 → "+(run+1)+
                "회차용 / "+path+" / 개정 "+job.revision+" / 전달할 꿈 인상 "+dreams.Count+"개");
        }
        Save(store,queue);
    }
    [Serializable] private sealed class DreamEnvelope { public List<LocalStoryDreamMemory> memories; }
    public static bool CanProcess(SaveIOService store,LocalStoryDialogueJob job)
    {
        string profile=Path.Combine(store.StorageRoot,"Profile.json");
        if(job==null || job.sourceRun<1 || job.targetRun!=job.sourceRun+1 || !File.Exists(profile)) return false;
        int current=JsonUtility.FromJson<ProfileData>(File.ReadAllText(profile)).currentRun;
        var progress=store.LoadRunData<GameProgress>(job.sourceRun,"Progress");
        bool validRun=current==job.sourceRun || (current==job.targetRun && progress?.isCompleted==true);
        var latest=Load(store,job.sourceRun)?.jobs.FirstOrDefault(j=>j.jobId==job.jobId);
        return validRun && progress!=null && latest?.revision==job.revision && latest.inputHash==job.inputHash;
    }
    public static bool CanReplace(SaveIOService store,LocalStoryDialogueJob job)
    {
        string file=ContentPath(store,job);
        if(File.Exists(Path.Combine(Path.GetDirectoryName(file),"Story.disabled.json"))) return false;
        return !File.Exists(file) || (!string.IsNullOrEmpty(job.contentHash) && Hash(File.ReadAllText(file))==job.contentHash);
    }
    public static LocalStoryDialogueJob Next(SaveIOService store)
    {
        string root=Path.Combine(store.StorageRoot,"Runs");
        if(!Directory.Exists(root)) return null;
        foreach(string file in Directory.GetFiles(root,FileName+".json",SearchOption.AllDirectories).OrderBy(p=>p))
        {
            var q=JsonUtility.FromJson<LocalStoryDialogueQueueData>(File.ReadAllText(file));
            foreach(var job in q.jobs.Where(j=>j.status=="Pending" || j.status=="Generating"))
                if(CanProcess(store,job)) return job;
        }
        return null;
    }
    public static bool BlocksRun(SaveIOService store,int currentRun)
    {
        var q=Load(store,currentRun-1);
        return q?.jobs.Any(j=>j.targetRun==currentRun && (j.status=="Pending" || j.status=="Generating"))==true;
    }
    public static async Task Process(SaveIOService store,LocalStoryDialogueJob job,
        Func<LocalStoryTestReport,CancellationToken,Task> generate,CancellationToken token)
    {
        if(!CanProcess(store,job)) return;
        var queue=Load(store,job.sourceRun);
        var latest=queue.jobs.First(j=>j.jobId==job.jobId);
        if(!CanReplace(store,latest))
        {
            latest.status="Preserved"; latest.error="수동 적용/수정 파일 또는 disabled 복구 보존"; Save(store,queue);
            LocalStoryLog.Write(LocalStoryLogStage.Deferred,job.characterId+" / "+latest.error); return;
        }
        latest.status="Generating"; Save(store,queue);
        job=JsonUtility.FromJson<LocalStoryDialogueJob>(JsonUtility.ToJson(latest));
        LocalStoryRuntimeResult result=null;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var npc=job.snapshot.npcContexts.First(c=>c.characterId==job.characterId);
            result=await LocalStoryRuntimePipeline.ProduceDialogue(job.snapshot,npc,job.storyPath,store,generate,token,
                ()=>CanProcess(store,job) && CanReplace(store,job));
        }
        finally
        {
            queue=Load(store,job.sourceRun); latest=queue.jobs.First(j=>j.jobId==job.jobId);
            // Newer inputs win; an old in-flight job never marks the newer request completed.
            if(latest.revision==job.revision)
            {
                latest.status=token.IsCancellationRequested?"Pending":result?.status??"Failed";
                latest.error=token.IsCancellationRequested?"중단됨 — 다음 실행에서 재개":result?.error;
                latest.reportPath=result?.reportPath; latest.seconds=watch.Elapsed.TotalSeconds;
                if(latest.status=="Ready") latest.contentHash=Hash(File.ReadAllText(ContentPath(store,latest)));
                Save(store,queue);
            }
        }
    }
}
