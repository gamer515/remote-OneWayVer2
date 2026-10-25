using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class SaveIOService
{
    private readonly Dictionary<string,(string stamp,NarrativeChapterSave value)> chapterPacks = new Dictionary<string,(string,NarrativeChapterSave)>();
    private readonly HashSet<string> migratedChapters = new HashSet<string>();
    private string GeneratedRoot => Path.Combine(BasePath,"GeneratedContent");
    internal void ArchiveMigratedFile(string file) => ArchiveLegacyNarrativeFile(file);
    private bool layoutReady;
    private void EnsureChapterLayout()
    {
        if(layoutReady) return;
        string runs=Path.Combine(BasePath,"Runs");
        if(Directory.Exists(runs)) foreach(string dir in Directory.GetDirectories(runs,"Run_*"))
            if(int.TryParse(Path.GetFileName(dir).Substring(4),out int run) && run>0)
            {
                string moved=EnsureRunDirectory(run);
                foreach(string packet in Directory.GetFiles(moved,"LocalPacket_*.json")) ArchiveLegacyNarrativeFile(packet);
            }
        foreach(var c in ChapterContentCatalog.Chapters) ReadNarrativeChapter((string)c["id"]);
        if(Directory.Exists(GeneratedRoot))
            foreach(string dir in Directory.GetDirectories(GeneratedRoot,"For_Run_*"))
            {
                if(!int.TryParse(Path.GetFileName(dir).Substring(8),out int target) || target<2) continue;
                string loose=Path.Combine(dir,"Encounters");
                if(Directory.Exists(loose))
                    foreach(string file in Directory.GetFiles(loose,"Story*.json",SearchOption.AllDirectories))
                    {
                        string path=Path.GetRelativePath(loose,Path.GetDirectoryName(file)).Replace('\\','/');
                        if(ChapterContentCatalog.Info(path)!=null) ImportLegacyNarrative(target,path);
                        else ArchiveLegacyNarrativeFile(file);
                    }
            }
        if(Directory.Exists(runs)) foreach(string dir in Directory.GetDirectories(runs,"Run_*"))
            if(int.TryParse(Path.GetFileName(dir).Substring(4),out int run) && File.Exists(Path.Combine(dir,LocalStoryDialogueQueue.FileName+".json")))
            {var queue=LoadNarrativeQueue(run);if(queue!=null) SaveNarrativeQueue(queue);}
        LocalStoryTestArchive.MigrateGrouped(Path.Combine(BasePath,"LocalStoryTests"));
        layoutReady=true;
    }
    private string EnsureRunDirectory(int run)
    {
        string root=Path.GetFullPath(Path.Combine(BasePath,"Runs"));
        string target=Path.Combine(root,$"Run_{run:D2}"),old=Path.Combine(root,$"Run_{run:D4}");
        if(!target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !old.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new IOException("잘못된 회차 경로");
        if(target!=old && Directory.Exists(old))
        {
            if(!Directory.Exists(target)) Directory.Move(old,target);
            else
            {
                // An interrupted/manual migration can leave both names. Never choose silently between conflicting records.
                foreach(string file in Directory.GetFiles(old))
                {
                    string copy=Path.Combine(target,Path.GetFileName(file));
                    if(File.Exists(copy) && !File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(copy))) throw new IOException("회차 저장 충돌: "+file);
                    if(!File.Exists(copy)) File.Copy(file,copy);
                }
                if(!Directory.EnumerateDirectories(old).Any())
                {foreach(string file in Directory.GetFiles(old)) File.Delete(file);Directory.Delete(old);}
            }
        }
        return target;
    }
    private string QueuePath(int sourceRun) => Path.Combine(GeneratedRoot,$"For_Run_{sourceRun+1:D2}","Queue.json");
    private string NpcDirectory(int targetRun,string path)
    {
        var info=ChapterContentCatalog.Info(path);
        if(info==null) throw new InvalidDataException("등록되지 않은 생성 이야기: "+path);
        return Path.Combine(GeneratedRoot,$"For_Run_{targetRun:D2}",(string)info["chapterFolder"],
            ChapterContentCatalog.EpisodeFolder((string)info["episodeId"]),(string)info["characterId"]);
    }
    public string GeneratedNpcDirectory(int targetRun,string path) => NpcDirectory(targetRun,path);
    private static void WriteDocument(string path,JToken document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp=path+".tmp"; File.WriteAllText(temp,document.ToString(Newtonsoft.Json.Formatting.Indented));
        if(File.Exists(path))
            for(int attempt=0;;attempt++)
            {try {File.Replace(temp,path,null);break;}catch(IOException) when(attempt<4) {System.Threading.Thread.Sleep(10<<attempt);}}
        else File.Move(temp,path);
    }
    private string[] PackedStoryFiles(string chapter)
    {
        if(!Directory.Exists(GeneratedRoot)) return Array.Empty<string>();
        string folder=ChapterContentCatalog.ChapterFolder(chapter);
        return Directory.GetDirectories(GeneratedRoot,"For_Run_*").Select(d=>Path.Combine(d,folder))
            .Where(Directory.Exists).SelectMany(d=>Directory.GetFiles(d,"Story.json",SearchOption.AllDirectories))
            .OrderBy(p=>p,StringComparer.Ordinal).ToArray();
    }
    private static EncounterCardRoot Rebase(string path,string json)
    {
        var source=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
        var old=JsonUtility.FromJson<EncounterCardRoot>(json);
        if(source?.MainStory?.Count!=old?.MainStory?.Count) throw new InvalidDataException("원문 카드 수 변경: "+path);
        // Stable identifiers added during the source migration do not change executable behavior.
        for(int i=0;i<source.MainStory.Count;i++)
        {
            var a=source.MainStory[i];var b=old.MainStory[i];
            if(string.IsNullOrEmpty(b.speakerId)) b.speakerId=a.speakerId;
            if(a.choiceActions?.Length==b.choiceActions?.Length)
                for(int j=0;j<(a.choiceActions?.Length??0);j++)
                    if(string.IsNullOrEmpty(b.choiceActions[j].choiceId)) b.choiceActions[j].choiceId=a.choiceActions[j].choiceId;
        }
        if(!LocalStoryRewrite.SameBindings(source,old)) throw new InvalidDataException("기존 생성본의 실행 연결 불일치: "+path);
        for(int i=0;i<source.MainStory.Count;i++) {source.MainStory[i].text=old.MainStory[i].text;source.MainStory[i].options=old.MainStory[i].options?.Length>0?old.MainStory[i].options:null;}
        return source;
    }
    private static JArray Changes(string path,EncounterCardRoot content)
    {
        var original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
        var result=new JArray();
        var info=ChapterContentCatalog.Info(path);
        for(int i=0;i<original.MainStory.Count;i++)
        {
            var a=original.MainStory[i];var b=content.MainStory[i];
            var change=new JObject { ["ID"]=ChapterContentCatalog.CardId(path,i) };
            if(a.text!=b.text) change["Text"]=b.text;
            if(!JToken.DeepEquals(a.options==null?null:JArray.FromObject(a.options),b.options==null?null:JArray.FromObject(b.options)))
                change["options"]=b.options==null?JValue.CreateNull():JArray.FromObject(b.options);
            if(change.Count>1) result.Add(change);
        }
        return result;
    }
    private static string ApplyChanges(string path,JArray changes)
    {
        var original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
        var map=Enumerable.Range(0,original.MainStory.Count).ToDictionary(i=>ChapterContentCatalog.CardId(path,i));
        var seen=new HashSet<int>();
        foreach(JObject change in changes??new JArray())
        {
            if(change["ID"]?.Type!=JTokenType.Integer || !map.TryGetValue((int)change["ID"],out int i) || !seen.Add(i) ||
                change.Properties().Any(p=>p.Name!="ID" && p.Name!="Text" && p.Name!="options")) throw new InvalidDataException("생성 문장 ID/속성 오류: "+path);
            if(change["Text"]!=null) original.MainStory[i].text=(string)change["Text"];
            if(change["options"]!=null) original.MainStory[i].options=change["options"].Type==JTokenType.Null?null:change["options"].ToObject<string[]>();
        }
        return JsonUtility.ToJson(original,true);
    }
    private static JObject PackEntry(NarrativeStoryEntry entry)
    {
        if(!string.IsNullOrEmpty(entry.preservedPackJson))
        {
            var preserved=JObject.Parse(entry.preservedPackJson);preserved["enabled"]=entry.enabled;preserved["origin"]=entry.origin;return preserved;
        }
        var doc=JObject.Parse(JsonUtility.ToJson(entry));
        doc.Remove("contentJson");doc.Remove("previousContentJson");doc.Remove("preservedPackJson");
        try
        {
            var root=Rebase(entry.storyPath,entry.contentJson);
            string json=JsonUtility.ToJson(root,true);
            doc["changes"]=Changes(entry.storyPath,root);
            doc["sourceHash"]=LocalStoryDialogueQueue.Hash(NarrativeInteractions.StorySource(entry.storyPath));
            doc["contentHash"]=LocalStoryDialogueQueue.Hash(json);
            if(!string.IsNullOrEmpty(entry.previousContentJson)) doc["previousChanges"]=Changes(entry.storyPath,Rebase(entry.storyPath,entry.previousContentJson));
        }
        catch(Exception e) when(e is InvalidDataException || e is ArgumentException)
        {
            // Preserve incompatible historical/manual bytes. Runtime validation will decline to apply them.
            doc["legacyContentJson"]=entry.contentJson;doc["legacyPreviousContentJson"]=entry.previousContentJson;
        }
        return doc;
    }
    private static NarrativeStoryEntry UnpackEntry(JObject doc)
    {
        if(doc["generationPolicyHash"]==null && doc["authoredPolicyHash"]!=null) doc["generationPolicyHash"]=doc["authoredPolicyHash"].DeepClone();
        var entry=JsonUtility.FromJson<NarrativeStoryEntry>(doc.ToString());
        if(doc["legacyContentJson"]!=null) {entry.contentJson=(string)doc["legacyContentJson"];entry.previousContentJson=(string)doc["legacyPreviousContentJson"];return entry;}
        string source=NarrativeInteractions.StorySource(entry.storyPath);
        if(source==null || entry.sourceHash!=LocalStoryDialogueQueue.Hash(source))
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(entry.sourceHash??"",@"^[a-f0-9]{64}$")) throw new InvalidDataException("원문 해시 형식 오류: "+entry.storyPath);
            entry.contentJson=null;entry.preservedPackJson=doc.ToString();return entry;
        }
        entry.contentJson=ApplyChanges(entry.storyPath,(JArray)doc["changes"]);
        if(LocalStoryDialogueQueue.Hash(entry.contentJson)!=entry.contentHash) throw new InvalidDataException("생성 변경분 해시 불일치: "+entry.storyPath);
        if(doc["previousChanges"] is JArray previous) entry.previousContentJson=ApplyChanges(entry.storyPath,previous);
        return entry;
    }
    private NarrativeChapterSave ReadPackedChapter(string chapter)
    {
        if(!migratedChapters.Contains(chapter))
        {
            string old=Path.Combine(BasePath,"Narrative","Chapters",chapter+".json");
            if(File.Exists(old))
            {
                var legacy=ParseNarrative(File.ReadAllText(old),chapter);
                // New packs win after an interrupted migration; do not overwrite newer player edits.
                if(PackedStoryFiles(chapter).Length==0) WritePackedChapter(legacy);
                else
                {
                    var existing=PackedStoryFiles(chapter).SelectMany(f=>((JArray)JObject.Parse(File.ReadAllText(f))["stories"]).OfType<JObject>().Select(UnpackEntry)).ToList();
                    foreach(var e in legacy.stories.Where(e=>!existing.Any(n=>n.targetRun==e.targetRun && n.storyPath==e.storyPath))) existing.Add(e);
                    legacy.stories=existing;WritePackedChapter(legacy);
                }
                var ids=PackedStoryFiles(chapter).SelectMany(f=>((JArray)JObject.Parse(File.ReadAllText(f))["stories"]).OfType<JObject>()).ToArray();
                if(legacy.stories.Where(e=>ChapterContentCatalog.Info(e.storyPath)!=null).Any(e=>!ids.Any(n=>(int)n["targetRun"]==e.targetRun && (string)n["storyPath"]==e.storyPath))) throw new IOException("생성본 이관 확인 실패");
                ArchiveLegacyNarrativeFile(old);
            }
            migratedChapters.Add(chapter);
        }
        var files=PackedStoryFiles(chapter);
        string stamp=string.Join(";",files.Select(f=>f+":"+new FileInfo(f).Length+":"+File.GetLastWriteTimeUtc(f).Ticks));
        if(chapterPacks.TryGetValue(chapter,out var cache) && cache.stamp==stamp) return cache.value;
        var result=new NarrativeChapterSave {chapterId=chapter};
        foreach(string f in files)
        {
            var doc=JObject.Parse(File.ReadAllText(f));
            if((int?)doc["schemaVersion"]!=2 || (string)doc["chapterId"]!=chapter) throw new InvalidDataException("생성 묶음 형식 오류: "+f);
            result.stories.AddRange(((JArray)doc["stories"]).OfType<JObject>().Select(UnpackEntry));
        }
        chapterPacks[chapter]=(stamp,result);return result;
    }
    private void WritePackedChapter(NarrativeChapterSave chapter)
    {
        var written=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var group in chapter.stories.Where(e=>ChapterContentCatalog.Info(e.storyPath)!=null).GroupBy(e=>NpcDirectory(e.targetRun,e.storyPath)))
        {
            string file=Path.Combine(group.Key,"Story.json");written.Add(file);
            var entries=new JArray(group.Select(PackEntry));
            WriteDocument(file,new JObject { ["schemaVersion"]=2,["chapterId"]=chapter.chapterId,["stories"]=entries });
            foreach(JObject e in entries) UnpackEntry(e); // Read-back validation before retiring any legacy source.
            WriteFeatures(group.Key,group.First().targetRun,group.First().characterId,entries,null);
        }
        foreach(string file in PackedStoryFiles(chapter.chapterId).Where(f=>!written.Contains(f)))
        {
            File.Delete(file);string feature=Path.Combine(Path.GetDirectoryName(file),"Features.json");if(File.Exists(feature)) File.Delete(feature);
        }
        foreach(var queue in chapter.queues) WritePackedQueue(queue);
        chapterPacks.Remove(chapter.chapterId);
    }
    private void WriteFeatures(string directory,int targetRun,string npc,JArray entries,LocalStoryDialogueQueueData queue)
    {
        queue??=ReadPackedQueue(targetRun-1);
        var jobs=(queue?.jobs??new List<LocalStoryDialogueJob>()).Where(j=>j.characterId==npc && NpcDirectory(targetRun,j.storyPath)==directory).ToArray();
        var events=jobs.SelectMany(j=>j.snapshot?.playEvents??new List<StoryEventRecord>()).Where(e=>e.relationshipId==npc && e.kind=="choice").GroupBy(e=>e.eventKey).Select(g=>g.First()).ToArray();
        var memories=jobs.SelectMany(j=>j.snapshot?.dreamMemories??new List<LocalStoryDreamMemory>()).Where(m=>m.characterId==npc).Select(m=>m.impression).Distinct().ToArray();
        WriteDocument(Path.Combine(directory,"Features.json"),new JObject {
            ["NPC_ID"]=npc,["targetRun"]=targetRun,["relationshipValue"]=jobs.LastOrDefault()?.relationshipValue??0,
            ["relationshipChange"]=jobs.LastOrDefault()?.relationshipChange??events.Sum(e=>e.relationshipDelta),
            ["speechStyle"]=(string)ChapterContentCatalog.CharacterData(npc)?["speechStyle"]??"",
            ["impressions"]=JArray.FromObject(memories),
            ["unlocks"]=JArray.FromObject(jobs.SelectMany(j=>j.snapshot?.earnedUnlocks??Array.Empty<string>()).Distinct()),
            ["stories"]=new JArray(jobs.Select(j=>new JObject { ["storyPath"]=j.storyPath,["chunkIndex"]=(int?)ChapterContentCatalog.Info(j.storyPath)?["chunkIndex"],
                ["status"]=j.status,["additionalContent"]=j.snapshot?.reason??"" })) });
    }
    private LocalStoryDialogueQueueData ReadPackedQueue(int run)
    {
        string file=QueuePath(run); return File.Exists(file)?JsonUtility.FromJson<LocalStoryDialogueQueueData>(File.ReadAllText(file)):null;
    }
    private void WritePackedQueue(LocalStoryDialogueQueueData queue)
    {
        var existing=ReadPackedQueue(queue.sourceRun);
        var combined=existing==null?queue:JsonUtility.FromJson<LocalStoryDialogueQueueData>(JsonUtility.ToJson(existing));
        if(existing!=null)
        {
            combined.finalized=queue.finalized;
            foreach(var job in queue.jobs) {combined.jobs.RemoveAll(j=>j.jobId==job.jobId);combined.jobs.Add(job);}
        }
        WriteJsonAtomically(QueuePath(queue.sourceRun),combined);
        foreach(var group in combined.jobs.Where(j=>ChapterContentCatalog.Info(j.storyPath)!=null).GroupBy(j=>NpcDirectory(j.targetRun,j.storyPath)))
            WriteFeatures(group.Key,combined.targetRun,group.First().characterId,null,combined);
        PruneGenerated();
    }
    private void PruneGenerated()
    {
        int current=File.Exists(ProfilePath)?ReadJson<ProfileData>(ProfilePath)?.currentRun??1:1;
        int cutoff=Math.Max(2,current-NarrativeRetainedRuns+1);
        if(!Directory.Exists(GeneratedRoot)) return;
        foreach(string dir in Directory.GetDirectories(GeneratedRoot,"For_Run_*"))
        {
            if(!int.TryParse(Path.GetFileName(dir).Substring(8),out int target) || target>=cutoff) continue;
            foreach(string story in Directory.GetFiles(dir,"Story.json",SearchOption.AllDirectories))
            {
                var doc=JObject.Parse(File.ReadAllText(story));if((int?)doc["schemaVersion"]!=2) continue;
                var entries=(JArray)doc["stories"];
                foreach(var entry in entries.Where(e=>(string)e["origin"]=="Runtime").ToArray()) entry.Remove();
                if(entries.Count>0) WriteDocument(story,doc);else File.Delete(story);
            }
            string queue=Path.Combine(dir,"Queue.json");if(File.Exists(queue)) File.Delete(queue);
            foreach(string features in Directory.GetFiles(dir,"Features.json",SearchOption.AllDirectories))
                if(!File.Exists(Path.Combine(Path.GetDirectoryName(features),"Story.json"))) File.Delete(features);
            foreach(string empty in Directory.GetDirectories(dir,"*",SearchOption.AllDirectories).OrderByDescending(p=>p.Length))
                if(!Directory.EnumerateFileSystemEntries(empty).Any()) Directory.Delete(empty);
            if(!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        chapterPacks.Clear();
    }
}
