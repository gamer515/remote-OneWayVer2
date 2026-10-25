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
        var queue=LocalStoryDialogueQueue.Load(store,1);queue.jobs[0].status="Pending";queue.jobs[0].revision=2;store.SaveNarrativeQueue(queue);
        Check(LocalStoryDialogueQueue.Next(store)==null&&LocalStoryDialogueQueue.Load(store,1).jobs.Single().status=="Ready","legacy requeue not recovered from existing content");
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
