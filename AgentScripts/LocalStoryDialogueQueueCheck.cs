using System;
using System.IO;
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
        choiceSlot=slot,choiceRole=LocalStoryDialogueQueue.ChoiceRole(slot),relationshipId=relationship,
        displayedText="RAW_AUDIT_ONLY_"+n,semanticText="RAW_AUDIT_ONLY_"+n };
    static LocalStoryDreamRule[] Rules() => JsonUtility.FromJson<LocalStoryDreamRules>(
        Resources.Load<TextAsset>("LocalStory/DreamMemoryRules").text).rules;
    static Func<LocalStoryTestReport,CancellationToken,Task> Fake(string text) => (report,token)=> {
        token.ThrowIfCancellationRequested();
        Check(report.interactionMemory.memoryMode=="Dream","not dream context");
        Check(report.prompt.Contains("꿈 같은 인상") && report.prompt.Contains("1번 긍정"),"dream/order prompt missing");
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
        string original=Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text;
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
        Check(LocalStoryDialogueQueue.ChoiceRole(0)=="Positive" && LocalStoryDialogueQueue.ChoiceRole(1)=="Neutral" &&
            LocalStoryDialogueQueue.ChoiceRole(2)=="Negative" && LocalStoryDialogueQueue.ChoiceRole(3)=="Chaotic" &&
            LocalStoryDialogueQueue.ChoiceRole(-1)=="Unknown","choice positions wrong");
        var dreams=LocalStoryDialogueQueue.SelectDreamMemories(store,1,Rules());
        Check(dreams.Count==4,"positive/negative/explicit chaos memory selection wrong");
        Check(!dreams.Any(m=>m.eventKey=="choice:2" || m.eventKey=="choice:5"),"neutral/garbled became relationship memory");
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
        Check(job.snapshot.playEvents.Count==6 && job.snapshot.events.Count==0,"audit/memory separation lost");
        Check(job.snapshot.dreamMemories.Count==3 && job.snapshot.dreamMemories.All(m=>m.characterId=="Guide"),"other NPC memory leaked");
        Check(job.targetRun==2 && !LocalStoryDialogueQueue.BlocksRun(store,1) && LocalStoryDialogueQueue.BlocksRun(store,2),"wrong run blocked");
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
            Check(!LocalStoryDialogueQueue.BlocksRun(store,2),"finished job blocks next run");
            Check(job.reportPath!=null && File.Exists(job.reportPath),"raw report missing");
            var repository1=new EncounterContentRepository(1); var repository2=new EncounterContentRepository(2);
            Check(repository1.TryLoadCards(path,out var current,out var error),error);
            Check(current.MainStory[0].text==JsonUtility.FromJson<EncounterCardRoot>(original).MainStory[0].text,"current run changed");
            Check(repository2.TryLoadCards(path,out var next,out error),error);
            Check(next.MainStory[0].text.Contains("낯설지만은"),"next run not using generated dialogue");
            Check(next.MainStory[3].guideAction.amount==5 && next.MainStory[7].coreMeaning==current.MainStory[7].coreMeaning,"action/core changed");
            Check(JsonUtility.ToJson(next.MainStory[8].choiceActions)==JsonUtility.ToJson(current.MainStory[8].choiceActions),"choice outcomes reordered");
            Check(LocalStoryTestArchive.ReadReport(job.reportPath).ToString().Contains("RAW_AUDIT_ONLY_"),"full audit not archived separately");
            progress.storyEvents.Add(Choice(7,0,"positive_again")); store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(job.revision==2 && job.status=="Pending" && LocalStoryDialogueQueue.CanReplace(store,job),"owned output cannot update");
            var release=new TaskCompletionSource<bool>(); var entered=new TaskCompletionSource<bool>();
            var blocked=LocalStoryDialogueQueue.Process(store,job,async (report,token)=> {
                entered.SetResult(true); await release.Task;
                await Fake("자네, 잠시 멈춰 보게. 할 이야기가 있네.")(report,token);
            },CancellationToken.None);
            await entered.Task;
            progress.storyEvents.Add(Choice(8,2,"negative_again")); store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
            release.SetResult(true); await blocked;
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(job.revision==3 && job.status=="Pending","older worker overwrote latest request");
            Check(repository2.TryLoadCards(path,out next,out error) && next.MainStory[0].text.Contains("낯설지만은"),"obsolete output applied");
            await LocalStoryDialogueQueue.Process(store,job,Fake("어이, 자네. 꿈에서 본 듯도 한데, 어디까지나 기분 탓이겠지."),CancellationToken.None);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(job.status=="Ready" && job.revision==3,"latest revision not applied");
            var rulesBefore=LocalStoryDialogueQueue.SelectDreamMemories(store,1,new[]{completionRule});
            progress.isCompleted=true; progress.unlockedIds.Add("earned"); store.SaveRunCheckpoint(1,"Progress",progress);
            Check(rulesBefore.Count==0 && LocalStoryDialogueQueue.SelectDreamMemories(store,1,new[]{completionRule}).Count==1,"completion/unlock condition wrong");
            LocalStoryDialogueQueue.FinalizeRun(store,1);
            Check(LocalStoryDialogueQueue.Load(store,1).finalized && LocalStoryDialogueQueue.Load(store,1).jobs.Single().revision==3,"unchanged finalization re-generated");
            store.SaveProfile(new ProfileData {currentRun=2});
            Check(LocalStoryDialogueQueue.CanProcess(store,job),"next-run resume refused completed source");
            store.SaveProfile(new ProfileData {currentRun=3});
            Check(!LocalStoryDialogueQueue.CanProcess(store,job),"stale past request still processes");
            store.SaveProfile(new ProfileData {currentRun=1});
            progress.isCompleted=false;
            progress.storyEvents.Add(Choice(9,0,"positive_late")); store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            using(var cancellation=new CancellationTokenSource())
            {
                cancellation.Cancel();
                await LocalStoryDialogueQueue.Process(store,job,Fake("잠깐 멈춰 보게."),cancellation.Token);
            }
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(job.status=="Pending" && LocalStoryDialogueQueue.Next(store)!=null,"cancelled work cannot resume");
            await LocalStoryDialogueQueue.Process(store,job,Fake("어이, 자네. 잠깐 이야기 좀 나누세."),CancellationToken.None);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(job.status=="Ready","resumed job failed");
            string content=LocalStoryDialogueQueue.ContentPath(store,job);
            File.AppendAllText(content,"\n ");
            progress.storyEvents.Add(Choice(10,2,"manual")); store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            await LocalStoryDialogueQueue.Process(store,job,(r,t)=>throw new Exception("manual file should not infer"),CancellationToken.None);
            Check(LocalStoryDialogueQueue.Load(store,1).jobs.Single().status=="Preserved","manual edit overwritten");
            File.Move(content,Path.Combine(Path.GetDirectoryName(content),"Story.disabled.json"));
            progress.storyEvents.Add(Choice(11,0,"disabled")); store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",path);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            await LocalStoryDialogueQueue.Process(store,job,(r,t)=>throw new Exception("disabled should not infer"),CancellationToken.None);
            Check(!File.Exists(content) && LocalStoryDialogueQueue.Load(store,1).jobs.Single().status=="Preserved","disabled restoration ignored");
            Check(repository2.TryLoadCards(path,out next,out error) && next.MainStory[0].text==current.MainStory[0].text,"disabled original fallback failed");
            // Read branches use the generic correction context rather than the Guide-only introduction.
            string branch=path+"/Redistribute";
            var branchOriginal=JsonUtility.FromJson<EncounterCardRoot>(Resources.Load<TextAsset>("Story_Json_Data/"+branch+"/Story").text);
            string correction=LocalStoryRewrite.CorrectionMessages(branch,branchOriginal,job.snapshot,Patch(branchOriginal));
            Check(correction.Contains("교정 대상 초안") && correction.Contains("꿈 같은 인상"),"branch correction unavailable");
            // A rejected raw generation is archived, not silently made into a successful original copy.
            string failedRoot=root+"_failed"; var failedStore=Storage(failedRoot);
            failedStore.SaveProfile(new ProfileData {currentRun=1}); failedStore.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(failedStore,1,"guide_1","Guide",path);
            await LocalStoryDialogueQueue.Process(failedStore,LocalStoryDialogueQueue.Next(failedStore),(r,t)=> {r.rawOutput="{broken"; r.error="invalid fixture";return Task.CompletedTask;},CancellationToken.None);
            var failed=LocalStoryDialogueQueue.Load(failedStore,1).jobs.Single();
            Check(failed.status=="Failed" && File.Exists(failed.reportPath),"failed output discarded");
            Check(!File.Exists(LocalStoryDialogueQueue.ContentPath(failedStore,failed)) && !LocalStoryDialogueQueue.BlocksRun(failedStore,2),"invalid content applied / next run blocked");
            Check(Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text==original,"canonical original changed");
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
