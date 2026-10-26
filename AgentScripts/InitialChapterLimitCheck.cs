using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using JourneyMapKit;

// Memory-only regression tests. Never calls real saves, scene loading, or an AI API.
public static class InitialChapterLimitCheck
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    public static object Check()
    {
        var resource=ChapterContentCatalog.Asset("Story_Json_Data/Omnibus_01");
        Assert(resource!=null,"Omnibus resource missing");
        var omnibus=JsonUtility.FromJson<OmnibusData>(resource.text);
        Assert(omnibus.chapters.Count==3,"Later chapters removed");
        Assert(omnibus.IsProgressionLimited&&omnibus.PlayableChapterCount==1,"Initial limit missing");
        Assert(omnibus.chapters[0].chapterId=="Initial","Initial order changed");
        var checks=new List<object>();
        foreach(var chapter in omnibus.chapters)
            foreach(var episode in chapter.episodeIds)
                Assert(ChapterContentCatalog.Asset("Story_Json_Data/"+chapter.chapterId+"/"+episode+"/Terrain")!=null,"Preserved terrain missing");
        var scene=EditorSceneManager.NewPreviewScene();
        var go=new GameObject("InitialLimit_MemoryTest"){hideFlags=HideFlags.HideAndDontSave};
        go.SetActive(false);SceneManager.MoveGameObjectToScene(go,scene);
        try
        {
            var manager=go.AddComponent<DecisionManager>();
            var input=go.AddComponent<JourneyBoardInput>();
            typeof(DecisionManager).GetField("journeyBoardInput",Fields).SetValue(manager,input);
            var load=typeof(DecisionManager).GetMethod("LoadCurrentEpisode",Fields);
            foreach(int chapterIndex in new[]{1,2,3,99})
            {
                var session=new DecisionSession(omnibus,5,123,chapterIndex,0,0);
                typeof(DecisionManager).GetField("session",Fields).SetValue(manager,session);
                var calls=new List<string>();
                typeof(DecisionManager).GetField("finalBattleRequested",Fields).SetValue(manager,false);
                typeof(DecisionManager).GetField("finalBattleTransition",Fields).SetValue(manager,(Action)(()=>calls.Add("battle")));
                var completion=new DecisionRunCompletionService(
                    ()=>calls.Add("complete"),
                    ()=>{calls.Add("prepareNext");return 6;},
                    ()=>calls.Add("mainMenu"));
                typeof(DecisionManager).GetField("runCompletionService",Fields).SetValue(manager,completion);
                input.SetInputEnabled(true);
                load.Invoke(manager,null);
                Assert(session.ChapterIndex==chapterIndex&&session.RunNumber==5,"Checkpoint/run overwritten");
                Assert(!input.InputEnabled,"Progress input not locked");
                var state=(Constants.StoryState)typeof(DecisionManager).GetField("currentState",Fields).GetValue(manager);
                Assert(state==Constants.StoryState.Transitioning,"Did not exit before loading next chapter");
                Assert(calls.SequenceEqual(new[]{"battle"}),"Final journey must enter battle without completing/advancing the run");
                load.Invoke(manager,null);
                Assert(calls.Count==1,"Repeated journey completion re-entered battle");
                checks.Add(new{chapterIndex,enteredBattle=true,sourceSessionPreserved=true,nextRunDeferredUntilBattle=true});
            }
            AssertInitialPacket(go);
            omnibus.lastPlayableChapterId="Chapter_1";
            Assert(omnibus.PlayableChapterCount==2,"Expandable limit failed");
            omnibus.lastPlayableChapterId="";
            Assert(!omnibus.IsProgressionLimited&&omnibus.PlayableChapterCount==3,"Full progression compatibility failed");
            omnibus.lastPlayableChapterId=null;
            Assert(omnibus.PlayableChapterCount==3,"Legacy missing field compatibility failed");
            omnibus.lastPlayableChapterId="unknown";
            var environment=go.AddComponent<EnvController>();
            Assert(!environment.TryValidateAllContent(omnibus,out var error)&&!string.IsNullOrEmpty(error),"Invalid cap failed open");
            return new{initialPlayableChapters=1,preservedChapters=3,checks,initialPacketCreated=true,
                responseValidationPassed=true,generatedTextAppliedInMemory=true,expandableLimit=true,
                legacyCompatible=true,invalidCapRejected=true,saveFilesUntouched=true,apiCallsMade=false};
        }
        finally{UnityEngine.Object.DestroyImmediate(go);EditorSceneManager.ClosePreviewScene(scene);}
    }

    static void AssertInitialPacket(GameObject go)
    {
        var prompt=ScriptableObject.CreateInstance<PromptData>();
        try
        {
            var relay=go.AddComponent<StoryRelayManager>();
            typeof(StoryRelayManager).GetField("promptData",Fields).SetValue(relay,prompt);
            const string path="Initial/Initial_01/Encounters/guide";
            var text=ChapterContentCatalog.Asset("Story_Json_Data/"+path+"/Story").text;
            var original=JsonUtility.FromJson<EncounterCardRoot>(text);
            var records=new List<PlayedEncounterCardRecord>{new PlayedEncounterCardRecord{
                placeId="guide",encounterPath=path,cardIndex=0,
                card=new Dialogue{id=1,type="Next",text=original.MainStory[0].text}}};
            var build=typeof(StoryRelayManager).GetMethod("CreatePacket",Fields);
            var packet=(StoryPacket)build.Invoke(relay,new object[]{StoryRelayTrigger.EpisodeEnd,
                "Initial/Initial_01",records,
                new[]{10,10,10,10},0,5,null});
            Assert(packet!=null&&packet.sourceRun==5&&packet.targetRun==6,"Initial excluded or target run wrong");
            Assert(packet.encounterHistory.Count==1&&packet.encounterHistory[0].encounterPath==path,"Initial card missing");
            var items=new[]{new AIAPIClient.AIModifiedData{encounterPath=path,cardIndex=0,text="어이, 낯선 여행자. 여기에는 또 어쩐 일이야?"}};
            var validate=typeof(AIAPIClient).GetMethod("TryValidateModifiedItems",BindingFlags.Static|BindingFlags.NonPublic);
            var args=new object[]{packet,items,null};
            Assert((bool)validate.Invoke(null,args),"Valid Initial AI response rejected: "+args[2]);
            var generated=JsonUtility.FromJson<EncounterCardRoot>(text);
            generated.MainStory[0].text=items[0].text;
            var apply=typeof(EncounterContentRepository).GetMethod("TryApplyGeneratedText",BindingFlags.Static|BindingFlags.NonPublic);
            args=new object[]{original,generated,null,false};
            Assert((bool)apply.Invoke(null,args)&&original.MainStory[0].text==items[0].text,"Initial generated text not applied");
            items[0].encounterPath="Chapter_2/unrequested";
            args=new object[]{packet,items,null};
            Assert(!(bool)validate.Invoke(null,args),"Unrequested chapter accepted");
            generated.MainStory[0].type="End";
            args=new object[]{original,generated,null,false};
            Assert(!(bool)apply.Invoke(null,args),"Generated response changed card structure");
            records[0].card.text="{안개 광산}에서 조심하라.";
            items[0].encounterPath=path;items[0].text="다른 광산이다.";
            args=new object[]{packet,items,null};
            Assert(!(bool)validate.Invoke(null,args),"Immutable core changed");
            var noPacket=(StoryPacket)build.Invoke(relay,new object[]{StoryRelayTrigger.EpisodeEnd,
                "Initial/Initial_01",new List<PlayedEncounterCardRecord>(),
                new[]{10,10,10,10},0,5,null});
            Assert(noPacket==null,"Empty history queued AI request");
        }
        finally{UnityEngine.Object.DestroyImmediate(prompt);}
    }
}
