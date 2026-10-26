using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class StoryRevisionCheck
{
    const string Guide="Initial/Initial_01/Encounters/guide", Item="prototype_loki_mark", Secret="prototype:loki_mine_mark_secret";
    static readonly XNamespace Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static int checks;
    static void Check(bool yes,string why) {if(!yes) throw new Exception(why);checks++;}
    static SaveIOService Store(string root) => (SaveIOService)Activator.CreateInstance(typeof(SaveIOService),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{root},null);
    static StoryEventRecord Choice(SaveManager manager,string id,int seq=1)
    {
        var value=new StoryEventRecord {eventKey="choice:"+seq,sequence=manager.LoadProgress().storyEvents.Count+1,run=manager.CurrentRun,chapterId="Initial",episodeId="Initial_01",characterId="Guide",characterName="로키",storyPath=Guide,kind="choice",choiceId=id};
        manager.LoadProgress().storyEvents.Add(value);manager.ApplyNarrativeInteractions(value);
        return manager.LoadProgress().storyEvents.Single(e=>e.eventKey==value.eventKey);
    }
    public static object Regression()
    {
        checks=0;Check(!Application.isPlaying && !LocalStoryRuntimeService.IsBusy,"Stop gameplay first.");
        foreach(var info in ChapterContentCatalog.Stories)
        {
            string path=(string)info["path"],file=ChapterContentCatalog.SourceFile(info);
            Check(!file.EndsWith((string)info["characterId"]+".Story.json",StringComparison.Ordinal),"Old combined NPC pack remains active.");
            Check(!File.Exists("Assets/Decision_YYS/Resources/Chapters/"+(string)info["chapterFolder"]+"/"+ChapterContentCatalog.EpisodeFolder((string)info["episodeId"])+"/"+(string)info["characterId"]+".Story.json"),"Old pack not retired.");
            var root=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));StoryChoiceRoles.ValidateRoot(root);
            Check(new EncounterContentRepository().TryLoadCards(path,out var _,out string error),path+": "+error);
        }
        string resource="Story_Json_Data/"+Guide+"/Story";var transient=ChapterContentCatalog.Asset(resource);string canonical=transient.text;
        UnityEngine.Object.DestroyImmediate(transient);
        Check(ChapterContentCatalog.Asset(resource).text==canonical,"Unloaded synthetic source asset was not recreated.");
        var instance=typeof(SaveIOService).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic);var previous=instance.GetValue(null);
        string rootPath=Path.GetFullPath("tmp/StoryRevision/"+Guid.NewGuid().ToString("N"));var storage=Store(rootPath);
        try
        {
            instance.SetValue(null,storage);var manager=new SaveManager();
            var gift=Choice(manager,"loki_question_god");
            Check(gift.resultStoryPath==Guide+"/Mark","First gift did not use normal scene.");
            Check(manager.LoadStoredItemIds().SequenceEqual(new[]{Item}),"First gift missing or duplicated.");
            Check(manager.ApplyNarrativeInteractions(gift).Count==0,"Replaying a confirmed choice selected a different item route.");
            var sameRun=Choice(manager,"loki_question_god",2);
            Check(sameRun.resultStoryPath==Guide+"/Mark","Same-run gift was treated as an unexplained past-run item.");
            var world=new WorldInventoryItemState {instanceId="carry-over-instance",itemId=Item,localPosition=new Vector3(.3f,.2f,-.4f),localRotation=Quaternion.Euler(0,23,0)};
            manager.SaveInventoryState(new InventoryState {worldItems=new List<WorldInventoryItemState>{world}});
            manager.MarkEncounterCompleted("guide");
            manager.StartNextRun();manager=new SaveManager();
            Check(manager.CurrentRun==2 && manager.LoadInventoryState().worldItems.Single().instanceId==world.instanceId,"Outside item lost at death.");
            Check(manager.LoadInventoryState().worldItems.Single().localPosition==world.localPosition,"Outside item pose lost.");
            Check(manager.LoadProgress().storyEvents.Count==0 && manager.LoadProgress().appliedEffectIds.Count==0 && string.IsNullOrEmpty(manager.LoadProgress().activeStoryPath),"Run-local progress was carried over.");
            Check(!manager.HasUnlock("event:guide"),"Completed encounter flag persisted into the new journey.");
            var owned=Choice(manager,"loki_question_god");
            Check(owned.resultStoryPath==Guide+"/Hidden/MarkOwned","Owned item did not select hidden scene.");
            Check(manager.LoadInventoryState().OwnedItemIds().Count==1,"Owned item granted again.");
            Check(new EncounterContentRepository(2).TryLoadCards(owned.resultStoryPath,out var dialogue,out string dialogueError),dialogueError);
            Check(dialogue.MainStory[0].options[0].Contains("이미"),"Owned-item first option missing.");
            var explain=Choice(manager,dialogue.MainStory[0].choiceActions[0].choiceId,2);
            Check(manager.HasUnlock(Secret),"Owned-item explanation did not unlock the secret.");
            string follow=dialogue.MainStory[0].choiceActions[0].storyPath;
            Check(new EncounterContentRepository(2).TryLoadCards(follow,out var reaction,out string reactionError),reactionError);
            Check(reaction.MainStory[0].text.Contains("왜") && reaction.MainStory.Last().text.Contains("오래된 길"),"Surprise/explanation is not playable dialogue.");
            manager.SaveInventoryState(new InventoryState {storedItemIds=new List<string>{Item}});
            manager.StartNextRun();manager=new SaveManager();
            Check(manager.LoadStoredItemIds().SequenceEqual(new[]{Item}) && manager.HasUnlock(Secret),"Bag item/discovery lost at next death.");
            var known=Choice(manager,"loki_question_god");
            Check(known.resultStoryPath==Guide+"/Hidden/MarkKnown","Unlocked secret did not change the scene.");
            Check(new EncounterContentRepository(3).TryLoadCards(known.resultStoryPath,out var knownCards,out string knownError),knownError);
            Check(knownCards.MainStory[0].options[0].Contains("단서"),"Unlocked first option does not show discovery.");
            Choice(manager,"prototype_mark_return",2);Check(!manager.LoadInventoryState().OwnedItemIds().Contains(Item),"Explicit return did not consume the item.");
            manager.StartNextRun();manager=new SaveManager();
            Check(!manager.LoadInventoryState().OwnedItemIds().Contains(Item),"Returned item resurrected at death.");
            Check(manager.HasUnlock(Secret),"Returning item removed discovery.");
            var clean=Store(Path.Combine(rootPath,"NewProfile"));instance.SetValue(null,clean);var fresh=new SaveManager();
            Check(fresh.CurrentRun==1 && !fresh.HasUnlock(Secret) && fresh.LoadInventoryState().OwnedItemIds().Count==0,"Full reset retained another profile's inventory.");
            Check(LocalStoryLog.Format(LocalStoryLogStage.Working,"Gemma", "모델 검사").Contains("[모델 검사 중]"),"Verification still labelled generation.");
            return new {checks,root=rootPath,itemAndUnlockCarryOver=true,ownedAndKnownDialogue=true,returnAndReset=true,allSourcesLoad=true};
        }
        finally {instance.SetValue(null,previous);}
    }
    public static async Task<object> GenerationStatus()
    {
        checks=0;string root=Path.GetFullPath("tmp/StoryRevisionStatus/"+Guid.NewGuid().ToString("N"));var storage=Store(root);
        storage.LoadOrCreateProfile();storage.SaveRunCheckpoint(1,"Progress",new GameProgress());
        var report=new LocalStoryTestReport {storyPath=Guide,model="Gemma",original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(Guide)),seed=3};
        var job=new LocalStoryDialogueJob {jobId=Guide,storyPath=Guide,chapterId="Initial",episodeId="Initial_01",characterId="Guide",sourceRun=1,targetRun=2,status="Generating",snapshot=new LocalStoryRevisionRequest {npcContexts=new[]{new LocalStoryNpcContext {characterId="Guide",model="Gemma"}}}};
        var queue=new LocalStoryDialogueQueueData {sourceRun=1,targetRun=2,jobs=new List<LocalStoryDialogueJob>{job}};
        queue.jobs.Add(new LocalStoryDialogueJob {jobId=Guide+"/Farewell",storyPath=Guide+"/Farewell",characterId="Guide",sourceRun=1,targetRun=2,status="Ready",model="Gemma"});
        LocalStoryDialogueQueue.Save(storage,queue);int callbacks=0;
        report.sectionProgress=(start,count,index,total,phase)=> {
            callbacks++;job.generationPart=index+"/"+total+" 구간 / 카드 "+(start+1)+"~"+(start+count)+" / "+phase;
            LocalStoryDialogueQueue.Save(storage,queue);
            var saved=LocalStoryDialogueQueue.Load(storage,1).jobs.Single(j=>j.storyPath==Guide);
            Check(saved.status=="Generating","Generation state missing from queue JSON.");
            Check(saved.generationPart.Contains("카드 "+(start+1)+"~"+(start+count)),"Current section not saved before inference.");
        };
        await LocalStorySections.Generate(report,CancellationToken.None,(input,correction,token,stream)=> {
            var slice=LocalStorySections.Slice(report.original,input.start,input.count);
            var raw=new JObject { ["cards"]=new JArray(slice.MainStory.Select((c,i)=>new JObject { ["index"]=i,["text"]=c.text,["options"]=JArray.FromObject(c.options??Array.Empty<string>()) })) };
            return Task.FromResult(new LocalStoryModelOutput {json=raw.ToString(),backend="isolated test"});
        });
        Check(callbacks==report.sectionInputs.Count && callbacks>1,"Not every section emitted status.");
        Check(report.valid,"Section integration invalid: "+report.error);
        job.status="Ready";LocalStoryDialogueQueue.Save(storage,queue);
        Check(!Directory.Exists(Path.Combine(root,"Records")),"generation progress still writes Excel.");
        return new {checks,root,sections=callbacks,wholeSceneTargets=report.sectionInputs.Sum(s=>s.count),nativeInference=false};
    }
    public static async Task<object> InvalidGenerationFallback()
    {
        checks=0;string root=Path.GetFullPath("tmp/StoryRevisionFallback/"+Guid.NewGuid().ToString("N"));var storage=Store(root);
        string path=Guide+"/Hidden/MarkOwnedExplain";
        var source=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
        foreach(var c in source.MainStory) Check(LocalStorySections.ValidateSituation(path,c,c.text,Array.Empty<EncounterCard>(),out _),"Guard rejects canonical dialogue.");
        Check(!LocalStorySections.ValidateSituation(path,source.MainStory[1],"벽에 같은 그림자를 발견하면 그냥 지나치게나.",Array.Empty<EncounterCard>(),out _),"Observed inverted clue was accepted.");
        var npc=new LocalStoryNpcContext {characterId="Guide",name="로키",model="Gemma",autoGenerate=true};
        var request=new LocalStoryRevisionRequest {sourceRun=1,targetRun=2,characterId="Guide",npcContexts=new[]{npc}};
        bool generated=false;
        var result=await LocalStoryRuntimePipeline.ProduceDialogue(request,npc,path,storage,(report,token)=> {
            generated=true;
            report.rawOutput=new JObject { ["cards"]=new JArray(source.MainStory.Select((c,i)=>new JObject { ["index"]=i,["text"]=c.text+" 999개",["options"]=new JArray() })) }.ToString();
            LocalStoryRewrite.EvaluateDraft(report,report.original,report.interactionMemory);return Task.CompletedTask;
        },CancellationToken.None,()=>true);
        Check(generated,"Generation failed before reaching the inference delegate: "+result.error);
        Check(result.status=="ReviewRequired" && File.Exists(result.reportPath),"Failed wording was not retained with review status.");
        Check(storage.TryLoadGeneratedContent(2,"Encounters",path+"/Story",out EncounterCardRoot failed)&&failed.MainStory[0].text.Contains("999개"),"Failed wording was silently replaced.");
        var instance=typeof(SaveIOService).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic);var previous=instance.GetValue(null);
        try
        {
            instance.SetValue(null,storage);
            Check(new EncounterContentRepository(2).TryLoadCards(path,out var cards,out string error),error);
            Check(cards.MainStory[0].text==source.MainStory[0].text+" 999개" && cards.MainStory[1].text==source.MainStory[1].text+" 999개","Gameplay loader did not show the failed wording.");
            var broken=Store(root+"_broken");
            var brokenResult=await LocalStoryRuntimePipeline.ProduceDialogue(request,npc,path,broken,(report,token)=>{
                report.rawOutput="{";LocalStoryRewrite.EvaluateDraft(report,report.original,report.interactionMemory);return Task.CompletedTask;
            },CancellationToken.None,()=>true);
            Check(brokenResult.status=="Failed"&&!broken.HasNarrativeContent(2,path),"Broken JSON became playable content.");
            instance.SetValue(null,broken);
            Check(new EncounterContentRepository(2).TryLoadCards(path,out cards,out error)&&cards.MainStory[0].text==source.MainStory[0].text,"Unparseable output did not retain canonical story.");
        }
        finally {instance.SetValue(null,previous);}
        return new {checks,root,failedWordingPlayable=true,brokenJsonUsesSource=true,nativeInference=false};
    }
}
