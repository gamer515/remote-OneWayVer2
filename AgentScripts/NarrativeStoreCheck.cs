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

// Integration fixtures, never actual player progression or native inference.
public static class NarrativeStoreCheck
{
    static int checks;
    const string Mark = "Initial/Initial_01/Encounters/guide/Mark";
    static readonly string Guide = LocalStoryTestInputs.GuidePath;
    static void Check(bool valid, string error) { if (!valid) throw new Exception(error); checks++; }
    static SaveIOService Storage(string root) => (SaveIOService)typeof(SaveIOService).GetConstructor(
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {typeof(string)}, null).Invoke(new object[] {root});
    static EncounterCardRoot Source(string path, string first = null)
    {
        var root = JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
        if (first != null) root.MainStory[0].text = first;
        return root;
    }
    static NarrativeChapterSave Read(SaveIOService store, string chapter = "Initial") =>
        (NarrativeChapterSave)typeof(SaveIOService).GetMethod("ReadNarrativeChapter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(store,new object[]{chapter});
    static LocalStoryDialogueJob Job(int run, string path) => new LocalStoryDialogueJob {
        jobId = path, storyPath = path, characterId = "Guide", chapterId = path.Split('/')[0], sourceRun = run, targetRun = run + 1,
        revision = 1, inputHash = "fixture", snapshot = new LocalStoryRevisionRequest {sourceRun = run, targetRun = run + 1, characterId = "Guide"}
    };
    static string[] ActualSaveFingerprint()
    {
        string root = Path.Combine(Application.persistentDataPath, "Saves");
        return Directory.Exists(root) ? Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(s => s)
            .Select(s => s + ":" + new FileInfo(s).Length + ":" + File.GetLastWriteTimeUtc(s).Ticks).ToArray() : Array.Empty<string>();
    }
    public static object Regression()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        checks = 0;
        string[] actual = ActualSaveFingerprint();
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tmp", "NarrativeStore", Guid.NewGuid().ToString("N")));
        var store = Storage(root);
        var singleton = typeof(SaveIOService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
        var previous = singleton.GetValue(null);
        string rawSource = NarrativeInteractions.StorySource(Guide);
        try
        {
            singleton.SetValue(null, store);
            store.SaveProfile(new ProfileData {currentRun = 1});
            var story = Source(Guide, "자네, 잠깐 멈춰 보게.");
            Check(store.SaveGeneratedContent(2, "Encounters", Guide + "/Story", story), "grouped save failed");
            Check(store.SaveGeneratedContent(2, "Encounters", Mark + "/Story", Source(Mark)), "packed scene save failed");
            for (int i = 0; i < 12; i++) Check(store.SaveGeneratedContent(2, "Encounters", Guide + "/Story", story), "upsert failed");
            Check(Directory.GetFiles(store.GeneratedNpcDirectory(2,Guide)).Length == 2 && Read(store).stories.Count == 2, "per-section file/revision growth");
            Check(!Directory.Exists(store.NarrativeDirectory), "new encounter still writes loose files");
            var entry = Read(store).stories.Single(e => e.storyPath == Mark);
            Check(entry.chapterId == "Initial" && entry.episodeId == "Initial_01" && entry.characterId == "Guide" &&
                entry.sceneId == "loki_mark_exchange" && entry.sectionId == "item_exchange", "chapter/NPC/scene/section identity lost");
            Check(entry.sourceHash == LocalStoryDialogueQueue.Hash(NarrativeInteractions.StorySource(Mark)), "source identity missing");
            Check(Storage(root).TryLoadGeneratedContent(2, "Encounters", Guide + "/Story", out EncounterCardRoot reloaded) &&
                reloaded.MainStory[0].text == story.MainStory[0].text, "fresh service cannot read pack");
            var oldPolicyDocument=JObject.Parse(File.ReadAllText(Path.Combine(store.GeneratedNpcDirectory(2,Guide),"Story.json")));
            foreach(JObject saved in (JArray)oldPolicyDocument["stories"])
            {saved["authoredPolicyHash"]=saved["generationPolicyHash"].DeepClone();saved.Remove("generationPolicyHash");}
            File.WriteAllText(Path.Combine(store.GeneratedNpcDirectory(2,Guide),"Story.json"),oldPolicyDocument.ToString());
            string beforePolicyRead=File.ReadAllText(Path.Combine(store.GeneratedNpcDirectory(2,Guide),"Story.json"));
            Check(Storage(root).TryLoadGeneratedContent(2,"Encounters",Guide+"/Story",out reloaded),"old serialized policy field cannot be read");
            Check(File.ReadAllText(Path.Combine(store.GeneratedNpcDirectory(2,Guide),"Story.json"))==beforePolicyRead,"policy-field read rewrote the chapter file");
            string owned = store.NarrativeContentHash(2, Guide);
            Check(store.CanReplaceNarrativeContent(2, Guide, owned), "owned result not replaceable");
            Check(!store.CanReplaceNarrativeContent(2, Guide, "other-owner"), "foreign result replaceable");
            var changedAnchor=Source(Guide,story.MainStory[0].text); changedAnchor.MainStory[1].text="왕국 전용 광산이니 허락 없이 드나들 곳은 아니지.";
            Check(store.SaveGeneratedContent(2,"Encounters",Guide+"/Story",changedAnchor),"runtime still locks the warning wording");
            var changedAction=Source(Guide); changedAction.MainStory[3].guideAction.amount=999;
            Check(!store.SaveGeneratedContent(2,"Encounters",Guide+"/Story",changedAction),"generated content changed executable action amounts");
            Check(store.TryLoadGeneratedContent(2,"Encounters",Guide+"/Story",out reloaded)&&reloaded.MainStory[1].text==changedAnchor.MainStory[1].text,
                "rejected action change erased the last good result");

            // Reopened gameplay gets its saved scene even when a late result arrives during the interruption.
            var repo = new EncounterContentRepository(2);
            Check(repo.TryLoadCards(Guide, out var opened, out var error), error);
            var pin = repo.Capture("guide_a", Guide, opened);
            var progress = new GameProgress(); progress.guideProgress.activeDialogue = pin;
            store.SaveRunCheckpoint(2, "Progress", progress);
            Check(store.SaveGeneratedContent(2, "Encounters", Guide + "/Story", Source(Guide, "어이, 자네. 잠시 멈춰 보게.")), "late result save failed");
            var restored = Storage(root).LoadRunData<GameProgress>(2, "Progress").guideProgress.activeDialogue;
            restored.sourceHash=LocalStoryDialogueQueue.Hash("previous generation-policy signature");
            Check(new EncounterContentRepository(2).TryLoadCards(Guide, out var resumed, out error, snapshot: restored), error);
            Check(resumed.MainStory[0].text == opened.MainStory[0].text, "restart switched an open scene");
            Check(new EncounterContentRepository(2).TryLoadCards(Guide, out var fresh, out error) && fresh.MainStory[0].text != resumed.MainStory[0].text,
                "new encounter cannot see new result");
            restored.cards.MainStory[0].text = "broken snapshot";
            Check(new EncounterContentRepository(2).TryLoadCards(Guide, out fresh, out error, snapshot: restored) && !fresh.MainStory[0].text.Contains("broken"),
                "damaged snapshot accepted");
            var badBindings=repo.Capture("guide_a",Guide,opened);
            badBindings.cards.MainStory[3].guideAction.amount=999;
            badBindings.contentHash=LocalStoryDialogueQueue.Hash(JsonUtility.ToJson(badBindings.cards));
            Check(new EncounterContentRepository(2).TryLoadCards(Guide,out fresh,out error,snapshot:badBindings)&&
                fresh.MainStory[3].guideAction.amount!=999,"old policy snapshot bypassed actual action validation");

            // Manual promotions are explicit, protected, and have one recoverable previous version.
            string beforeReview = fresh.MainStory[0].text;
            Check(store.SaveGeneratedContentForTest(2, "Encounters", Guide + "/Story", Source(Guide, "자네, 할 이야기가 있네. 잠깐 멈춰 보게.")), "review promotion failed");
            Check(!store.CanReplaceNarrativeContent(2, Guide, store.NarrativeContentHash(2, Guide)), "review became runtime-owned");
            Check(!store.SaveGeneratedContent(2, "Encounters", Guide + "/Story", story), "runtime overwrote review");
            Check(store.SetNarrativeContentEnabled(2, Guide, false), "disable failed");
            Check(!store.TryLoadGeneratedContent(2, "Encounters", Guide + "/Story", out EncounterCardRoot disabled), "disabled loaded");
            Check(new EncounterContentRepository(2).TryLoadCards(Guide, out fresh, out error) && fresh.MainStory[0].text == Source(Guide).MainStory[0].text,
                "disabled fallback failed");
            Check(store.SetNarrativeContentEnabled(2, Guide, true, restorePrevious: true), "previous version restore failed");
            Check(store.TryLoadGeneratedContent(2, "Encounters", Guide + "/Story", out reloaded) && reloaded.MainStory[0].text == beforeReview,
                "wrong previous version restored");

            // Queues share the chapter documents; combining chapters preserves NPC work and finalization.
            var queue = new LocalStoryDialogueQueueData {sourceRun = 1, targetRun = 2};
            queue.jobs.Add(Job(1, Guide)); queue.jobs.Add(Job(1, Mark)); queue.jobs.Add(Job(1, "Chapter_1/Chapter_1_01/Encounters/chapter_1_chunk_1_instructor_0"));
            store.SaveNarrativeQueue(queue);
            var loaded = Storage(root).LoadNarrativeQueue(1);
            Check(loaded.jobs.Count == 3 && !loaded.finalized, "multi-chapter queue merge failed");
            Check(loaded.jobs.Single(j=>j.storyPath==Mark).sceneId=="loki_mark_exchange" &&
                loaded.jobs.Single(j=>j.storyPath==Mark).sectionId=="item_exchange", "queue scene identity missing");
            Check(!File.Exists(Path.Combine(root, "Runs", "Run_0001", "LocalStoryDialogueQueue.json")), "new queue still loose");
            queue.finalized = true; store.SaveNarrativeQueue(queue);
            Check(Storage(root).LoadNarrativeQueue(1).finalized, "finalization lost");
            Check(store.LoadLatestLocalStoryRevision()?.sourceRun == 1, "test panel lost current input");

            // Lazy legacy migration: read-back + archive byte verification precede removing any old file.
            var legacy = Storage(root + "_legacy");
            string old = Path.Combine(legacy.StorageRoot, "GeneratedContent", "For_Run_0002", "Encounters", Guide.Replace('/', Path.DirectorySeparatorChar), "Story.json");
            Directory.CreateDirectory(Path.GetDirectoryName(old)); File.WriteAllText(old, JsonUtility.ToJson(story, true) + "\n ");
            byte[] originalBytes = File.ReadAllBytes(old);
            Check(legacy.TryLoadGeneratedContent(2, "Encounters", Guide + "/Story", out reloaded), "legacy migration did not load");
            Check(!File.Exists(old) && Read(legacy).stories.Single().origin == "Legacy", "legacy left as second live file");
            using (var stream = File.OpenRead(Path.Combine(legacy.StorageRoot, "Migrations", "LegacyContent.zip")))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            using (var copy = new MemoryStream())
            {
                using (var input = zip.Entries.Single().Open()) input.CopyTo(copy);
                Check(originalBytes.SequenceEqual(copy.ToArray()), "migration backup bytes changed");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(old));
            File.WriteAllText(old, JsonUtility.ToJson(story));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(old), "Story.disabled.json"), JsonUtility.ToJson(story));
            Check(!legacy.TryLoadGeneratedContent(2, "Encounters", Guide + "/Story", out disabled), "legacy disabled preference lost");
            Check(!File.Exists(old) && !File.Exists(Path.Combine(Path.GetDirectoryName(old), "Story.disabled.json")), "dual legacy not archived");
            var oldQueue = new LocalStoryDialogueQueueData {sourceRun = 1, targetRun = 2}; oldQueue.jobs.Add(Job(1, Guide));
            legacy.SaveRunCheckpoint(1, LocalStoryDialogueQueue.FileName, oldQueue);
            Check(legacy.LoadNarrativeQueue(1).jobs.Count == 1, "legacy queue unreadable");
            legacy.SaveNarrativeQueue(legacy.LoadNarrativeQueue(1));
            Check(!File.Exists(Path.Combine(legacy.StorageRoot, "Runs", "Run_0001", LocalStoryDialogueQueue.FileName + ".json")) &&
                Storage(legacy.StorageRoot).LoadNarrativeQueue(1).jobs.Count == 1, "legacy queue migration lost work");

            // A partial cross-chapter migration still merges every not-yet-migrated legacy job.
            var partial = Storage(root + "_partial");
            var partialQueue = new LocalStoryDialogueQueueData {sourceRun = 1, targetRun = 2}; partialQueue.jobs.Add(Job(1, Guide));
            partial.SaveNarrativeQueue(partialQueue);
            partial.SaveRunCheckpoint(1, LocalStoryDialogueQueue.FileName, queue);
            Check(Storage(partial.StorageRoot).LoadNarrativeQueue(1).jobs.Count == 3, "partial migration hid another chapter's job");

            // Failure to create the recovery archive must never remove the old live file.
            var lockedArchive = Storage(root + "_archive_failure");
            string unarchived = Path.Combine(lockedArchive.StorageRoot, "GeneratedContent", "For_Run_0002", "Encounters", Guide.Replace('/', Path.DirectorySeparatorChar), "Story.json");
            Directory.CreateDirectory(Path.GetDirectoryName(unarchived)); File.WriteAllBytes(unarchived, originalBytes);
            Directory.CreateDirectory(Path.Combine(lockedArchive.StorageRoot, "Migrations", "LegacyContent.zip"));
            Check(!lockedArchive.TryLoadGeneratedContent(2, "Encounters", Guide + "/Story", out reloaded), "failed migration reported ready");
            Check(File.Exists(unarchived) && originalBytes.SequenceEqual(File.ReadAllBytes(unarchived)), "archive failure deleted old content");

            // Automatic generation storage is bounded by the real current run, with one upcoming run.
            var retained = Storage(root + "_retention"); retained.SaveProfile(new ProfileData {currentRun = 1});
            Check(retained.SaveGeneratedContentForTest(2, "Encounters", Guide + "/Story", story), "retained review failed");
            for (int run = 2; run <= 13; run++) Check(retained.SaveGeneratedContent(run, "Encounters", Mark + "/Story", Source(Mark)), "retention fixture failed");
            retained.SaveProfile(new ProfileData {currentRun = 12});
            Check(retained.SaveGeneratedContent(13, "Encounters", Mark + "/Story", Source(Mark)), "compaction failed");
            Check(Read(retained).stories.Count(e => e.origin == "Runtime") == SaveIOService.NarrativeRetainedRuns + 1, "automatic variants grew unbounded");
            Check(Read(retained).stories.Any(e => e.targetRun == 2 && e.origin == "Review"), "compaction deleted review");
            Check(File.Exists(Path.Combine(root, "Runs", "Run_02", "Progress.json")), "compaction deleted player save");

            // Source mismatch, content corruption and whole-file damage fall back, without overwriting evidence.
            var bad = Storage(root + "_bad");
            Check(bad.SaveGeneratedContent(2, "Encounters", Guide + "/Story", story), "bad fixture failed");
            string file = Path.Combine(bad.GeneratedNpcDirectory(2,Guide),"Story.json"); var doc = JObject.Parse(File.ReadAllText(file));
            doc["stories"][0]["sourceHash"] = "obsolete-source"; File.WriteAllText(file, doc.ToString());
            Check(!Storage(bad.StorageRoot).TryLoadGeneratedContent(2, "Encounters", Guide + "/Story", out reloaded), "obsolete source accepted");
            doc["stories"][0]["contentJson"] = JsonUtility.ToJson(Source(Guide, "tampered content")); File.WriteAllText(file, doc.ToString());
            Check(!Storage(bad.StorageRoot).CanReplaceNarrativeContent(2, Guide, (string)doc["stories"][0]["contentHash"]), "tampered content auto-overwritten");
            File.WriteAllText(file, "{broken");
            Check(!Storage(bad.StorageRoot).TryLoadGeneratedContent(2, "Encounters", Guide + "/Story", out reloaded), "broken pack accepted");
            Check(!Storage(bad.StorageRoot).SaveGeneratedContent(2, "Encounters", Guide + "/Story", story) && File.ReadAllText(file) == "{broken",
                "broken pack erased by empty recovery");
            Check(NarrativeInteractions.StorySource(Guide) == rawSource, "canonical source changed");
            Check(actual.SequenceEqual(ActualSaveFingerprint()), "actual saves changed");
            return new {passed = true, checks, isolatedStorage = root, groupedStories = true, groupedQueues = true,
                migrationByteVerified = true, boundedAutomaticVariants = true, reviewProtected = true, restartDialogueStable = true, realSavesUntouched = true};
        }
        finally { singleton.SetValue(null, previous); }
    }
    // Read-only inspection for the current user's real save; no migration or play advancement.
    public static object Inspect()
    {
        string directory = SaveIOService.Instance.NarrativeDirectory;
        return new {directory, chapters = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json").Select(file => {
            var chapter = JObject.Parse(File.ReadAllText(file));
            return new {file, chapterId = (string)chapter["chapterId"], stories = chapter["stories"].Select(e => new {
                targetRun = (int)e["targetRun"], characterId = (string)e["characterId"], sceneId = (string)e["sceneId"],
                sectionId = (string)e["sectionId"], storyPath = (string)e["storyPath"], enabled = (bool)e["enabled"], origin = (string)e["origin"]}).ToArray(),
                queues = chapter["queues"].Count()};
        }).ToArray() : null};
    }
    public static object SetEnabled(int run, string storyPath, bool enabled)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        bool changed = SaveIOService.Instance.SetNarrativeContentEnabled(run, storyPath, enabled);
        return new {changed, run, storyPath, enabled, appliesOnNextEncounter = true, currentSavedDialoguePreserved = true};
    }
    public static object RestorePrevious(int run, string storyPath)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        bool restored = SaveIOService.Instance.SetNarrativeContentEnabled(run, storyPath, true, restorePrevious: true);
        return new {restored, run, storyPath, appliesOnNextEncounter = true, currentSavedDialoguePreserved = true};
    }
    static string SectionPatch(EncounterCardRoot root) => new JObject { ["cards"] = new JArray(root.MainStory.Select((c,i) =>
        new JObject {["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())})) }.ToString(Newtonsoft.Json.Formatting.None);
    static LocalStoryTestReport SectionReport() => new LocalStoryTestReport {
        storyPath=Guide,original=Source(Guide),seed=12345,model="Gemma",writingPolicy=LocalStoryRewrite.WritingPolicy,
        interactionMemory=new LocalStoryRevisionRequest {sourceRun=1,targetRun=2,chapterId="Initial",characterId="Guide",memoryMode="Dream",
            dreamMemories=new List<LocalStoryDreamMemory>{new LocalStoryDreamMemory {characterId="Guide",impression="어딘가 편안한 꿈 같은 인상"}},
            playEvents=new List<StoryEventRecord>{new StoryEventRecord {displayedText="FULL_AUDIT_MUST_NOT_ENTER_PROMPT"}},
            npcContexts=new[]{new LocalStoryNpcContext {characterId="Guide",name="로키",personality="장난스럽고 능청스럽다."},
                new LocalStoryNpcContext {characterId="Duelist",publicFacts="OTHER_NPC_PRIVATE_FACT"}}}
    };
    public static async Task<object> SectionsRegression()
    {
        var composition=LocalStoryPrompt.Policy["composition"];bool prior=(bool)composition["useEditedExamples"];
        composition["useEditedExamples"]=true;
        try{return await SectionsWithEditedExamples();}
        finally{composition["useEditedExamples"]=prior;}
    }
    private static async Task<object> SectionsWithEditedExamples()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        checks=0; string[] actual=ActualSaveFingerprint();
        string root=Path.GetFullPath(Path.Combine(Application.dataPath,"..","tmp","NarrativeSections",Guid.NewGuid().ToString("N")));
        var store=Storage(root); string original=NarrativeInteractions.StorySource(Guide);
        var report=SectionReport();
        var plan=LocalStoryPrompt.Plan(Guide,report.original);
        int nativeSections=plan.Count;
        Check(plan.Sum(p=>p.count)==report.original.MainStory.Count && plan.All(p=>p.count<=LocalStoryPrompt.Limits(Guide).maxCards),"section plan loses cards");
        Check(plan.SelectMany(p=>Enumerable.Range(p.start,p.count)).SequenceEqual(Enumerable.Range(0,report.original.MainStory.Count)),"section plan overlaps");
        int calls=0;
        Func<LocalStorySectionInput,bool,CancellationToken,Action<string>,Task<LocalStoryModelOutput>> good=(input,correction,ct,progress)=> {
            ct.ThrowIfCancellationRequested(); calls++;
            var source=LocalStorySections.Slice(report.original,input.start,input.count);
            foreach(var card in source.MainStory)
            {
                card.text+=" …";
                if(card.options!=null) card.options=card.options.Select(s=>s+" …").ToArray();
            }
            string patch=SectionPatch(source); progress(patch);
            return Task.FromResult(new LocalStoryModelOutput {json=patch,backend="IsolatedFake",sessionReused=calls>1,
                modelLoadSeconds=calls==1?1:0,generationSeconds=.1,firstTextSeconds=.02});
        };
        await LocalStorySections.Generate(report,CancellationToken.None,good);
        Check(report.valid && !report.correctionAttempted && report.sectionsGenerated==nativeSections && calls==nativeSections,"successful sections repeated full correction");
        Check(report.sectionInputs.First(s=>s.start>0).prompt.Contains("…"),"generated preceding context lost");
        Check(report.sectionInputs.Where(s=>s.count>0).All(s=>!s.prompt.Contains("FULL_AUDIT_MUST_NOT_ENTER_PROMPT")&&!s.prompt.Contains("OTHER_NPC_PRIVATE_FACT")),"private or full audit leaked into short prompt");
        Check(report.sectionInputs.Where(s=>s.count>0).All(s=>s.prompt.Contains("꿈 같은 인상")&&s.maxTokens<1800),"memory or bounded token budget lost");
        Check(report.sectionInputs.Where(s=>s.count>0&&report.original.MainStory[s.start].semanticText!=null).All(s=>!s.prompt.Contains("1번 긍정")),"attitude legend leaked into garbling");
        Check(report.draftModelLoadSeconds==1 && Math.Abs(report.draftGenerationSeconds-.1*nativeSections)<.001,"section performance aggregation wrong");
        Check(report.authoredCards==0&&report.sectionInputs.Sum(s=>s.count)==9&&report.changedCards==9,
            "runtime still excludes warning, action, identity, or choice cards");
        Check(report.generated.MainStory[4].options.Zip(report.original.MainStory[4].options,(a,b)=>a!=b).All(v=>v)&&
            report.generated.MainStory[8].options.Zip(report.original.MainStory[8].options,(a,b)=>a!=b).All(v=>v),"choice wording is still copied from the source");
        Check(LocalStoryTestPanel.DisplayOutput(report).Contains("전체 카드 생성")&&
            !LocalStoryTestPanel.DisplayRawOutput(report).Contains("모델 호출 없음"),"viewer hides excluded cards");
        Check(LocalStoryRewrite.SameBindings(report.original,report.generated),"full generation changed an action or scene definition");
        Check(JsonUtility.ToJson(report.generated.MainStory.Last().choiceActions)==JsonUtility.ToJson(report.original.MainStory.Last().choiceActions),"section writer changed game bindings");
        string archived=store.SaveLocalStoryTest(report);
        var archivedReport=LocalStoryTestArchive.ReadReport(archived);
        Check((int)archivedReport["sectionsGenerated"]==plan.Count && archivedReport["sectionInputs"]==null && archivedReport["sectionOutputs"]==null &&
            !Directory.GetFiles(Path.Combine(store.StorageRoot,"LocalStoryTests"),"Evidence.zip",SearchOption.AllDirectories).Any(),"compact result still duplicates section evidence");

        string editFile=LocalStoryTestArchive.EditPath(archived), evidence=File.ReadAllText(archived);
        var edit=JObject.Parse(File.ReadAllText(editFile));
        Check(LocalStoryRuntimePipeline.RuntimeExamples(Guide,store,out _,out var snippets,12345)==""&&snippets.Length==0,"unedited AI became a runtime reference");
        edit["draft"][0]["text"]="이보게, 그리 급히 가나? 잠깐 이야기 좀 하지.";
        File.WriteAllText(editFile,edit.ToString());
        string humanEdit=File.ReadAllText(editFile);
        string reference=LocalStoryRuntimePipeline.RuntimeExamples(Guide,store,out _,out snippets,12345);
        Check(snippets.Length==1&&snippets[0].Contains("draft[0]")&&snippets[0].Contains("SHA256=")&&reference.Contains("loki_intro_1"),"partial human bridge reference/provenance missing");
        Check(LocalStoryTestArchive.CaptureWritingExamples(Guide,out _,out _,Path.Combine(store.StorageRoot,"LocalStoryTests"),12345)=="","runtime relaxed comparison UI reference qualification");
        edit["draft"][0]["speaker"]="플레이어"; File.WriteAllText(editFile,edit.ToString());
        Check(LocalStoryRuntimePipeline.RuntimeExamples(Guide,store,out _,out snippets,12345)==""&&snippets.Length==0,"wrong-speaker edit became a runtime reference");
        File.WriteAllText(editFile,humanEdit);
        Check(File.ReadAllText(archived)==evidence,"reference capture modified raw evidence");

        report=SectionReport(); calls=0;
        var snippetNames=new[]{"HUMAN_STYLE_A","HUMAN_STYLE_B","HUMAN_STYLE_C"};
        report.examplePrompt="말투 참고\n"+new JArray(snippetNames.Select(s=>new JObject {["text"]=s})).ToString();
        await LocalStorySections.Generate(report,CancellationToken.None,good);
        Check(report.sectionInputs.Where(s=>s.count>0).All(s=>snippetNames.Count(name=>s.prompt.Contains(name))==
            (string.IsNullOrWhiteSpace(report.original.MainStory[s.start].semanticText)?1:0)),"section style snippets are duplicated or normalize garbling");

        report=SectionReport(); report.storyPath=Mark; report.original=Source(Mark); calls=0;
        await LocalStorySections.Generate(report,CancellationToken.None,good);
        Check(calls==2&&report.sectionInputs.Sum(s=>s.count)==2&&report.authoredCards==0,"item scene menu was excluded from generation");
        Check(report.generated.MainStory[1].text!=report.original.MainStory[1].text&&
            !report.generated.MainStory[1].options.SequenceEqual(report.original.MainStory[1].options)&&
            LocalStoryRewrite.SameBindings(report.original,report.generated),"menu wording is locked or its action bindings changed");
        Check(LocalStoryTestArchive.RawCards(report.sectionOutputs[0].rawOutput).Count==1&&
            LocalStoryTestArchive.RawCards(report.rawOutput).Count==2,"section output did not preserve real menu output");
        report=SectionReport(); report.storyPath=Mark; report.original=Source(Mark); report.original.MainStory.RemoveAt(0); calls=0;
        bool missingRejected=false;try{await LocalStorySections.Generate(report,CancellationToken.None,good);}catch(InvalidDataException){missingRejected=true;}
        Check(missingRejected&&calls==0,"Story card was removed without updating its authored paragraph");
        report=SectionReport(); report.storyPath=Guide+"/Redistribute"; report.original=Source(report.storyPath); calls=0;
        await LocalStorySections.Generate(report,CancellationToken.None,good);
        Check(report.valid&&report.sectionInputs.Sum(s=>s.count)==17&&report.changedCards==17&&report.authoredCards==0,
            "ability explanations are still excluded instead of generated in short sections");
        Check(LocalStoryRewrite.SameBindings(report.original,report.generated),"tutorial generation changed allocation actions");
        var tutorialStore=Storage(root+"_tutorial");
        tutorialStore.SaveProfile(new ProfileData {currentRun=1}); tutorialStore.SaveRunCheckpoint(1,"Progress",new GameProgress());
        LocalStoryDialogueQueue.QueuePaths(tutorialStore,1,"Guide",new[]{Guide+"/Redistribute"},"tutorial generation coverage");
        Check(LocalStoryDialogueQueue.Load(tutorialStore,1)?.jobs.Single().storyPath==Guide+"/Redistribute/Repeat",
            "automatic queue ignores the authored run-2 allocation scene");

        report=SectionReport(); calls=0;
        Func<LocalStorySectionInput,bool,CancellationToken,Action<string>,Task<LocalStoryModelOutput>> repair=(input,correction,ct,progress)=> {
            calls++; var source=LocalStorySections.Slice(report.original,input.start,input.count);
            if(input.start==0&&!correction) source.MainStory[0].text="Guide → Player; 자네, 멈추게.";
            return Task.FromResult(new LocalStoryModelOutput {json=SectionPatch(source),backend="IsolatedFake",sessionReused=true});
        };
        await LocalStorySections.Generate(report,CancellationToken.None,repair);
        Check(report.correctionValid&&!report.valid&&report.sectionsCorrected==1&&calls==nativeSections+1,"repair was not restricted to one failed section");
        Check(report.sectionOutputs[0].rawOutput.Contains("Guide → Player")&&!report.corrected.MainStory[0].text.Contains("Guide → Player"),"failed raw was hidden or applied");

        report=SectionReport(); calls=0;
        await LocalStorySections.Generate(report,CancellationToken.None,(input,correction,ct,progress)=> {
            calls++; var source=LocalStorySections.Slice(report.original,input.start,input.count);
            if(input.start==0&&!correction) source.MainStory[0].text="지식이 빠졌다고 해도 괜찮아요.";
            return Task.FromResult(new LocalStoryModelOutput {json=SectionPatch(source),backend="IsolatedFake"});
        });
        Check(!report.valid&&report.correctionValid&&report.sectionsCorrected==1,"future mechanic claim passed the section state guard");
        var opening=JObject.Parse(((string)JArray.Parse(report.sectionInputs.First(s=>s.count>0).prompt)[0]["content"]).Split('\n').Last());
        Check(!opening["cards"].ToString().Contains("지식을 잠시 빼앗"),"opening generation targets contain a future mechanism");
        report=SectionReport(); calls=0;
        await LocalStorySections.Generate(report,CancellationToken.None,(input,correction,ct,progress)=> {
            calls++; var source=LocalStorySections.Slice(report.original,input.start,input.count);
            if((input.start==0||input.start==6)&&!correction) source.MainStory[0].text="자네, 이곳은 제법 조용하군. 잠깐 이야기 좀 나누세.";
            return Task.FromResult(new LocalStoryModelOutput {json=SectionPatch(source),backend="IsolatedFake"});
        });
        Check(report.correctionValid&&report.sectionsCorrected==1,"verbatim repetition was not repaired locally");

        report=SectionReport(); calls=0;
        await LocalStorySections.Generate(report,CancellationToken.None,(input,correction,ct,progress)=> {
            calls++; var source=LocalStorySections.Slice(report.original,input.start,input.count);
            if(input.start==0&&!correction) source.MainStory[0].text="오랜만이야, 아는 얼굴을 보니 반갑군. 잘 지냈나?";
            return Task.FromResult(new LocalStoryModelOutput {json=SectionPatch(source),backend="IsolatedFake"});
        });
        Check(report.correctionValid&&report.sectionsCorrected==1,"dream impression became a claimed actual reunion");
        report=SectionReport(); report.interactionMemory.memoryMode="CurrentRun";
        report.interactionMemory.events=new List<StoryEventRecord>{new StoryEventRecord {
            chapterId="Initial",characterId="Guide",kind="choice",semanticText="이번 회차에 실제로 만나 표식을 돌려주었다."}};
        await LocalStorySections.Generate(report,CancellationToken.None,(input,correction,ct,progress)=> {
            var source=LocalStorySections.Slice(report.original,input.start,input.count);
            if(input.start==0) source.MainStory[0].text="오랜만이야, 아는 얼굴을 보니 반갑군.";
            return Task.FromResult(new LocalStoryModelOutput {json=SectionPatch(source),backend="IsolatedFake"});
        });
        Check(report.valid&&!report.correctionAttempted,"actual current-run reunion was treated as a dream impression");

        report=SectionReport(); bool failed=false;
        try {await LocalStorySections.Generate(report,CancellationToken.None,(input,correction,ct,progress)=> {
            var bad=LocalStorySections.Slice(report.original,input.start,input.count); bad.MainStory[0].text="Guide → Player; 잘못된 표기";
            return Task.FromResult(new LocalStoryModelOutput {json=SectionPatch(bad),backend="IsolatedFake"});
        });} catch(InvalidOperationException){failed=true;}
        Check(failed&&!report.correctionValid&&report.corrected==null,"failed section promoted a source copy");
        Check(report.sectionOutputs[0].correctionRawOutput.Contains("Guide → Player"),"failed section correction evidence lost");

        report=SectionReport(); LocalStorySectionCheckpoint checkpoint=null; calls=0;
        using(var cancel=new CancellationTokenSource())
        {
            report.saveSectionCheckpoint=s=> {checkpoint=JsonUtility.FromJson<LocalStorySectionCheckpoint>(JsonUtility.ToJson(s));cancel.Cancel();};
            try {await LocalStorySections.Generate(report,cancel.Token,good);}catch(OperationCanceledException){}
        }
        Check(checkpoint?.inputs.Count==1 && !report.valid,"cancelled prefix was treated as a complete story");
        report=SectionReport(); report.sectionCheckpoint=checkpoint; calls=0;
        await LocalStorySections.Generate(report,CancellationToken.None,good);
        Check(report.valid&&report.sectionsResumed==1&&calls==nativeSections-1,"completed section was regenerated after restart");
        report=SectionReport(); report.seed++; report.sectionCheckpoint=checkpoint; calls=0;
        await LocalStorySections.Generate(report,CancellationToken.None,good);
        Check(report.valid&&report.sectionsResumed==0&&calls==nativeSections,"changed inputs resumed old sections");

        var chapter=NarrativeInteractions.Chapter("Initial"); bool previousProof=chapter.proofreadAllSections;
        try
        {
            chapter.proofreadAllSections=true; report=SectionReport(); calls=0;
            await LocalStorySections.Generate(report,CancellationToken.None,good);
            Check(report.correctionValid&&report.sectionsCorrected==nativeSections&&calls==nativeSections*2,"full section proofreading option lost");
        }
        finally {chapter.proofreadAllSections=previousProof;}

        // Exercise the real Produce -> checkpoint -> persisted queue -> cancellation -> resume wiring.
        store.SaveProfile(new ProfileData {currentRun=1}); store.SaveRunCheckpoint(1,"Progress",new GameProgress());
        LocalStoryDialogueQueue.QueuePaths(store,1,"Guide",new[]{Guide},"isolated section resume");
        int firstCalls=0;
        using(var cancel=new CancellationTokenSource())
        {
            await LocalStoryDialogueQueue.Process(store,LocalStoryDialogueQueue.Next(store),async(r,ct)=> {
                var saveCheckpoint=r.saveSectionCheckpoint;
                Check(saveCheckpoint!=null,"runtime did not attach a persistent section checkpoint");
                r.saveSectionCheckpoint=s=> {saveCheckpoint(s);cancel.Cancel();};
                await LocalStorySections.Generate(r,ct,(input,correction,sectionToken,progress)=> {
                    firstCalls++;
                    return Task.FromResult(new LocalStoryModelOutput {json=SectionPatch(LocalStorySections.Slice(r.original,input.start,input.count)),backend="IsolatedFake"});
                });
            },cancel.Token);
        }
        var pending=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
        Check(firstCalls==1&&pending.status=="Pending"&&pending.sectionCheckpoint?.accepted.Count==plan[0].count,"cancelled runtime lost its saved prefix");
        Check(!store.HasNarrativeContent(2,Guide),"cancelled runtime applied an incomplete scene");
        var restarted=Storage(root); int resumedCalls=0; LocalStoryTestReport resumedReport=null;
        await LocalStoryDialogueQueue.Process(restarted,LocalStoryDialogueQueue.Next(restarted),async(r,ct)=> {
            resumedReport=r;
            await LocalStorySections.Generate(r,ct,(input,correction,sectionToken,progress)=> {
                resumedCalls++;
                return Task.FromResult(new LocalStoryModelOutput {json=SectionPatch(LocalStorySections.Slice(r.original,input.start,input.count)),backend="IsolatedFake"});
            });
        },CancellationToken.None);
        var completed=LocalStoryDialogueQueue.Load(restarted,1).jobs.Single();
        Check(resumedCalls==nativeSections-1&&resumedReport.sectionsResumed==1&&completed.status=="Ready","runtime resume regenerated completed sections");
        Check((completed.sectionCheckpoint?.accepted?.Count??0)==0&&(completed.sectionCheckpoint?.inputs?.Count??0)==0&&restarted.HasNarrativeContent(2,Guide),"completed runtime retained duplicate checkpoints or lost content");
        var stalePolicy=JObject.Parse(File.ReadAllText(Path.Combine(restarted.GeneratedNpcDirectory(2,Guide),"Story.json")));
        ((JArray)stalePolicy["stories"]).OfType<JObject>().Single(e=>(string)e["storyPath"]==Guide)["generationPolicyHash"]="old-partial-generation-policy";
        File.WriteAllText(Path.Combine(restarted.GeneratedNpcDirectory(2,Guide),"Story.json"),stalePolicy.ToString());
        var upgraded=Storage(root);var renewed=LocalStoryDialogueQueue.Next(upgraded);
        Check(renewed==null,"policy edit recreated completed work");
        Check(LocalStoryDialogueQueue.Load(upgraded,1).jobs.Single(j=>j.storyPath==Guide).revision==completed.revision,"policy edit changed request version");
        await LocalStoryDialogueQueue.Process(upgraded,renewed,async(r,ct)=> {
            await LocalStorySections.Generate(r,ct,(input,correction,sectionToken,progress)=>Task.FromResult(new LocalStoryModelOutput {
                json=SectionPatch(LocalStorySections.Slice(r.original,input.start,input.count)),backend="IsolatedFake"}));
        },CancellationToken.None);
        Check(LocalStoryDialogueQueue.Load(upgraded,1).jobs.Single().status=="Ready"&&
            upgraded.TryLoadGeneratedContent(2,"Encounters",Guide+"/Story",out EncounterCardRoot fullStory)&&fullStory.MainStory.Count==9,
            "upgraded full scene did not replace its owned partial result");
        Check(NarrativeInteractions.StorySource(Guide)==original,"section generation changed canonical content");
        Check(actual.SequenceEqual(ActualSaveFingerprint()),"section checks changed actual saves");
        return new {passed=true,checks,isolatedStorage=root,sections=plan.Count,targetedRepair=true,resume=true,rawEvidencePreserved=true,realSavesUntouched=true};
    }
    public static async Task<object> NativeSectionsSmoke(string storyPath = Mark)
    {
        if(Application.isPlaying||LocalStoryRuntimeService.IsBusy) throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        LocalStoryLifetime.CancelAndWait();
        var report=SectionReport(); report.storyPath=storyPath; report.original=Source(storyPath);
        report.createdUtc=DateTime.UtcNow.ToString("o"); report.testScenario="격리 Gemma 구간 생성 / 실제 플레이 적용 아님";
        report.interactionSource="구간 엔진 연결 확인용 합성 자료. 실제 기록으로 승격하지 않음";
        report.examplePrompt=LocalStoryRuntimePipeline.RuntimeExamples(storyPath,SaveIOService.Instance,out report.exampleSummary,out report.exampleSources,report.seed);
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","tmp","NarrativeNativeSections",Guid.NewGuid().ToString("N")));
        var storage=Storage(folder); var watch=System.Diagnostics.Stopwatch.StartNew();
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(300));
        try
        {
            try {await LocalStoryRuntimePipeline.Infer(report,budget.Token);}
            catch(Exception e){report.runtimeError=e.Message;report.runtimeFailureDetails=e.ToString();}
            report.seconds=watch.Elapsed.TotalSeconds;
            string result=storage.SaveLocalStoryTest(report);
            var finalStory=report.correctionAttempted?report.corrected:report.generated;
            int changed=finalStory==null?0:report.original.MainStory.Zip(finalStory.MainStory,(a,b)=>a.text!=b.text||
                !(a.options??Array.Empty<string>()).SequenceEqual(b.options??Array.Empty<string>())).Count(different=>different);
            return new {passed=report.correctionAttempted?report.correctionValid:report.valid,report.model,report.seconds,report.sectionsGenerated,report.sectionsCorrected,
                report.draftVerificationSeconds,report.draftModelLoadSeconds,report.draftGenerationSeconds,report.correctionGenerationSeconds,
                report.verificationProvider,report.verificationBytes,
                report.exampleSummary,
                wordingChangedCards=changed,report.authoredCards,naturalness="직접 평가 필요",
                targetedCards=report.sectionInputs?.Sum(s=>s.count),targetedOptions=report.original.MainStory.Sum(c=>c.options?.Length??0),
                allCardsTargeted=report.sectionInputs?.SelectMany(s=>Enumerable.Range(s.start,s.count)).SequenceEqual(Enumerable.Range(0,report.original.MainStory.Count)),
                error=report.runtimeError??(report.correctionAttempted?report.correctionError:report.error),draftError=report.error,reportPath=result,
                draft=report.rawOutput,correction=report.correctionRawOutput,realSavesUntouched=true};
        }
        finally {LocalStoryLifetime.CancelAndWait();}
    }
}
