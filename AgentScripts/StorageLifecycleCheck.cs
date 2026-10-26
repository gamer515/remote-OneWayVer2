using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class StorageLifecycleCheck
{
    const string Guide="Initial/Initial_01/Encounters/guide";
    static SaveIOService Store(string root)=>(SaveIOService)Activator.CreateInstance(typeof(SaveIOService),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{root},null);
    static EncounterCardRoot Source()=>JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(Guide));
    static string Patch(EncounterCardRoot source)=>new JObject{["cards"]=new JArray(source.MainStory.Select((c,i)=>new JObject{["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}))}.ToString(Formatting.None);
    static GameProgress Progress()
    {
        var p=new GameProgress();p.unlockedIds.Add("event:fixture");
        p.guideProgress.runHistory.Add(new PlayedEncounterCardRecord{placeId="fixture",encounterPath=Guide});
        p.storyEvents.Add(new StoryEventRecord{eventKey="actual:1",kind="choice",chapterId="Initial",characterId="Guide",choiceId="loki_insult",relationshipId="Guide",relationshipDelta=-2});return p;
    }
    public static object ApplyToActual()
    {
        if(Application.isPlaying || LocalStoryRuntimeService.IsBusy)throw new Exception("Stop gameplay and generation first.");
        ChapterContentCatalog.Reset();
        string root=Path.Combine(Application.persistentDataPath,"Saves");
        if(!Directory.Exists(root))throw new DirectoryNotFoundException(root);
        string folder=Path.GetFullPath("tmp/StorageLifecycle/ActualBackup/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var before=new Dictionary<string,string>();
        foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))
        {
            string relative=Path.GetRelativePath(root,file);byte[] bytes=File.ReadAllBytes(file);
            string backup=Path.Combine(folder,relative);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.WriteAllBytes(backup,bytes);
            if(!File.ReadAllBytes(backup).SequenceEqual(bytes))throw new IOException("Backup failed: "+relative);
            before[relative]=LocalStoryDialogueQueue.Hash(Convert.ToBase64String(bytes));
        }
        File.WriteAllText(Path.Combine(folder,"Manifest.json"),JObject.FromObject(before).ToString());
        string tests=Path.Combine(root,"LocalStoryTests");
        var reportFiles=LocalStoryTestArchive.List(tests);
        var reports=reportFiles.ToDictionary(f=>f,f=>LocalStoryTestArchive.ReadReport(f));
        int migrated=LocalStoryTestArchive.MigrateGrouped(tests);
        foreach(string file in reportFiles)
        {
            var after=LocalStoryTestArchive.ReadReport(file);var prior=reports[file];
            foreach(string key in new[]{"rawOutput","correctionRawOutput","original"})
                if(!JToken.DeepEquals(after[key],prior[key]))throw new IOException("Report changed during migration: "+file+" / "+key);
        }
        var store=Store(root);int run=JsonUtility.FromJson<ProfileData>(File.ReadAllText(Path.Combine(root,"Profile.json"))).currentRun;
        store.NextNarrativeJob();store.NextNarrativeJob(); // Normalize old jobs and inspect stored failures. Never invoke a model.
        var queue=LocalStoryDialogueQueue.Load(store,Math.Max(1,run-1));var displayed=new JArray();
        var instance=typeof(SaveIOService).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic);var previous=instance.GetValue(null);
        try
        {
            instance.SetValue(null,store);
            var repository=new EncounterContentRepository(run);
            foreach(var job in queue?.jobs??new List<LocalStoryDialogueJob>())
            {
                bool loaded=repository.TryLoadCards(job.storyPath,out var cards,out string error);
                bool generated=store.TryLoadGeneratedContent(run,"Encounters",job.storyPath+"/Story",out EncounterCardRoot content);
                bool matched=!generated || (loaded&&cards.MainStory.Select(c=>c.text).SequenceEqual(content.MainStory.Select(c=>c.text)));
                if(!matched)throw new IOException("Game loader mismatch: "+job.storyPath);
                displayed.Add(new JObject{["storyPath"]=job.storyPath,["status"]=job.status,["reviewRequired"]=store.NarrativeRequiresReview(run,job.storyPath,out _),
                    ["loaded"]=loaded,["generated"]=generated,["matchesGameLoader"]=matched,["cards"]=cards?.MainStory.Count??0,["error"]=error??job.error});
            }
        }
        finally{instance.SetValue(null,previous);}
        int protectedFiles=0;
        foreach(var pair in before)
            if(pair.Key=="Profile.json" || pair.Key.StartsWith("Runs"+Path.DirectorySeparatorChar,StringComparison.Ordinal) ||
                pair.Key.StartsWith("Records"+Path.DirectorySeparatorChar,StringComparison.Ordinal) || pair.Key.Contains(Path.DirectorySeparatorChar+"Edits"+Path.DirectorySeparatorChar))
            {
                string path=Path.Combine(root,pair.Key);
                if(!File.Exists(path) || LocalStoryDialogueQueue.Hash(Convert.ToBase64String(File.ReadAllBytes(path)))!=pair.Value)
                    throw new IOException("Protected save/edit changed: "+pair.Key);
                protectedFiles++;
            }
        var result=new JObject{["backup"]=folder,["backedUpFiles"]=before.Count,["protectedFilesUnchanged"]=protectedFiles,["migratedReports"]=migrated,
            ["reportCountBefore"]=reportFiles.Count,["reportCountAfter"]=LocalStoryTestArchive.List(tests).Count,
            ["nativeInferenceInvoked"]=false,["pendingJobs"]=queue?.jobs.Count(j=>j.status=="Pending"||j.status=="Generating")??0,["playback"]=displayed};
        File.WriteAllText(Path.GetFullPath("tmp/StorageLifecycle/actual-result.json"),result.ToString(),new UTF8Encoding(false));
        return result;
    }
    public static async Task<object> Regression()
    {
        if(Application.isPlaying)throw new Exception("Stop gameplay first.");ChapterContentCatalog.Reset();
        int checks=0;void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        string root=Path.GetFullPath("tmp/StorageLifecycle/Regression/"+Guid.NewGuid().ToString("N"));var store=Store(root);
        store.SaveProfile(new ProfileData{currentRun=1});var progress=Progress();store.SaveRunCheckpoint(1,"Progress",progress);
        LocalStoryDialogueQueue.EncounterEnded(store,1,"fixture","Guide",Guide);
        var job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();string hash=job.inputHash,snapshot=JsonUtility.ToJson(job.snapshot);
        Check(job.revision==1,"initial request not singular");
        Check((job.snapshot.playEvents?.Count??0)==0&&(job.snapshot.playedCards?.Count??0)==0,"queue duplicates source-run audit");
        progress.isCompleted=true;progress.unlockedIds.Add("later-unlock");progress.storedItemIds.Add("later-item");
        progress.storyEvents.Add(new StoryEventRecord{eventKey="actual:2",kind="choice",chapterId="Initial",characterId="Guide",choiceId="loki_question_god",relationshipId="Guide",relationshipDelta=1});
        store.SaveRunCheckpoint(1,"Progress",progress);
        LocalStoryDialogueQueue.EncounterEnded(store,1,"fixture","Guide",Guide);LocalStoryDialogueQueue.FinalizeRun(store,1);
        var frozen=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
        Check(frozen.revision==1&&frozen.inputHash==hash&&JsonUtility.ToJson(frozen.snapshot)==snapshot,"completion/extra choices recreated input");
        int calls=0;
        await LocalStoryDialogueQueue.Process(store,frozen,(r,ct)=>{calls++;r.rawOutput=Patch(r.original);LocalStoryRewrite.EvaluateDraft(r,r.original,r.interactionMemory);return Task.CompletedTask;},CancellationToken.None);
        job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();Check(job.status=="Ready"&&calls==1,"first generation failed");
        store.SaveProfile(new ProfileData{currentRun=2});int reports=LocalStoryTestArchive.List(Path.Combine(root,"LocalStoryTests")).Count;
        var composition=LocalStoryPrompt.Policy["composition"];int prior=(int)composition["maxExamples"];composition["maxExamples"]=0;
        try
        {
            Check(LocalStoryDialogueQueue.Next(store)==null,"next run/policy change recreated completed work");
            Check(store.TryLoadGeneratedContent(2,"Encounters",Guide+"/Story",out EncounterCardRoot _),"policy edit expired completed content");
        }
        finally{composition["maxExamples"]=prior;}
        await LocalStoryDialogueQueue.Process(store,job,(r,ct)=>throw new Exception("completed work inferred again"),CancellationToken.None);
        Check(LocalStoryTestArchive.List(Path.Combine(root,"LocalStoryTests")).Count==reports,"duplicate result created");
        string oldReport=job.reportPath;
        string emptyAttempt=LocalStoryTestArchive.Save(Path.Combine(root,"LocalStoryTests"),JObject.FromObject(new {
            storyPath=Guide,sourceRun=1,targetRun=2,original=JObject.Parse(JsonUtility.ToJson(Source())),rawOutput="{broken",runtimeError="cancelled"}));
        var queue=LocalStoryDialogueQueue.Load(store,1);queue.jobs[0].status="Pending";queue.jobs[0].revision=2;queue.jobs[0].reportPath=emptyAttempt;queue.jobs[0].error="cancelled";store.SaveNarrativeQueue(queue);
        Check(LocalStoryDialogueQueue.Next(store)==null&&LocalStoryDialogueQueue.Load(store,1).jobs.Single().status=="Ready","legacy requeue not recovered from existing content");
        var reused=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
        Check(reused.reportPath==oldReport&&string.IsNullOrEmpty(reused.error),"completed result still points to failed attempt");
        var nextProgress=Progress();store.SaveRunCheckpoint(2,"Progress",nextProgress);
        LocalStoryDialogueQueue.EncounterEnded(store,2,"fixture","Guide",Guide);
        Check(LocalStoryDialogueQueue.Load(store,2).jobs.Single().targetRun==3,"new actual dialogue cannot create following-run work");

        var failedStore=Store(root+"_failed");failedStore.SaveProfile(new ProfileData{currentRun=1});failedStore.SaveRunCheckpoint(1,"Progress",Progress());
        LocalStoryDialogueQueue.EncounterEnded(failedStore,1,"fixture","Guide",Guide);
        await LocalStoryDialogueQueue.Process(failedStore,LocalStoryDialogueQueue.Next(failedStore),(r,ct)=>{
            var source=Source();source.MainStory[8].options[0]=source.MainStory[8].choiceActions[0].choiceId;r.rawOutput=Patch(source);LocalStoryRewrite.EvaluateDraft(r,r.original,r.interactionMemory);return Task.CompletedTask;
        },CancellationToken.None);
        var failed=LocalStoryDialogueQueue.Load(failedStore,1).jobs.Single();
        Check(failed.status=="ReviewRequired"&&failedStore.NarrativeRequiresReview(2,Guide,out _),"failure hidden or labeled success");
        failedStore.SaveProfile(new ProfileData{currentRun=2});var completed=failedStore.LoadRunData<GameProgress>(1,"Progress");completed.isCompleted=true;failedStore.SaveRunCheckpoint(1,"Progress",completed);
        Check(LocalStoryDialogueQueue.Next(failedStore)==null,"failed playback recreated model request");
        Check(failedStore.TryLoadGeneratedContent(2,"Encounters",Guide+"/Story",out EncounterCardRoot visible)&&visible.MainStory[8].options[0]==visible.MainStory[8].choiceActions[0].choiceId,"failure not available in next run");
        Check(LocalStoryRewrite.SameBindings(Source(),visible),"failed wording changed executable bindings");
        var fragment=new JObject{["incompleteSections"]=new JArray(new JObject{["start"]=0,["count"]=1,["rawOutput"]=Patch(LocalStorySections.Slice(Source(),0,1))},new JObject{["start"]=1,["count"]=1,["rawOutput"]="{broken"})};
        var partial=new LocalStoryTestReport{storyPath=Guide,original=Source(),rawOutput=fragment.ToString(),runtimeError="cancelled"};
        var preview=LocalStoryGameApply.BuildFailedPreview(partial,out _);
        Check(partial.reviewGeneratedCards==1&&LocalStoryRewrite.SameBindings(Source(),preview),"partial diagnostic text lost or changed bindings");

        var original=Source();var draft=Source();draft.MainStory[0].text="자네, 잠시 멈춰 보게.";
        var corrected=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(draft));corrected.MainStory[1].text="이곳은 왕국에서 관리하는 광산이라네.";
        var report=new LocalStoryTestReport{storyPath=Guide,sourceRun=1,targetRun=2,original=original,rawOutput=Patch(draft),correctionAttempted=true,correctionRawOutput=Patch(corrected)};
        string tests=Path.Combine(root,"Archive"),file=LocalStoryTestArchive.Save(tests,JObject.Parse(JsonUtility.ToJson(report)));var saved=JObject.Parse(File.ReadAllText(file));
        Check(saved["draft"]==null&&saved["correction"]==null&&saved["application"]==null,"duplicate display output saved");
        Check(saved["correctionRawOutput"]==null&&saved["correctionPatch"].Count()==1,"unchanged correction cards duplicated");
        var loaded=LocalStoryTestArchive.ReadReport(file);
        Check((string)loaded["rawOutput"]==report.rawOutput&&(string)loaded["correctionRawOutput"]==report.correctionRawOutput,"canonical outputs lost in delta round trip");
        Check(loaded["draft"].Count()==1&&loaded["correction"].Count()==2,"inspection/edit view lost");
        var update=LocalStoryTestArchive.Load(file);update.saved=true;update.runtimeStage="failed-output";
        LocalStoryTestArchive.RecordRuntimeApplication(tests,file,update);saved=JObject.Parse(File.ReadAllText(file));
        Check(saved["application"]==null&&(bool)saved["saved"],"application metadata duplicated");
        return new{passed=true,checks,root,noTransitionRegeneration=true,failedPlayback=true,deltaRoundTrip=true};
    }
}
