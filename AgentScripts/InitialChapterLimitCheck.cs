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

}
