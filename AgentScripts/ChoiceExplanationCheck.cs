using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Production data, queue and section generator; all writes use an isolated save directory.
public static class ChoiceExplanationCheck
{
    const string Guide="Initial/Initial_01/Encounters/guide";
    const string Allocate=Guide+"/Redistribute";
    const string Repeat=Allocate+"/Repeat";
    static int checks;
    static void Check(bool ok,string error) {if(!ok)throw new Exception(error);checks++;}
    static EncounterCardRoot Source(string path)=>JsonUtility.FromJson<EncounterCardRoot>(ChapterContentCatalog.Story(path));
    static void ClearStoryCache()
    {
        ((IDictionary)typeof(ChapterContentCatalog).GetField("storyCache",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Clear();
        var assets=(IDictionary)typeof(ChapterContentCatalog).GetField("assets",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        foreach(TextAsset asset in assets.Values)if(asset!=null)UnityEngine.Object.DestroyImmediate(asset);
        assets.Clear();NarrativeInteractions.ResetCache();
    }
    static SaveIOService Storage(string root)=>(SaveIOService)typeof(SaveIOService).GetConstructor(
        BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(string)},null).Invoke(new object[]{root});
    static string Patch(EncounterCardRoot source)=>new JObject {["cards"]=new JArray(source.MainStory.Select((c,i)=>new JObject {
        ["index"]=i,["text"]=c.text+" …",["options"]=new JArray(c.options??Array.Empty<string>())}))}.ToString();

    public static async Task<object> Regression()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        checks=0;
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"..","tmp","ChoiceExplanationCheck",Guid.NewGuid().ToString("N")));
        var store=Storage(root);
        var instance=typeof(SaveIOService).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic);
        object previous=instance.GetValue(null);
        try
        {
            instance.SetValue(null,store);
            int roleCount=0;
            foreach(var info in ChapterContentCatalog.Stories)
            {
                string path=(string)info["path"];
                var file=Resources.Load<TextAsset>("Chapters/"+ChapterContentCatalog.SourceFile(info).Replace(".json",""));
                var raw=(JArray)JObject.Parse(file.text)[(string)info["key"]]["MainStory"];
                foreach(JObject card in raw)
                foreach(JObject action in card["choiceActions"] as JArray??new JArray())
                    Check(action["choiceRole"]==null,"classification remains in Story: "+path);
                foreach(var card in Source(path).MainStory.Where(c=>c.choiceKind=="Attitude"))
                {
                    Check(StoryChoiceRoles.Validate(card,out _),"compiled choice invalid: "+path);
                    roleCount+=card.choiceActions.Length;
                }
            }
            Check(roleCount==4,"unexpected role migration coverage");
            var choice=Source(Guide).MainStory.Single(c=>c.eventIdStable=="loki_identity_choice");
            var explanation=ChapterContentCatalog.Explanation(Guide);
            var meaning=explanation["coreMeaning"]["-1574548902"]["choices"];
            Check((string)meaning["loki_question_god"]["choiceRole"]=="Positive","explanation lacks stable ID meaning");
            meaning["loki_question_god"]["choiceRole"]="Negative";
            meaning["loki_question_god"]["intent"]="MODIFIED_INTENT_FROM_EXPLANATION";
            ClearStoryCache();
            var changed=Source(Guide);
            var actual=changed.MainStory.Single(c=>c.eventIdStable=="loki_identity_choice");
            Check(actual.choiceActions.Single(a=>a.choiceId=="loki_question_god").choiceRole=="Negative","explanation edit ignored");
            var infoGuide=ChapterContentCatalog.Info(Guide);
            string resource=ChapterContentCatalog.SourceFile(infoGuide).Replace(".json","");
            var document=(JObject)typeof(ChapterContentCatalog).GetMethod("Document",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{resource});
            var authored=document[(string)infoGuide["key"]]["MainStory"].Single(c=>(string)c["eventIDstable"]=="loki_identity_choice");
            authored["options"]=new JArray(authored["options"].Reverse().Select(x=>x.DeepClone()));
            authored["choiceActions"]=new JArray(authored["choiceActions"].Reverse().Select(x=>x.DeepClone()));
            ClearStoryCache();changed=Source(Guide);actual=changed.MainStory.Single(c=>c.eventIdStable=="loki_identity_choice");
            Check(StoryChoiceRoles.Validate(actual,out _),"classification still depends on button order or uniqueness of roles");
            string prompt=LocalStoryRewrite.Messages(Guide,changed,null);
            var targets=JObject.Parse(((string)JArray.Parse(prompt)[1]["content"]).Split('\n').Last())["cards"];
            var intent=targets.Last["options"];
            Check((string)intent[0]["intent"]==(string)meaning["loki_insult"]["intent"] &&
                (string)intent[3]["intent"]=="MODIFIED_INTENT_FROM_EXPLANATION" && intent.All(o=>o["choiceId"]==null),"prompt ignores ID mapping or leaks identifiers");
            Check(!actual.options.Any(s=>s.Contains("Positive")||s.Contains("Negative")),"metadata leaked into buttons");
            Check(StoryChoiceRoles.ForMemory(new StoryEventRecord {choiceRole="Positive",choiceSlot=3})=="Positive","recorded historical meaning replaced");
            ChapterContentCatalog.Reset();

            string full=ChapterContentCatalog.Story(Allocate);
            Check(ChapterContentCatalog.ResolveStoryPath(Allocate,1)==Allocate,"first-run tutorial skipped");
            Check(ChapterContentCatalog.ResolveStoryPath(Allocate,2)==Repeat && ChapterContentCatalog.ResolveStoryPath(Allocate,8)==Repeat,"repeat route missing");
            Check(ChapterContentCatalog.ResolveStoryPath(Guide,2)==Guide && ChapterContentCatalog.ResolveStoryPath(Guide+"/Hidden/MarkOwned",2)==Guide+"/Hidden/MarkOwned",
                "unrelated NPC or hidden dialogue skipped");
            var repository1=new EncounterContentRepository(1);var repository2=new EncounterContentRepository(2);
            Check(repository1.TryLoadCards(Allocate,out var first,out var error),error);
            Check(first.MainStory.Count==17,"first tutorial changed");
            var old=repository1.Capture("guide_1",Allocate,first);
            Check(repository2.TryLoadCards(Allocate,out var next,out error,snapshot:old),error);
            Check(next.MainStory.Count==1 && next.MainStory[0].eventIdStable=="loki_allocate" && next.MainStory[0].guideAction.kind=="allocate","repeat not ready for allocation");
            Check(next.MainStory[0].id==first.MainStory.Last().id && JsonUtility.ToJson(next.MainStory[0].guideAction)==JsonUtility.ToJson(first.MainStory.Last().guideAction),
                "allocation ID or ready/retry/death routes changed");
            next.MainStory[0].text="기다리고 있었네. 어서 선택하게.";
            var pinned=repository2.Capture("guide_1",Allocate,next);
            Check(pinned.storyPath==Repeat,"repeat snapshot stored under old path");
            Check(repository2.TryLoadCards(Allocate,out var resumed,out error,snapshot:pinned) && resumed.MainStory[0].text==next.MainStory[0].text,"repeat snapshot changed on resume");

            store.SaveProfile(new ProfileData {currentRun=1});
            var progress=new GameProgress();
            progress.unlockedIds.Add("event:guide_1");
            progress.guideProgress.runHistory.Add(new PlayedEncounterCardRecord {placeId="guide_1",encounterPath=Allocate,card=first.MainStory.Last()});
            store.SaveRunCheckpoint(1,"Progress",progress);
            LocalStoryDialogueQueue.EncounterEnded(store,1,"guide_1","Guide",Guide);
            var job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(job.storyPath==Repeat && job.targetRun==2,"first-run completion queues explanatory tutorial for run 2");
            int calls=0;
            await LocalStoryDialogueQueue.Process(store,job,async (report,token)=> {
                Check(report.original.MainStory.Count==1 && report.storyPath==Repeat,"section generator receives wrong scene");
                await LocalStorySections.Generate(report,token,(input,correction,ct,onText)=> {
                    calls++;ct.ThrowIfCancellationRequested();
                    string json=Patch(LocalStorySections.Slice(report.original,input.start,input.count));onText(json);
                    return Task.FromResult(new LocalStoryModelOutput {json=json,backend="IsolatedFake"});
                });
                Check(report.valid && report.authoredCards==0 && report.sectionsGenerated==1 && report.sectionInputs.Sum(s=>s.count)==1,
                    "selected repeat scene was not fully generated: "+report.error);
            },CancellationToken.None);
            job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
            Check(calls==1 && job.status=="Ready","repeat queue did not finish in one section: "+job.error);
            Check(store.TryLoadGeneratedContent(2,"Encounters",Repeat+"/Story",out EncounterCardRoot generated) && generated.MainStory.Count==1,"repeat output stored under incorrect scene");
            Check(repository2.TryLoadCards(Allocate,out resumed,out error) && resumed.MainStory.Single().text.EndsWith(" …"),"run 2 ignores generated repeat");

            var legacy=LocalStoryDialogueQueue.Load(store,1);
            legacy.jobs[0].storyPath=Allocate;legacy.jobs[0].jobId=Allocate;legacy.jobs[0].status="Pending";
            LocalStoryDialogueQueue.Save(store,legacy);
            Check(store.NextNarrativeJob()==null,"legacy tutorial job blocks new run");
            Check(LocalStoryDialogueQueue.Load(store,1).jobs.Single(j=>j.storyPath==Allocate).status=="Preserved","legacy queued evidence discarded");
            Check(!LocalStoryDialogueQueue.HasPendingForRun(store,2),"obsolete tutorial remains pending");
            Check(ChapterContentCatalog.Story(Allocate)==full,"first-run source modified by repeat generation");

            progress.guideProgress.allocating=true;progress.guideProgress.allocationVisit=41;
            progress.guideProgress.allocation=new[]{2,3,4,5};
            store.SaveRunCheckpoint(2,"Progress",progress);
            var reload=store.LoadRunData<GameProgress>(2,"Progress");
            Check(repository2.TryLoadCards(Allocate,out resumed,out error,snapshot:old) && resumed.MainStory.Count==1,
                "old multi-card snapshot prevents repeat allocation load");
            Check(reload.guideProgress.allocating && reload.guideProgress.allocationVisit==41 && reload.guideProgress.allocation.SequenceEqual(new[]{2,3,4,5}),
                "allocation draft lost during save/load");
            return new {passed=true,checks,rolesInExplanation=roleCount,firstRunCards=17,repeatCards=1,modelCalls=calls,isolatedSave=root};
        }
        finally {instance.SetValue(null,previous);ChapterContentCatalog.Reset();}
    }
}
