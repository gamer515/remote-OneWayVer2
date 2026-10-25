using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

public static class ChapterStructureCheck
{
    public static object LatestNativeResult()
    {
        string directory=Directory.GetDirectories("tmp/NarrativeNativeSections").OrderByDescending(Directory.GetLastWriteTimeUtc).First();
        string result=LocalStoryTestArchive.List(Path.Combine(directory,"LocalStoryTests")).LastOrDefault();
        if(result==null) return new {ready=false,directory};
        var report=LocalStoryTestArchive.Load(result);
        return new {ready=true,path=result,report.valid,report.correctionValid,report.seconds,report.sectionsGenerated,report.sectionsCorrected,
            report.authoredCards,report.model,report.backend,report.error,report.correctionError,
            cards=report.original.MainStory.Count,options=report.original.MainStory.Sum(c=>c.options?.Length??0),
            targets=report.sectionInputs.Sum(i=>i.count),sectionCalls=report.sectionInputs.Count};
    }
    public static object CleanupSourceArtifacts()
    {
        foreach(string file in new[]{"Assets/Decision_YYS/Resources/Story_Json_Data/Story Frame.json","Assets/StreamingAssets/LocalStory/NpcStoryData.xlsx"})
            if(File.Exists(file))
            {
                string backup=Path.Combine("tmp/ChapterMigration/RetiredSources",file);Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if(!File.Exists(backup)) File.Copy(file,backup);
                if(!File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(backup))) throw new Exception("Retired source backup mismatch: "+file);
                if(File.Exists(file+".meta")) File.Copy(file+".meta",backup+".meta",true);
                if(!AssetDatabase.DeleteAsset(file)) throw new Exception("Could not remove retired source artifact: "+file);
            }
        string books="Assets/StreamingAssets/LocalStory/Chapters";
        foreach(string file in Directory.GetFiles(books,"*.inspect.ndjson")) AssetDatabase.DeleteAsset(file.Replace('\\','/'));
        foreach(string folder in new[]{"Assets/Decision_YYS/Resources/Story_Json_Data","Assets/Decision_YYS/Resources/Narrative"})
            if(Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) AssetDatabase.DeleteAsset(folder);
        return new {cleaned=true};
    }
    private static SaveIOService Isolated(string root) => (SaveIOService)Activator.CreateInstance(typeof(SaveIOService),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{root},null);
    public static object StaleSourcesAndRetention()
    {
        int checks=0;void Check(bool yes,string why) {if(!yes) throw new Exception(why);checks++;}
        string root=Path.GetFullPath("tmp/StaleSources/"+Guid.NewGuid().ToString("N")),path=LocalStoryTestInputs.GuidePath,mark=path+"/Mark";
        var store=Isolated(root);var source=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));source.MainStory[0].text="자네, 잠깐 멈춰 보게.";
        Check(store.SaveGeneratedContent(2,"Encounters",path+"/Story",source),"Initial save failed");
        string file=Path.Combine(store.GeneratedNpcDirectory(2,path),"Story.json");var doc=JObject.Parse(File.ReadAllText(file));
        string owned=(string)doc["stories"][0]["contentHash"];
        doc["stories"][0]["sourceHash"]=LocalStoryDialogueQueue.Hash("previous source version");File.WriteAllText(file,doc.ToString());store=Isolated(root);
        Check(!store.TryLoadGeneratedContent(2,"Encounters",path+"/Story",out EncounterCardRoot _),"Stale changes applied");
        Check(store.CanReplaceNarrativeContent(2,path,owned),"Owned stale output cannot regenerate");
        Check(store.SaveGeneratedContent(2,"Encounters",mark+"/Story",JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(mark))),"Stale scene blocked another scene");
        Check(((JArray)JObject.Parse(File.ReadAllText(file))["stories"]).Count==2,"Stale scene was dropped while saving another scene");
        Check(store.SaveGeneratedContent(2,"Encounters",path+"/Story",source),"Fresh output cannot replace stale owned output");
        Check(store.TryLoadGeneratedContent(2,"Encounters",mark+"/Story",out EncounterCardRoot _),"Refreshing one scene removed another");
        Check(File.Exists(Path.Combine(root,"Migrations","LegacyContent.zip")),"Prior stale changes were not backed up");
        string tests=Path.Combine(root,"LocalStoryTests"),protectedFile=null;
        for(int n=0;n<6;n++)
        {
            var raw=new JObject { ["cards"]=new JArray(source.MainStory.Select((c,i)=>new JObject { ["index"]=i,["text"]=c.text,["options"]=JArray.FromObject(c.options??Array.Empty<string>()) })) };
            var report=JObject.Parse(JsonUtility.ToJson(new LocalStoryTestReport {storyPath=path,targetRun=2,model="Gemma",testScenario="ActualGameplay / isolated retention",rawOutput=raw.ToString(),original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path))}));
            string result=LocalStoryTestArchive.Save(tests,report);
            if(n==0)
            {protectedFile=LocalStoryTestArchive.EditPath(result);var edit=JObject.Parse(File.ReadAllText(protectedFile));edit["draft"][0]["text"]="사람이 수정한 대사";File.WriteAllText(protectedFile,edit.ToString());}
        }
        Check(LocalStoryTestArchive.List(tests).Count==4,"Automatic result count was not bounded");
        Check(File.Exists(protectedFile) && File.ReadAllText(protectedFile).Contains("사람이 수정한 대사"),"Automatic cleanup deleted a human edit");
        return new {checks,root,staleSourceFallback=true,otherScenesPreserved=true,humanEditProtected=true};
    }
    public static object Migration()
    {
        int checks=0;void Check(bool yes,string message) {if(!yes) throw new Exception(message);checks++;}
        string root=Path.GetFullPath("tmp/ChapterMigrationChecks/"+Guid.NewGuid().ToString("N"));var store=Isolated(root);
        string path=LocalStoryTestInputs.GuidePath;
        var source=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));source.MainStory[0].text="자네, 잠깐 이쪽을 보게.";
        string content=JsonUtility.ToJson(source,true);
        string run=Path.Combine(root,"Runs","Run_0001");Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(run,"Progress.json"),JsonUtility.ToJson(new GameProgress {storedItemIds=new System.Collections.Generic.List<string>{"kept-item"}}));
        File.WriteAllText(Path.Combine(run,"LocalPacket_Initial_Initial.json"),"{\"old\":true}");
        var chapter=new NarrativeChapterSave {chapterId="Initial"};
        chapter.stories.Add(new NarrativeStoryEntry {targetRun=2,chapterId="Initial",episodeId="Initial_01",characterId="Guide",storyPath=path,
            origin="Review",sourceHash="old-original-format-hash",contentJson=content,contentHash=LocalStoryDialogueQueue.Hash(content)});
        chapter.queues.Add(new LocalStoryDialogueQueueData {sourceRun=1,targetRun=2,jobs=new System.Collections.Generic.List<LocalStoryDialogueJob> {
            new LocalStoryDialogueJob {jobId=path,storyPath=path,sourceRun=1,targetRun=2,characterId="Guide",snapshot=new LocalStoryRevisionRequest {sourceRun=1,targetRun=2}} }});
        string legacy=store.NarrativeChapterPath("Initial");Directory.CreateDirectory(Path.GetDirectoryName(legacy));File.WriteAllText(legacy,JsonUtility.ToJson(chapter));
        string tests=Path.Combine(root,"LocalStoryTests"),oldFolder=Path.Combine(tests,"Results","0001");Directory.CreateDirectory(oldFolder);
        var raw=new JObject { ["cards"]=new JArray(source.MainStory.Select((c,i)=>new JObject { ["index"]=i,["text"]=c.text,["options"]=JArray.FromObject(c.options??Array.Empty<string>()) })) };
        var report=new JObject { ["archiveNumber"]=1,["archiveId"]="0001_legacy",["archiveOrigin"]=null,["storyPath"]=path,["sourceRun"]=1,["targetRun"]=2,["model"]="Gemma",
            ["original"]=JObject.Parse(JsonUtility.ToJson(JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path)))),["rawOutput"]=raw.ToString(),["prompt"]="actual old prompt",["testScenario"]="legacy" };
        File.WriteAllText(Path.Combine(oldFolder,"Input.json"),new JObject { ["original"]=report["original"].DeepClone(),["prompt"]=report["prompt"].DeepClone() }.ToString());
        File.WriteAllText(Path.Combine(oldFolder,"Result.json"),new JObject { ["rawOutput"]=report["rawOutput"].DeepClone() }.ToString());
        File.WriteAllText(Path.Combine(oldFolder,"Analysis.json"),new JObject(report.Properties().Where(p=>p.Name!="original" && p.Name!="prompt" && p.Name!="rawOutput").Select(p=>new JProperty(p.Name,p.Value.DeepClone()))).ToString());
        string oldResult=Path.Combine(oldFolder,"Result.json"),oldEdit=LocalStoryTestArchive.NumberedEditPath(oldResult,report);
        Directory.CreateDirectory(Path.GetDirectoryName(oldEdit));const string human="{\"draft\":[{\"speaker\":\"로키\",\"text\":\"사람이 남긴 수정\"}]}";File.WriteAllText(oldEdit,human);
        File.WriteAllText(Path.Combine(tests,"Index.html"),"<!-- LocalStoryCatalog:v1 -->");
        store.LoadOrCreateProfile();
        Check(Directory.Exists(Path.Combine(root,"Runs","Run_01")) && !Directory.Exists(run),"Run name migration failed");
        Check(store.LoadRunData<GameProgress>(1,"Progress").storedItemIds.Single()=="kept-item","Old inventory lost");
        Check(!File.Exists(legacy),"Old chapter document is still active");
        Check(store.TryLoadGeneratedContent(2,"Encounters",path+"/Story",out EncounterCardRoot generated) && generated.MainStory[0].text==source.MainStory[0].text,"Reviewed old output lost");
        Check(!store.CanReplaceNarrativeContent(2,path,store.NarrativeContentHash(2,path)),"Review became automatic-owned");
        Check(store.LoadNarrativeQueue(1)?.jobs.Count==1,"Old chapter queue lost");
        var results=LocalStoryTestArchive.List(tests);Check(results.Count==1 && results[0].Contains("For_Run_02"),"Old result not regrouped");
        Check(File.ReadAllText(LocalStoryTestArchive.EditPath(results[0]))==human,"Human edit changed during migration");
        Check((string)LocalStoryTestArchive.ReadReport(results[0])["rawOutput"]==raw.ToString(),"Old raw output lost");
        Check(!File.Exists(Path.Combine(tests,"Index.html")),"Generated HTML left active");
        Check(File.Exists(Path.Combine(tests,"Legacy.zip")) && File.Exists(Path.Combine(root,"Migrations","LegacyContent.zip")),"Migration recovery archive missing");
        LocalStoryNotebook.Contexts(store,Array.Empty<string>());LocalStoryNotebook.Refresh(store);
        Check(Directory.GetFiles(Path.Combine(root,"Records"),"*.xlsx").Length==3,"Chapter books missing");
        var stamps=Directory.GetFiles(Path.Combine(root,"Records"),"*.xlsx").Select(File.GetLastWriteTimeUtc).ToArray();LocalStoryNotebook.Refresh(store);
        Check(stamps.SequenceEqual(Directory.GetFiles(Path.Combine(root,"Records"),"*.xlsx").Select(File.GetLastWriteTimeUtc)),"Unchanged workbook rewritten");
        return new {checks,root,humanEditPreserved=true,reviewProtected=true,legacyQueueMigrated=true};
    }
    public static object ChunkIsolation()
    {
        string path=LocalStoryTestInputs.GuidePath,second=path+"/SecondChunk";
        var info=(JObject)ChapterContentCatalog.Info(path).DeepClone();
        var entries=(JArray)ChapterContentCatalog.Info(path).Parent;
        var docs=(System.Collections.Generic.Dictionary<string,JObject>)typeof(ChapterContentCatalog).GetField("documents",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null);
        string storyResource=ChapterContentCatalog.SourceFile(info).Replace(".json","");
        string explainResource=ChapterContentCatalog.SourceFile(info,true).Replace(".json","");
        var story=docs[storyResource];var explain=docs[explainResource];
        string key="guide/SecondChunk";info["path"]=second;info["key"]=key;info["chunkIndex"]=2;info["placeId"]="guide_second";
        entries.Add(info);story[key]=story["guide"].DeepClone();explain[key]=explain["guide"].DeepClone();NarrativeInteractions.ResetCache();
        try
        {
            string root=Path.GetFullPath("tmp/ChunkIsolation/"+Guid.NewGuid().ToString("N"));var store=Isolated(root);
            var a=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));a.MainStory[0].text="자네, 잠시 기다려 보게.";
            var b=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(second));b.MainStory[0].text="이 청크까지 따라왔군. 잠시 쉬게.";
            if(!store.SaveGeneratedContent(2,"Encounters",path+"/Story",a) || !store.SaveGeneratedContent(2,"Encounters",second+"/Story",b)) throw new Exception("Chunk save failed");
            if(!store.TryLoadGeneratedContent(2,"Encounters",path+"/Story",out EncounterCardRoot first) || first.MainStory[0].text!=a.MainStory[0].text ||
                !store.TryLoadGeneratedContent(2,"Encounters",second+"/Story",out EncounterCardRoot other) || other.MainStory[0].text!=b.MainStory[0].text) throw new Exception("Chunk dialogue mixed");
            if(Directory.GetFiles(store.GeneratedNpcDirectory(2,path)).Length!=2) throw new Exception("Per-chunk files grew");
            return new {passed=true,sameNpc=true,distinctChunks=true,twoFiles=true,root};
        }
        finally {ChapterContentCatalog.Reset();}
    }
    public static object NormalizeFolders()
    {
        foreach(var c in ChapterContentCatalog.Chapters)
            foreach(string episode in c["episodes"])
            {
                string dir="Assets/Decision_YYS/Resources/Chapters/"+(string)c["folder"]+"/";
                string old=dir+episode,target=dir+ChapterContentCatalog.EpisodeFolder(episode);
                if(old!=target && AssetDatabase.IsValidFolder(old))
                {string error=AssetDatabase.MoveAsset(old,target);if(!string.IsNullOrEmpty(error)) throw new Exception(error);}
            }
        ChapterContentCatalog.Reset();return new {normalized=true};
    }
    public static object Storage()
    {
        int checks=0;void Check(bool yes,string message) {if(!yes) throw new Exception(message);checks++;}
        string root=Path.GetFullPath("tmp/ChapterLayout/"+Guid.NewGuid().ToString("N"));
        var store=(SaveIOService)Activator.CreateInstance(typeof(SaveIOService),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{root},null);
        string path=LocalStoryTestInputs.GuidePath;
        store.SaveProfile(new ProfileData());
        var source=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
        source.MainStory[0].text="자네, 여기서 잠깐 멈춰 보게.";
        Check(store.SaveGeneratedContent(2,"Encounters",path+"/Story",source),"Save generated changes failed");
        string pack=Path.Combine(store.GeneratedNpcDirectory(2,path),"Story.json");
        Check(File.Exists(pack),"NPC pack missing");
        var doc=JObject.Parse(File.ReadAllText(pack));
        Check(((JArray)doc["stories"][0]["changes"]).Count==1,"Unchanged cards duplicated");
        Check(!File.ReadAllText(pack).Contains("contentJson"),"Full story duplicated in NPC pack");
        Check(store.TryLoadGeneratedContent(2,"Encounters",path+"/Story",out EncounterCardRoot loaded) && loaded.MainStory[0].text==source.MainStory[0].text,"Read changes failed");
        for(int i=0;i<5;i++) Check(store.SaveGeneratedContent(2,"Encounters",path+"/Story",source),"Repeated upsert failed");
        Check(Directory.GetFiles(store.GeneratedNpcDirectory(2,path)).Length==2,"NPC file growth");
        Check(!Directory.Exists(Path.Combine(root,"Narrative","Chapters")),"Old chapter save recreated");
        var job=new LocalStoryDialogueJob {sourceRun=1,targetRun=2,characterId="Guide",storyPath=path,jobId=path,revision=1,
            snapshot=new LocalStoryRevisionRequest {sourceRun=1,targetRun=2,characterId="Guide"}};
        store.SaveNarrativeQueue(new LocalStoryDialogueQueueData {sourceRun=1,targetRun=2,jobs=new System.Collections.Generic.List<LocalStoryDialogueJob>{job}});
        Check(store.LoadNarrativeQueue(1)?.jobs.Count==1,"Grouped queue lost");
        Check(File.Exists(Path.Combine(root,"GeneratedContent","For_Run_02","Queue.json")),"Queue path wrong");
        var progress=new GameProgress {storedItemIds=new System.Collections.Generic.List<string>{"bag"},
            worldInventoryItems=new System.Collections.Generic.List<WorldInventoryItemState>{new WorldInventoryItemState {instanceId="outside-1",itemId="mark",localPosition=new Vector3(1,2,3),localRotation=Quaternion.Euler(0,30,0)}}};
        progress.guideProgress.allocating=true;progress.guideProgress.allocation[1]=3;
        var record=new PlayedEncounterCardRecord {encounterPath=path,placeId="guide",cardIndex=0,card=new Dialogue {text="읽은 대사",type="Next"}};
        progress.guideProgress.runHistory.Add(record);progress.currentEncounterHistory.Add(record);
        store.SaveRunCheckpoint(1,"Progress",progress);
        var restored=store.LoadRunData<GameProgress>(1,"Progress");
        Check(JsonUtility.ToJson(restored)==JsonUtility.ToJson(progress),"Progress round-trip lost inventory/history/allocation");
        string checkpoint=Path.Combine(root,"Runs","Run_01","Progress.json");
        Check(File.Exists(checkpoint) && JObject.Parse(File.ReadAllText(checkpoint))["player"]!=null,"Progress layout wrong");
        string oldCheckpoint=File.ReadAllText(checkpoint);
        progress.guideProgress.runHistory.Add(new PlayedEncounterCardRecord {card=new Dialogue {text="새 대사"}});
        store.SaveRunCheckpoint(1,"Progress",progress);
        File.WriteAllText(checkpoint,oldCheckpoint);
        Check(store.LoadRunData<GameProgress>(1,"Progress").guideProgress.runHistory.Count==1,"Interrupted history write cannot recover prior checkpoint");
        var raw=new JArray(source.MainStory.Select((c,i)=>new JObject { ["index"]=i,["text"]=c.text,["options"]=JArray.FromObject(c.options??Array.Empty<string>()) }));
        var report=JObject.Parse(JsonUtility.ToJson(new LocalStoryTestReport {storyPath=path,sourceRun=1,targetRun=2,model="Gemma",original=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path)),
            generated=source,rawOutput=new JObject { ["cards"]=raw }.ToString(),valid=true,prompt="actual prompt",seconds=1}));
        string result=LocalStoryTestArchive.Save(Path.Combine(root,"LocalStoryTests"),report);
        Check(result.Contains("For_Run_02") && result.Contains("Guide"),"Result grouping wrong");
        Check((string)LocalStoryTestArchive.ReadReport(result)["rawOutput"]==(string)report["rawOutput"] &&
            JToken.DeepEquals(report["original"],LocalStoryTestArchive.ReadReport(result)["original"]),"Raw output/source snapshot lost");
        Check(!Directory.GetFiles(root,"*.html",SearchOption.AllDirectories).Any(),"HTML still generated");
        var view=JObject.Parse(File.ReadAllText(result));
        Check(((JArray)view["draft"]).Count==1,"Result includes unchanged dialogue");
        string edit=LocalStoryTestArchive.EditPath(result);var sparse=JObject.Parse(File.ReadAllText(edit));
        Check(((JArray)sparse["draft"]).Count==1,"Edit includes unchanged dialogue");
        sparse["draft"][0]["text"]="자네, 잠시 이쪽을 보게.";File.WriteAllText(edit,sparse.ToString());
        var full=LocalStoryTestArchive.ReadEditData(edit,report);
        Check(((JArray)full["draft"]).Count==9 && (string)full["draft"][0]["text"]=="자네, 잠시 이쪽을 보게.","Sparse Edit mapping failed");
        return new {checks,root,result,edit};
    }
    public static object Sources()
    {
        ChapterContentCatalog.Reset();
        bool comparedOriginals=File.Exists("tmp/ChapterMigration/Originals.json");
        var originals=comparedOriginals?JObject.Parse(File.ReadAllText("tmp/ChapterMigration/Originals.json")):
            new JObject(ChapterContentCatalog.Stories.Select(e=>new JProperty((string)e["path"],JObject.Parse(NarrativeInteractions.StorySource((string)e["path"])))));
        int cards=0,choices=0;
        foreach(var p in originals.Properties())
        {
            var before=(JObject)p.Value.DeepClone();
            var info=ChapterContentCatalog.Info(p.Name);
            var after=JObject.Parse(NarrativeInteractions.StorySource(p.Name));
            var oldCards=(JArray)before["MainStory"]; var newCards=(JArray)after["MainStory"];
            if(oldCards.Count!=newCards.Count) throw new Exception("Card loss: "+p.Name);
            for(int i=0;i<oldCards.Count;i++)
            {
                // The author's choice classification now lives in the explanation; its prose was updated accordingly.
                if(p.Name==LocalStoryTestInputs.GuidePath && (string)oldCards[i]["eventIdStable"]=="loki_identity_choice")
                    oldCards[i]["coreMeaning"]=newCards[i]["coreMeaning"]?.DeepClone();
                if(oldCards[i]["speakerId"]==null) oldCards[i]["speakerId"]=newCards[i]["speakerId"];
                var actions=oldCards[i]["choiceActions"] as JArray;
                if(actions!=null) for(int j=0;j<actions.Count;j++)
                    if(actions[j]["choiceId"]==null) actions[j]["choiceId"]=newCards[i]["choiceActions"][j]["choiceId"];
                choices+=(oldCards[i]["options"] as JArray)?.Count??0;
            }
            // Compare game objects after the same Unity deserialization used at runtime.
            string a=JsonUtility.ToJson(JsonUtility.FromJson<EncounterCardRoot>(before.ToString()));
            string b=JsonUtility.ToJson(JsonUtility.FromJson<EncounterCardRoot>(after.ToString()));
            if(a!=b) throw new Exception("Changed story/action/explanation: "+p.Name);
            if(!new EncounterContentRepository().TryLoadCards(p.Name,out var loaded,out var error)) throw new Exception(p.Name+": "+error);
            cards+=loaded.MainStory.Count;
        }
        foreach(var c in ChapterContentCatalog.Chapters)
        {
            NarrativeInteractions.Chapter((string)c["id"]);
            foreach(string e in c["episodes"])
                if(new TerrainRepository().Load((string)c["id"]+"/"+e+"/Terrain")==null) throw new Exception("Terrain failed: "+e);
        }
        var root=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(LocalStoryTestInputs.GuidePath));
        var sections=LocalStorySections.Plan(root,2);
        return new {stories=originals.Count,cards,options=choices,guideCards=root.MainStory.Count,comparedOriginals};
    }
    public static object RemoveLegacySources()
    {
        Sources();
        var files=JArray.Parse(File.ReadAllText("tmp/ChapterMigration/Remove.json"));
        int removed=0;
        foreach(string file in files)
        {
            if(!file.StartsWith("Assets/Decision_YYS/Resources/",StringComparison.Ordinal)) throw new Exception("Unexpected removal: "+file);
            if(File.Exists(file) && !AssetDatabase.DeleteAsset(file)) throw new Exception("Cannot remove: "+file);
            removed++;
        }
        foreach(string root in new[]{"Assets/Decision_YYS/Resources/Story_Json_Data","Assets/Decision_YYS/Resources/Narrative"})
            if(Directory.Exists(root))
                foreach(var d in Directory.GetDirectories(root,"*",SearchOption.AllDirectories).OrderByDescending(d=>d.Length))
                    if(Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) AssetDatabase.DeleteAsset(d.Replace('\\','/'));
        AssetDatabase.Refresh();
        return new {removed};
    }
}
