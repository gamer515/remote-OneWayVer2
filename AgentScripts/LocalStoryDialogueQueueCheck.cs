using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Isolated persistent fixtures + deterministic fake inference. Never completes actual gameplay.
public static class LocalStoryDialogueQueueCheck
{
    static int checks;
    static void Check(bool ok,string error) { if(!ok) throw new Exception(error); checks++; }
    static SaveIOService Storage(string root) => (SaveIOService)typeof(SaveIOService).GetConstructor(
        BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(string)},null).Invoke(new object[]{root});
    static string Patch(EncounterCardRoot source) => new JObject {["cards"]=new JArray(source.MainStory.Select((c,i)=>new JObject {
        ["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}))}.ToString(Newtonsoft.Json.Formatting.None);
    static StoryEventRecord Choice(int n,int slot,string id,string npc="Guide",string relationship="Guide") => new StoryEventRecord {
        eventKey="choice:"+n,run=1,kind="choice",chapterId="Initial",characterId=npc,choiceId=id,
        choiceSlot=slot,choiceRole=new[]{"Positive","Neutral","Negative","Chaotic"}[slot],relationshipId=relationship,
        displayedText="RAW_AUDIT_ONLY_"+n,semanticText="RAW_AUDIT_ONLY_"+n };
    static LocalStoryDreamRule[] Rules() => JsonUtility.FromJson<LocalStoryDreamRules>(
        Resources.Load<TextAsset>("LocalStory/DreamMemoryRules").text).rules;
    static Func<LocalStoryTestReport,CancellationToken,Task> Fake(string text) => (report,token)=> {
        token.ThrowIfCancellationRequested();
        Check(report.interactionMemory.memoryMode=="Dream","not dream context");
        var context=JObject.Parse(((string)JArray.Parse(report.prompt)[0]["content"]).Split('\n').Last());
        Check((string)context["memory"]["mode"]=="Dream"&&context["memory"]["impressions"] is JArray&&
            !context["cards"].ToString().Contains("loki_question_god") && context["cards"].Any(c=>c["options"].Any(o=>!string.IsNullOrWhiteSpace((string)o["intent"]))),"dream/explanation mapping missing");
        Check(!report.prompt.Contains("RAW_AUDIT_ONLY_"),"unfiltered gameplay leaked into prompt");
        var changed=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(report.original));
        changed.MainStory[0].text=text;
        report.rawOutput=Patch(changed);
        LocalStoryRewrite.EvaluateDraft(report,report.original,report.interactionMemory);
        Check(report.valid,report.error);
        return Task.CompletedTask;
    };
    public static async Task<string> Regression()
    {
        checks=0;
        string root=Path.Combine(Path.GetTempPath(),"OneWayDialogueQueue_"+Guid.NewGuid().ToString("N"));
        var store=Storage(root);
        var path=LocalStoryTestInputs.GuidePath;
        string original=ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text;
        store.SaveProfile(new ProfileData {currentRun=1});
        var progress=new GameProgress();
        progress.storyEvents.Add(Choice(1,0,"positive"));
        progress.storyEvents.Add(Choice(2,1,"neutral"));
        progress.storyEvents.Add(Choice(3,2,"negative"));
        progress.storyEvents.Add(Choice(4,3,"loki_insult"));
        progress.storyEvents.Add(Choice(5,0,"garbled",relationship:""));
        progress.storyEvents.Add(Choice(6,0,"other",npc:"Duelist",relationship:"Duelist"));
        progress.guideProgress.runHistory.Add(new PlayedEncounterCardRecord {placeId="guide_1",encounterPath=path});
        store.SaveRunCheckpoint(1,"Progress",progress);
        Check(StoryChoiceRoles.ForMemory(new StoryEventRecord {choiceRole="Neutral",choiceSlot=3})=="Neutral","recorded meaning was inferred from position");
        var dreams=LocalStoryDialogueQueue.SelectDreamMemories(store,1,Rules());
        Check(dreams.Count==5,"four-role memory selection wrong");
        Check(dreams.Any(m=>m.eventKey=="choice:2") && !dreams.Any(m=>m.eventKey=="choice:5"),"neutral memory missing or mechanic became relationship memory");
        var completionRule=new LocalStoryDreamRule {enabled=true,runCompletedOnly=true,ruleId="completion",
            characterId="Guide",choiceId="positive",impression="fixture dream",requiresUnlocks=new[]{"earned"}};
        Check(LocalStoryDialogueQueue.SelectDreamMemories(store,1,new[]{completionRule}).Count==0,"early completion-only memory");
        LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
        Check(LocalStoryDialogueQueue.Load(store,1)==null,"opening alone queued unfinished dialogue");
        progress.unlockedIds.Add("event:guide_1");
        store.SaveRunCheckpoint(1,"Progress",progress);
        LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
        var queue=LocalStoryDialogueQueue.Load(store,1); var job=queue.jobs.Single();
        Check(job.status=="Pending" && !progress.isCompleted,"waited for whole run completion");
        Check((job.snapshot.playEvents?.Count??0)==0 && job.snapshot.events.Count==0,"audit/memory separation lost");
        Check(job.snapshot.dreamMemories.Count==4 && job.snapshot.dreamMemories.All(m=>m.characterId=="Guide"),"other NPC memory leaked");
        Check(job.targetRun==2 && !LocalStoryDialogueQueue.HasPendingForRun(store,1) && LocalStoryDialogueQueue.HasPendingForRun(store,2),"wrong run blocked");
        LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
        Check(LocalStoryDialogueQueue.Load(store,1).jobs.Single().revision==1,"duplicate checkpoint requeued");
        var serviceField=typeof(SaveIOService).GetField("_instance",BindingFlags.NonPublic|BindingFlags.Static);
        object previous=serviceField.GetValue(null);
        try
        {
            serviceField.SetValue(null,store);
            await LocalStoryDialogueQueue.Process(store,job,Fake("어이, 자네. 어쩐지 낯설지만은 않구만. 잠깐 멈춰 보게."),CancellationToken.None);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(job.status=="Ready" && !string.IsNullOrEmpty(job.contentHash),"next-run content not saved before run end");
            Check(!LocalStoryDialogueQueue.HasPendingForRun(store,2),"finished job blocks next run");
            Check(job.reportPath!=null && File.Exists(job.reportPath),"raw report missing");
            Check(job.auditArchived && (job.snapshot.playEvents?.Count??0)==0 && (job.snapshot.playedCards?.Count??0)==0,"completed live job duplicates long dialogue audit");
            Check(store.LoadLatestLocalStoryRevision().playEvents.Count==6,"test input cannot recover its archived audit");
            LocalStoryNotebook.Refresh(store);
            using(var workbook=ZipFile.OpenRead(LocalStoryNotebook.PathFor(store)))
                Check(workbook.Entries.Where(e=>e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml"))
                    .All(e=> {using var reader=new StreamReader(e.Open()); return !reader.ReadToEnd().Contains("생성 완료");}),
                    "generation state is still duplicated into Excel");
            var repository1=new EncounterContentRepository(1); var repository2=new EncounterContentRepository(2);
            Check(repository1.TryLoadCards(path,out var current,out var error),error);
            Check(current.MainStory[0].text==JsonUtility.FromJson<EncounterCardRoot>(original).MainStory[0].text,"current run changed");
            Check(repository2.TryLoadCards(path,out var next,out error),error);
            Check(next.MainStory[0].text.Contains("낯설지만은"),"next run not using generated dialogue");
            Check(next.MainStory[3].guideAction.amount==5 && next.MainStory[7].coreMeaning==current.MainStory[7].coreMeaning,"action/core changed");
            Check(JsonUtility.ToJson(next.MainStory[8].choiceActions)==JsonUtility.ToJson(current.MainStory[8].choiceActions),"choice outcomes reordered");
            Check(!File.ReadAllText(job.reportPath).Contains("RAW_AUDIT_ONLY_"),"full play audit duplicated in generated result");
            progress.storyEvents.Add(Choice(7,0,"positive_again")); store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(job.revision==1 && job.status=="Ready" && LocalStoryDialogueQueue.Next(store)==null,"later choices recreated completed request");
            await LocalStoryDialogueQueue.Process(store,job,(r,t)=>throw new Exception("completed job inferred again"),CancellationToken.None);
            var rulesBefore=LocalStoryDialogueQueue.SelectDreamMemories(store,1,new[]{completionRule});
            progress.isCompleted=true; progress.unlockedIds.Add("earned"); store.SaveRunCheckpoint(1,"Progress",progress);
            Check(rulesBefore.Count==0 && LocalStoryDialogueQueue.SelectDreamMemories(store,1,new[]{completionRule}).Count==1,"completion/unlock condition wrong");
            LocalStoryDialogueQueue.FinalizeRun(store,1);
            Check(LocalStoryDialogueQueue.Load(store,1).finalized && LocalStoryDialogueQueue.Load(store,1).jobs.Single().revision==1,"unchanged finalization re-generated");
            store.SaveProfile(new ProfileData {currentRun=2});
            Check(LocalStoryDialogueQueue.CanProcess(store,job),"next-run resume refused completed source");
            store.SaveProfile(new ProfileData {currentRun=3});
            Check(!LocalStoryDialogueQueue.CanProcess(store,job),"stale past request still processes");
            store.SaveProfile(new ProfileData {currentRun=1});
            progress.isCompleted=false;
            progress.storyEvents.Add(Choice(9,0,"positive_late")); store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            var cancelStore=Storage(root+"_cancel");cancelStore.SaveProfile(new ProfileData {currentRun=1});cancelStore.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(cancelStore,1,"guide_1","Guide",path);
            var cancelJob=LocalStoryDialogueQueue.Load(cancelStore,1).jobs.Single();
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();
                await LocalStoryDialogueQueue.Process(cancelStore,cancelJob,Fake("잠깐 멈춰 보게."),cancellation.Token);
            }
            cancelJob=LocalStoryDialogueQueue.Load(cancelStore,1).jobs.Single();
            Check(cancelJob.status=="Pending" && LocalStoryDialogueQueue.Next(cancelStore)!=null,"cancelled empty work cannot resume");
            await LocalStoryDialogueQueue.Process(cancelStore,cancelJob,Fake("어이, 자네. 잠깐 이야기 좀 나누세."),CancellationToken.None);
            Check(LocalStoryDialogueQueue.Load(cancelStore,1).jobs.Single().status=="Ready","resumed job failed");
            Check(store.TryLoadGeneratedContent(2,"Encounters",path+"/Story",out EncounterCardRoot reviewed),"review source missing");
            Check(store.SaveGeneratedContentForTest(2,"Encounters",path+"/Story",reviewed),"review application failed");
            progress.storyEvents.Add(Choice(10,2,"manual")); store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            await LocalStoryDialogueQueue.Process(store,job,(r,t)=>throw new Exception("manual file should not infer"),CancellationToken.None);
            Check(LocalStoryDialogueQueue.Load(store,1).jobs.Single().status=="Ready","manual edit overwritten");
            Check(store.SetNarrativeContentEnabled(2,path,false),"disable failed");
            progress.storyEvents.Add(Choice(11,0,"disabled")); store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            await LocalStoryDialogueQueue.Process(store,job,(r,t)=>throw new Exception("disabled should not infer"),CancellationToken.None);
            Check(!store.TryLoadGeneratedContent(2,"Encounters",path+"/Story",out EncounterCardRoot disabled) && LocalStoryDialogueQueue.Load(store,1).jobs.Single().status=="Ready","disabled restoration ignored");
            Check(repository2.TryLoadCards(path,out next,out error) && next.MainStory[0].text==current.MainStory[0].text,"disabled original fallback failed");
            // Read branches use the generic correction context rather than the Guide-only introduction.
            string branch=path+"/QuestionGod";
            var branchOriginal=JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Asset("Story_Json_Data/"+branch+"/Story").text);
            string correction=LocalStoryRewrite.CorrectionMessages(branch,branchOriginal,job.snapshot,Patch(branchOriginal));
            var repair=JObject.Parse(((string)JArray.Parse(correction)[1]["content"]).Split('\n').Last());
            Check((string)repair["repair"]["draft"]==Patch(branchOriginal)&&(string)repair["memory"]["mode"]=="Dream","branch correction unavailable");
            // A rejected raw generation is archived, not silently made into a successful original copy.
            string failedRoot=root+"_failed"; var failedStore=Storage(failedRoot);
            failedStore.SaveProfile(new ProfileData {currentRun=1}); failedStore.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(failedStore,1,"guide_1","Guide",path);
            await LocalStoryDialogueQueue.Process(failedStore,LocalStoryDialogueQueue.Next(failedStore),(r,t)=> {r.rawOutput="{broken"; r.error="invalid fixture";return Task.CompletedTask;},CancellationToken.None);
            var failed=LocalStoryDialogueQueue.Load(failedStore,1).jobs.Single();
            Check(failed.status=="Failed" && File.Exists(failed.reportPath),"failed output discarded");
            Check(!failedStore.HasNarrativeContent(failed.targetRun,failed.storyPath) && !LocalStoryDialogueQueue.HasPendingForRun(failedStore,2),"invalid content applied / next run blocked");
            Check(ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text==original,"canonical original changed");
            var deathStore=Storage(root+"_death"); deathStore.SaveProfile(new ProfileData {currentRun=1});
            var death=new GameProgress {isCompleted=true,activePlaceId="active_guide",runEndReason="fixture death"};
            death.storyEvents.Add(Choice(1,3,"loki_insult"));
            death.guideProgress.runHistory.Add(new PlayedEncounterCardRecord {placeId="active_guide",encounterPath=path,
                card=new Dialogue {speakerId="Guide"}});
            deathStore.SaveRunCheckpoint(1,"Progress",death);
            LocalStoryDialogueQueue.FinalizeRun(deathStore,1);
            Check(LocalStoryDialogueQueue.Load(deathStore,1)?.jobs.Count==1 && LocalStoryDialogueQueue.Load(deathStore,1).finalized,
                "death/run termination dropped active dialogue");
            var branches=Storage(root+"_branches"); branches.SaveProfile(new ProfileData {currentRun=1});
            var played=new GameProgress(); played.unlockedIds.Add("event:both");
            foreach(string p in new[]{path,branch}) played.guideProgress.runHistory.Add(
                new PlayedEncounterCardRecord {placeId="both",encounterPath=p});
            branches.SaveRunCheckpoint(1,"Progress",played);
            LocalStoryDialogueQueue.EncounterEnded(branches,1,"both","Guide",path);
            Check(LocalStoryDialogueQueue.Load(branches,1)?.jobs.Count==2,"read follow-up branch not queued");
            LocalStoryDialogueQueue.QueuePaths(branches,1,"Duelist",new[]{"Initial/Initial_01/Encounters/duelist"},"fixture disabled");
            Check(LocalStoryDialogueQueue.Load(branches,1).jobs.Count==2,"disabled NPC silently enabled");
        }
        finally { serviceField.SetValue(null,previous); }
        return checks+" checks passed. Isolated fake inference; not model quality or a real playthrough. Kept fixtures: "+root;
    }
}
