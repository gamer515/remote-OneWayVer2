using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Reusable installation and integration checks; never advance the real player profile.
public static class NarrativeProgressCheck
{
    const string ItemId = "prototype_loki_mark";
    const string ItemPath = "Assets/Decision_YYS/Resources/Inventory/LokiMark.asset";
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static object Inspect()
    {
        return new {
            playing=Application.isPlaying,
            scenes=Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount)
                .Select(i=>UnityEngine.SceneManagement.SceneManager.GetSceneAt(i)).Select(s=>new {s.path,s.isDirty}).ToArray(),
            items=AssetDatabase.FindAssets("t:InventoryItemDefinition").Select(AssetDatabase.GUIDToAssetPath).ToArray(),
            inventories=Resources.FindObjectsOfTypeAll<BackpackInventoryController>().Select(i=>new {i.name,i.gameObject.scene.path}).ToArray()
        };
    }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/'); Folder(parent);
        AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    public static object InstallItem()
    {
        Check(!Application.isPlaying,"Stop Play before installing example assets.");
        Folder("Assets/Decision_YYS/Resources/Inventory");
        const string prefabFolder="Assets/Decision_YYS/Prafabs/Inventory";
        Folder(prefabFolder);
        var definition=AssetDatabase.LoadAssetAtPath<InventoryItemDefinition>(ItemPath);
        if(definition!=null)
        {
            Check(definition.ItemId==ItemId && definition.WorldPrefab!=null,"Existing example item was edited; inspect rather than overwrite it.");
            return new {item=ItemPath,created=false};
        }
        definition=ScriptableObject.CreateInstance<InventoryItemDefinition>();
        AssetDatabase.CreateAsset(definition,ItemPath);
        var fields=new SerializedObject(definition);
        fields.FindProperty("itemId").stringValue=ItemId;
        fields.FindProperty("displayName").stringValue="로키의 표식";
        fields.FindProperty("icon").objectReferenceValue=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        fields.ApplyModifiedPropertiesWithoutUndo();
        const string materialPath="Assets/Decision_YYS/Prafabs/Inventory/LokiMark.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(material==null)
        {
            material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor",new Color(.12f,.65f,.8f));
            AssetDatabase.CreateAsset(material,materialPath);
        }
        var token=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        try
        {
            token.name="LokiMark"; token.transform.localScale=new Vector3(.55f,.06f,.55f);
            token.GetComponent<Renderer>().sharedMaterial=material;
            token.AddComponent<Rigidbody>().mass=.25f;
            var world=token.AddComponent<WorldInventoryItem>();
            var worldFields=new SerializedObject(world);
            worldFields.FindProperty("definition").objectReferenceValue=definition;
            worldFields.ApplyModifiedPropertiesWithoutUndo();
            var prefab=PrefabUtility.SaveAsPrefabAsset(token,"Assets/Decision_YYS/Prafabs/Inventory/LokiMark.prefab");
            fields.Update(); fields.FindProperty("worldPrefab").objectReferenceValue=prefab;
            fields.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition);
        }
        finally { UnityEngine.Object.DestroyImmediate(token); }
        return new {item=ItemPath,created=true};
    }
    static StoryEventRecord Choice(string id, string role, int sequence) => new StoryEventRecord {
        eventKey="choice:"+sequence, sequence=sequence, run=1, chapterId="Initial",episodeId="Initial_01",
        characterId="Guide",characterName="로키",placeId="guide",cardId="loki_identity_choice",
        storyPath=LocalStoryTestInputs.GuidePath,kind="choice",choiceId=id,choiceRole=role,relationshipId="Guide"
    };
    public static object Regression()
    {
        Check(!Application.isPlaying && !LocalStoryRuntimeService.IsBusy,"Run isolated checks while gameplay is stopped.");
        Check(Resources.Load<InventoryItemDefinition>("Inventory/LokiMark")?.ItemId==ItemId,"InstallItem must run first.");
        var field=typeof(SaveIOService).GetField("_instance",BindingFlags.NonPublic|BindingFlags.Static);
        var previous=field.GetValue(null);
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","tmp","NarrativeProgress",Guid.NewGuid().ToString("N")));
        var constructor=typeof(SaveIOService).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(string)},null);
        var storage=(SaveIOService)constructor.Invoke(new object[]{folder});
        string source=ChapterContentCatalog.Asset("Story_Json_Data/"+LocalStoryTestInputs.GuidePath+"/Story").text;
        try
        {
            field.SetValue(null,storage);
            var manager=new SaveManager();
            var positive=Choice("loki_question_god","Positive",1);
            manager.LoadProgress().storyEvents.Add(positive);
            Check(manager.ApplyNarrativeInteractions(positive).Count==1,"Positive choice did not grant item.");
            Check(manager.LoadStoredItemIds().SequenceEqual(new[]{ItemId}),"Reward inventory mismatch.");
            Check(manager.ApplyNarrativeInteractions(positive).Count==0,"Duplicate reward was applied.");
            manager=new SaveManager();
            Check(manager.LoadStoredItemIds().Contains(ItemId),"Item did not survive reload.");
            string followup=manager.LoadProgress().storyEvents.First(e=>e.eventKey==positive.eventKey).resultStoryPath;
            Check(!string.IsNullOrEmpty(followup),"Packed followup path was not persisted.");
            Check(new EncounterContentRepository(1).TryLoadCards(followup,out var packed,out string packedError),packedError);
            Check(packed.MainStory.Count==2&&packed.MainStory[1].choiceActions[0].requiresItems.Contains(ItemId),"Packed scene lost item conditions.");
            Check(new DecisionSaveService(manager).HasAllItems(packed.MainStory[1].choiceActions[0].requiresItems),"Owned item choice is locked.");
            Check(!manager.HasUnlock("prototype:loki_mine_mark_secret"),"Secret leaked before the interaction.");
            var neutral=Choice("loki_question_place","Neutral",4);
            manager.LoadProgress().storyEvents.Add(neutral);
            Check(manager.ApplyNarrativeInteractions(neutral).Count==1,"Owned item did not unlock discovery.");
            Check(manager.HasUnlock("prototype:loki_mine_mark_secret"),"Discovery not persisted.");
            Check(manager.LoadStoredItemIds().Contains(ItemId),"Showing an item consumed it.");
            string[] earned=LocalStoryNotebook.EarnedUnlocks(storage,1);
            var contexts=LocalStoryNotebook.Contexts(storage,earned);
            Check(contexts.Single(c=>c.characterId=="Guide").publicFacts.Contains("오래된 길"),"Discovery is missing from LLM character context.");
            Check(!contexts.Single(c=>c.characterId=="Duelist").publicFacts.Contains("오래된 길"),"Another NPC received private discovery context.");
            LocalStoryDialogueQueue.QueuePaths(storage,1,"Guide",new[]{LocalStoryTestInputs.GuidePath},"isolated example");
            var job=LocalStoryDialogueQueue.Load(storage,1).jobs.Single();
            Check(job.snapshot.npcContexts.Single(c=>c.characterId=="Guide").publicFacts.Contains("오래된 길"),"Queue snapshot lost discovery.");
            Check(job.snapshot.playEvents.Any(e=>e.kind=="item_received"&&e.itemId==ItemId),"LLM audit lost actual item movement.");
            LocalStoryDialogueQueue.QueuePaths(storage,1,"Guide",new[]{followup},"isolated packed scene");
            Check(LocalStoryDialogueQueue.Load(storage,1).jobs.Any(j=>j.storyPath==followup),"Packed scene did not enter the common generation queue.");
            var packedSource=JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(followup));
            Check(LocalStoryRewrite.Messages(followup,packedSource,job.snapshot).Contains("표식"),"Packed scene has no writing context.");
            var insult=Choice("loki_insult","Chaotic",7);
            manager.LoadProgress().storyEvents.Add(insult);
            Check(manager.ApplyNarrativeInteractions(insult).Count==1,"Item was not reclaimed.");
            manager=new SaveManager();
            Check(manager.LoadStoredItemIds().Count==0,"Reclaimed item returned after reload.");
            Check(!new DecisionSaveService(manager).HasAllItems(packed.MainStory[1].choiceActions[0].requiresItems),"Missing item choice stayed unlocked.");
            AssertConfirmedExchangeResume(manager);
            Check(manager.ApplyNarrativeInteractions(insult).Count==0 && manager.ApplyNarrativeInteractions(positive).Count==0,"Exchange can be repeated to farm rewards.");
            Check(manager.LoadProgress().storyEvents.Count(e=>e.kind=="item_received")==1 &&
                manager.LoadProgress().storyEvents.Count(e=>e.kind=="item_returned")==1,"Movement journal duplicates or omissions.");
            var empty=new GameProgress(); string before=JsonUtility.ToJson(empty);
            var secret=NarrativeInteractions.Chapter("Initial").interactions.Single(r=>r.id.EndsWith("_secret"));
            Check(!NarrativeInteractions.TryApply(empty,secret,neutral)&&JsonUtility.ToJson(empty)==before,"Missing-item exchange changed state.");
            // Pending work does not prevent using source; a late result does not mutate an open dialogue.
            Check(LocalStoryDialogueQueue.HasPendingForRun(storage,2),"Pending example job missing.");
            var repository=new EncounterContentRepository(2);
            Check(repository.TryLoadCards(LocalStoryTestInputs.GuidePath,out var opened,out string error),error);
            string openedText=opened.MainStory[0].text;
            var generated=JsonUtility.FromJson<EncounterCardRoot>(source);
            generated.MainStory[0].text="자네, 잠깐 멈춰 보게.";
            storage.SaveGeneratedContent(2,"Encounters",LocalStoryTestInputs.GuidePath+"/Story",generated);
            Check(opened.MainStory[0].text==openedText,"Late generation mutated an open dialogue.");
            Check(repository.TryLoadCards(LocalStoryTestInputs.GuidePath,out var next,out error)&&next.MainStory[0].text==generated.MainStory[0].text,error);
            Check(ChapterContentCatalog.Asset("Story_Json_Data/"+LocalStoryTestInputs.GuidePath+"/Story").text==source,"Canonical dialogue changed.");
            return new {passed=true,isolatedStorage=folder,reward=true,conditionalDiscovery=true,reclaimed=true,
                noDuplicates=true,reload=true,llmContext=true,pendingSourceFallback=true,openDialogueStable=true,realSavesUntouched=true};
        }
        finally { field.SetValue(null,previous); }
    }
    static void AssertConfirmedExchangeResume(SaveManager storage)
    {
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var go=new GameObject("ExchangeResumeCheck"); go.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
        try
        {
            var manager=go.AddComponent<DecisionManager>();
            var stats=go.AddComponent<StatContainer>();
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            void Set(string field,object value)=>typeof(DecisionManager).GetField(field,flags).SetValue(manager,value);
            Set("statContainer",stats); Set("saveService",new DecisionSaveService(storage));
            Set("storyEvents",storage.LoadProgress().storyEvents); Set("storyVisit",7); Set("selectedGearIndex",0);
            var card=new Dialogue {type="Choice",choiceActions=new[]{new StoryChoiceAction {
                choiceId="loki_insult",action="continue",requiresItems=new[]{ItemId}}}};
            bool blocked=(bool)typeof(DecisionManager).GetMethod("ExecuteStoryChoice",flags).Invoke(manager,new object[]{card});
            Check(!blocked,"Restored confirmed exchange became locked after consuming its item.");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }
    public static object Presentation()
    {
        Check(!Application.isPlaying,"Run the preview check with gameplay stopped.");
        var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/Scenes/DecisionScene.unity");
        try
        {
            var roots=scene.GetRootGameObjects();
            var ui=roots.SelectMany(r=>r.GetComponentsInChildren<UiController>(true)).Single();
            var history=roots.SelectMany(r=>r.GetComponentsInChildren<ChatHistoryUi>(true)).Single();
            int count=history.MessageCount;
            ui.AppendInteraction("표식의 무늬를 눈여겨보게.","Guide");
            Check(history.MessageCount==count+1,"Interaction notice did not reach the existing chat.");
            var inventory=roots.SelectMany(r=>r.GetComponentsInChildren<BackpackInventoryController>(true)).Single();
            typeof(BackpackInventoryController).GetMethod("BuildCatalog",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(inventory,null);
            var content=(Transform)new SerializedObject(inventory).FindProperty("content").objectReferenceValue;
            foreach(var child in content.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            inventory.Initialize(new[]{ItemId},null);
            var icon=Resources.Load<InventoryItemDefinition>("Inventory/LokiMark").Icon;
            Check(icon!=null && content.GetComponentsInChildren<UnityEngine.UI.Image>(true).Any(i=>i.enabled && i.sprite==icon),"Item is not visible in existing inventory slots.");
            return new {passed=true,existingChat=true,existingInventory=true,sceneSaved=false};
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }
    public static object InventoryPersistence()
    {
        Check(!Application.isPlaying,"Stop Play before the isolated inventory check.");
        string actualRoot=Path.Combine(Application.persistentDataPath,"Saves");
        string[] Fingerprint()=>Directory.Exists(actualRoot)?Directory.GetFiles(actualRoot,"*.json",SearchOption.AllDirectories).OrderBy(s=>s)
            .Select(s=>s+":"+new FileInfo(s).Length+":"+File.GetLastWriteTimeUtc(s).Ticks).ToArray():Array.Empty<string>();
        string[] before=Fingerprint();
        var singleton=typeof(SaveIOService).GetField("_instance",BindingFlags.NonPublic|BindingFlags.Static);
        var previous=singleton.GetValue(null);
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"..","tmp","InventoryPersistence",Guid.NewGuid().ToString("N")));
        var constructor=typeof(SaveIOService).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(string)},null);
        var storage=(SaveIOService)constructor.Invoke(new object[]{root});
        UnityEngine.SceneManagement.Scene scene=default;
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        BackpackInventoryController Open()
        {
            scene=UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene("Assets/Scenes/DecisionScene.unity");
            var bag=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<BackpackInventoryController>(true)).Single();
            typeof(BackpackInventoryController).GetMethod("BuildCatalog",flags).Invoke(bag,null);
            return bag;
        }
        System.Collections.Generic.Dictionary<string,WorldInventoryItem> Worlds(BackpackInventoryController bag)=>
            (System.Collections.Generic.Dictionary<string,WorldInventoryItem>)typeof(BackpackInventoryController).GetField("worldItems",flags).GetValue(bag);
        void Disable(BackpackInventoryController bag)=>typeof(BackpackInventoryController).GetMethod("OnDisable",flags).Invoke(bag,null);
        void Close() {if(scene.IsValid()) {UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);scene=default;}}
        int checks=0;
        void Assert(bool value,string message){Check(value,message);checks++;}
        try
        {
            singleton.SetValue(null,storage);
            var manager=new SaveManager(); var service=new DecisionSaveService(manager);
            var gift=Choice("loki_question_god","Positive",1);manager.LoadProgress().storyEvents.Add(gift);
            Assert(manager.ApplyNarrativeInteractions(gift).Count==1,"gift failed");
            var bag=Open(); bag.Initialize(manager.LoadInventoryState(),service.SaveInventoryState);
            var board=(Collider)new SerializedObject(bag).FindProperty("boardDropArea").objectReferenceValue;
            Vector3 position=board.bounds.center+Vector3.up*(board.bounds.extents.y+.15f);
            Assert(bag.TryDeploySlot(0,position),"deploy failed");
            var deployed=bag.CaptureState(); string instanceId=deployed.worldItems.Single().instanceId;
            Assert(deployed.storedItemIds.Count==0&&deployed.worldItems.Count==1,"deployed ownership lost/duplicated");
            Assert(service.HasAllItems(new[]{ItemId}),"deployed item no longer satisfies ownership condition");
            Assert(new SaveManager().LoadInventoryState().worldItems.Single().instanceId==instanceId,"deploy was not immediately saved");
            var item=Worlds(bag)[instanceId];
            Assert(Vector3.Distance(item.transform.lossyScale,Resources.Load<InventoryItemDefinition>("Inventory/LokiMark").WorldPrefab.transform.localScale)<.0001f,"board scale distorted the item");
            item.transform.position+=new Vector3(.04f,.02f,.03f);item.transform.rotation=Quaternion.Euler(12,34,56);
            position=item.transform.position; Quaternion rotation=item.transform.rotation;
            Disable(bag);Close(); // Exercise the same hook as shutdown without playing the user's save.
            manager=new SaveManager();service=new DecisionSaveService(manager);bag=Open();
            var restored=manager.LoadInventoryState();bag.Initialize(restored,service.SaveInventoryState);item=Worlds(bag)[instanceId];
            Assert(Vector3.Distance(item.transform.position,position)<.0001f&&Quaternion.Angle(item.transform.rotation,rotation)<.01f,"board pose did not survive shutdown/reload");
            bag.Initialize(manager.LoadInventoryState(),service.SaveInventoryState);
            Assert(Worlds(bag).Count==1&&bag.CaptureState().OwnedItemIds().Count==1,"refresh duplicates deployed items");
            var body=item.GetComponent<Rigidbody>();body.isKinematic=true;
            typeof(BackpackInventoryController).GetMethod("BeginWorldDrag",flags).Invoke(bag,new object[]{item,Vector2.zero});
            item.transform.position+=Vector3.one*10;
            Assert(Vector3.Distance(bag.CaptureState().worldItems.Single().localPosition,restored.worldItems.Single().localPosition)<.0001f,"drag cursor was committed as the item's position");
            Disable(bag);
            Assert(Vector3.Distance(item.transform.position,position)<.0001f&&body.isKinematic,"cancelled drag lost position/kinematic state");
            Assert(bag.TryStoreWorldItem(item),"return to bag failed");
            Assert(manager.LoadInventoryState().storedItemIds.SequenceEqual(new[]{ItemId})&&manager.LoadInventoryState().worldItems.Count==0,"pickup lost/duplicated item");
            Assert(bag.TryDeploySlot(0,position),"redeploy failed");
            var show=Choice("prototype_mark_show","Neutral",2);manager.LoadProgress().storyEvents.Add(show);
            Assert(manager.ApplyNarrativeInteractions(show).Count==1&&manager.LoadInventoryState().worldItems.Count==1,"showing a board item failed/consumed it");
            bag.Initialize(manager.LoadInventoryState(),service.SaveInventoryState);
            var giveBack=Choice("prototype_mark_return","Neutral",3);manager.LoadProgress().storyEvents.Add(giveBack);
            Assert(manager.ApplyNarrativeInteractions(giveBack).Count==1,"NPC cannot reclaim the board item");
            bag.Initialize(manager.LoadInventoryState(),service.SaveInventoryState);
            Assert(Worlds(bag).Count==0&&!service.HasAllItems(new[]{ItemId}),"reclaimed board model/ownership remains");
            Assert(new SaveManager().LoadInventoryState().OwnedItemIds().Count==0,"consumed item resurrected after reload");
            Close();

            // Old buggy saves: a receipt without a matching outgoing movement is recoverable once.
            var legacy=new GameProgress {runSeed=42};
            legacy.storyEvents.Add(new StoryEventRecord {eventKey="receipt",kind="item_received",itemId=ItemId,quantity=1});
            storage.SaveRunCheckpoint(1,"Progress",legacy);
            string progressFile=Path.Combine(root,"Runs","Run_01","Progress.json");
            var json=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(progressFile));((Newtonsoft.Json.Linq.JObject)json["player"]).Remove("inventoryStateVersion");((Newtonsoft.Json.Linq.JObject)json["player"]).Remove("worldInventoryItems");
            File.WriteAllText(progressFile,json.ToString());
            manager=new SaveManager();
            Assert(manager.LoadInventoryState().storedItemIds.SequenceEqual(new[]{ItemId}),"old missing receipt was not recovered");
            Assert(new SaveManager().LoadInventoryState().OwnedItemIds().Count==1,"legacy recovery duplicated the receipt");
            Assert(manager.LoadProgress().storyEvents.Count==1,"legacy recovery fabricated play events");
            legacy.storyEvents.Add(new StoryEventRecord {eventKey="return",kind="item_returned",itemId=ItemId,quantity=1});
            storage.SaveRunCheckpoint(1,"Progress",legacy);manager=new SaveManager();
            Assert(manager.LoadInventoryState().OwnedItemIds().Count==0,"legacy recovery resurrected an actual return");
            legacy.storyEvents.Clear();storage.SaveRunCheckpoint(1,"Progress",legacy);manager=new SaveManager();
            Assert(manager.LoadInventoryState().OwnedItemIds().Count==0,"legacy recovery invented an unrecorded item");

            // A stale scene callback must not carry the previous run's inventory into the new run.
            service=new DecisionSaveService(manager);manager.StartNextRun();
            service.SaveInventoryState(new InventoryState {storedItemIds=new System.Collections.Generic.List<string>{ItemId}});
            Assert(manager.LoadInventoryState().OwnedItemIds().Count==0,"old scene shutdown contaminated the new run");

            // A missing world prefab or failed checkpoint must keep the item recoverable.
            bag=Open();bag.Initialize(new[]{ItemId},null);
            var broken=ScriptableObject.CreateInstance<InventoryItemDefinition>();
            var fields=new SerializedObject(broken);fields.FindProperty("itemId").stringValue=ItemId;fields.ApplyModifiedPropertiesWithoutUndo();
            var catalog=(System.Collections.Generic.Dictionary<string,InventoryItemDefinition>)typeof(BackpackInventoryController).GetField("definitions",flags).GetValue(bag);
            var real=catalog[ItemId];catalog[ItemId]=broken;
            Assert(!bag.TryDeploySlot(0,position)&&bag.CaptureState().storedItemIds.Count==1,"invalid prefab deleted the bag item");
            catalog[ItemId]=real;UnityEngine.Object.DestroyImmediate(broken);
            bag.Initialize(new InventoryState {storedItemIds=new System.Collections.Generic.List<string>{ItemId}},_=>throw new IOException("isolated checkpoint failure"));
            Assert(!bag.TryDeploySlot(0,position)&&bag.CaptureState().storedItemIds.Count==1&&bag.CaptureState().worldItems.Count==0,"failed save lost an item instead of rolling back");
            bag.Initialize(new InventoryState {worldItems=new System.Collections.Generic.List<WorldInventoryItemState>{new WorldInventoryItemState {
                instanceId="unknown",itemId="missing_catalog_item",localRotation=Quaternion.identity}}},null);
            Assert(bag.CaptureState().worldItems.Single().itemId=="missing_catalog_item","unknown saved item was discarded");
            Close();
            Assert(before.SequenceEqual(Fingerprint()),"inventory regression modified actual player saves");
            return new {passed=true,checks,isolatedStorage=root,boardPoseRestored=true,boardOwnership=true,reclaim=true,
                oldMissingItemRecovery=true,noDuplicates=true,failedWriteRollback=true,realSavesUntouched=true};
        }
        finally {Close();singleton.SetValue(null,previous);}
    }
    public static async Task<object> SessionSmoke()
    {
        Check(!Application.isPlaying && !LocalStoryRuntimeService.IsBusy,"Run the native smoke check while gameplay is stopped.");
        LocalStoryLifetime.CancelAndWait();
        string root=Path.Combine(Application.streamingAssetsPath,"LocalStory");
        string modelPath=LocalStoryTestModels.PathFor(root,LocalStoryTestModel.Gemma);
        string messages="[{\"role\":\"user\",\"content\":\"지정된 JSON만 출력해.\"}]";
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var first=await LocalStoryInference.Generate(root,messages,"root ::= \"{\\\"step\\\":1}\"",12345,budget.Token,null,LocalStorySampling.Comparison(),modelPath);
            var second=await LocalStoryInference.Generate(root,messages,"root ::= \"{\\\"step\\\":2}\"",12345,budget.Token,null,LocalStorySampling.Proofreading(),modelPath);
            Check(first.json.Trim()=="{\"step\":1}"&&second.json.Trim()=="{\"step\":2}","Session leaked the previous output or grammar.");
            Check(!first.sessionReused&&second.sessionReused&&second.modelLoadSeconds==0,"Model was reloaded between calls.");
            Check(LocalStoryInference.HasResidentModel,"Successful session was not retained.");
            bool cancelled=false;
            using(var stop=CancellationTokenSource.CreateLinkedTokenSource(budget.Token))
            {
                Task stopTask=null;
                try
                {
                    await LocalStoryInference.Generate(root,messages,"root ::= \"{\\\"cancel\\\":true}\"",12345,stop.Token,
                        text=>{if(!string.IsNullOrEmpty(text)&&stopTask==null) stopTask=Task.Run(()=>stop.Cancel());},LocalStorySampling.Comparison(),modelPath);
                }
                catch(OperationCanceledException) { cancelled=true; }
                finally { if(stopTask!=null) await stopTask; }
            }
            Check(cancelled&&!LocalStoryInference.HasResidentModel,"Cancelled native session was retained.");
            var resumed=await LocalStoryInference.Generate(root,messages,"root ::= \"{\\\"step\\\":3}\"",12345,budget.Token,null,LocalStorySampling.Comparison(),modelPath);
            Check(!resumed.sessionReused&&resumed.json.Trim()=="{\"step\":3}","Generation did not recover after cancellation.");
            LocalStoryInference.ReleaseIfIdle(TimeSpan.Zero); LocalStoryLifetime.WaitForWorkers();
            Check(!LocalStoryInference.HasResidentModel,"Idle session was not released.");
            return new {passed=true,model="Gemma",first,second,cancellationRecovered=true,idleReleased=true};
        }
        finally { LocalStoryLifetime.CancelAndWait(); }
    }
}
