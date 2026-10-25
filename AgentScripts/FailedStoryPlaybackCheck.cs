using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class FailedStoryPlaybackCheck
{
    const string Guide="Initial/Initial_01/Encounters/guide";
    static int checks;
    static void Check(bool value,string error){if(!value)throw new Exception(error);checks++;}
    static EncounterCardRoot Source()=>JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(Guide));
    static SaveIOService Store(string root)=>(SaveIOService)Activator.CreateInstance(typeof(SaveIOService),BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{root},null);
    static string Patch(EncounterCardRoot root)=>new JObject {["cards"]=new JArray(root.MainStory.Select((c,i)=>new JObject {
        ["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}))}.ToString(Newtonsoft.Json.Formatting.None);
    static LocalStoryTestReport Report()=>new LocalStoryTestReport {original=Source(),storyPath=Guide,model=LocalStoryTestModels.Name(LocalStoryTestModel.Gemma),
        seed=12345,sourceRun=1,targetRun=2,allowFailedOutput=true,createdUtc=DateTime.UtcNow.ToString("o"),testScenario="Isolated failed output playback"};
    static LocalStoryModelOutput Output(LocalStoryTestReport report,LocalStorySectionInput input)
    {
        var piece=LocalStorySections.Slice(report.original,input.start,input.count);
        if(input.start==0)piece.MainStory[0].text="Guide → Player; 검사 실패 문구 그대로 확인";
        if(input.start==8)piece.MainStory[0].options[0]=piece.MainStory[0].choiceActions[0].choiceId;
        return new LocalStoryModelOutput {json=Patch(piece),backend="IsolatedFake"};
    }
    static object SwapStore(SaveIOService store)
    {
        var field=typeof(SaveIOService).GetField("_instance",BindingFlags.NonPublic|BindingFlags.Static);var old=field.GetValue(null);field.SetValue(null,store);return old;
    }
    static void RestoreStore(object old)=>typeof(SaveIOService).GetField("_instance",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,old);
    public static async Task<object> Regression()
    {
        ChapterContentCatalog.Reset();var runtime=LocalStoryPrompt.Policy["runtime"];bool prior=(bool)runtime["allowFailedOutput"];
        runtime["allowFailedOutput"]=true;
        try{return await DiagnosticPlaybackRegression();}
        finally{runtime["allowFailedOutput"]=prior;}
    }
    private static async Task<object> DiagnosticPlaybackRegression()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Play를 중지하고 실행하세요.");
        checks=0;var original=Source();string canonical=JsonUtility.ToJson(original);
        var failed=Source();failed.MainStory[8].options[0]=failed.MainStory[8].choiceActions[0].choiceId;
        Check(!LocalStoryRewrite.TryBuild(Patch(failed),original,null,out _,out _),"strict validation no longer rejects ID output");
        Check(LocalStoryRewrite.TryBuildForReview(Patch(failed),original,out var view,out _),"inspectable failed text discarded");
        Check(view.MainStory[8].options[0]=="loki_question_god"&&LocalStoryRewrite.SameBindings(original,view),"preview changed text or executable bindings");
        Check(!LocalStoryRewrite.TryBuildForReview("{",original,out _,out _),"broken JSON became game data");
        var fewer=Source();fewer.MainStory[8].options=fewer.MainStory[8].options.Take(3).ToArray();
        Check(!LocalStoryRewrite.TryBuildForReview(Patch(fewer),original,out _,out _),"wrong choice count became playable");
        var injected=JObject.Parse(Patch(failed));injected["cards"][0]["guideAction"]=new JObject {["kind"]="punish"};
        Check(!LocalStoryRewrite.TryBuildForReview(injected.ToString(),original,out _,out _),"model action injection accepted");
        var prefix=LocalStorySections.Slice(failed,0,3);
        Check(LocalStoryRewrite.TryBuildForReview(Patch(prefix),original,out var partial,out var note,true)&&partial.MainStory.Count==9&&note.Contains("3/9"),"legacy partial output not labelled");
        Check(partial.MainStory[8].text==original.MainStory[8].text,"missing legacy line was invented");

        var report=Report();int drafts=0,repairs=0;
        await LocalStorySections.Generate(report,CancellationToken.None,(input,repair,ct,stream)=>{
            if(repair)repairs++;else drafts++;return Task.FromResult(Output(report,input));});
        Check(drafts==6&&repairs==2,"failed section stopped later paragraph generation");
        Check(report.reviewRequired&&!report.valid&&!report.correctionValid&&report.reviewGeneratedCards==9,"failure was disguised as a successful validation");
        Check(report.sectionOutputs.Count(s=>s.reviewApplied)==2,"failed sections not identified");
        view=LocalStoryGameApply.BuildFailedPreview(report,out string reason);
        Check(view.MainStory[0].text.Contains("검사 실패 문구")&&view.MainStory[8].options[0]=="loki_question_god","failed draft/correction not preserved verbatim");
        Check(reason.Contains("카드 1~3")&&reason.Contains("카드 9~9")&&LocalStoryRewrite.SameBindings(original,view),"failure location or source mechanics lost");

        LocalStorySectionCheckpoint checkpoint=null;var cancelled=Report();
        using(var cancel=new CancellationTokenSource())
        {
            cancelled.saveSectionCheckpoint=s=>{checkpoint=JsonUtility.FromJson<LocalStorySectionCheckpoint>(JsonUtility.ToJson(s));cancel.Cancel();};
            try{await LocalStorySections.Generate(cancelled,cancel.Token,(input,repair,ct,stream)=>Task.FromResult(Output(cancelled,input)));}catch(OperationCanceledException){}
        }
        var resumed=Report();resumed.sectionCheckpoint=checkpoint;int resumedDrafts=0;
        await LocalStorySections.Generate(resumed,CancellationToken.None,(input,repair,ct,stream)=>{if(!repair)resumedDrafts++;return Task.FromResult(Output(resumed,input));});
        Check(resumed.sectionsResumed==1&&resumedDrafts==5&&resumed.reviewRequired,"failure-preview checkpoint was regenerated or treated as valid");

        string root=Path.GetFullPath("tmp/FailedStoryPlayback/"+Guid.NewGuid().ToString("N"));var store=Store(root);
        store.SaveProfile(new ProfileData {currentRun=1});store.SaveRunCheckpoint(1,"Progress",new GameProgress());
        string progressBefore=File.ReadAllText(Path.Combine(root,"Runs","Run_01","Progress.json"));
        LocalStoryDialogueQueue.QueuePaths(store,1,"Guide",new[]{Guide},"isolated failed text test");
        await LocalStoryDialogueQueue.Process(store,LocalStoryDialogueQueue.Next(store),(r,ct)=>LocalStorySections.Generate(r,ct,
            (input,repair,st,stream)=>Task.FromResult(Output(r,input))),CancellationToken.None);
        var job=LocalStoryDialogueQueue.Load(store,1).jobs.Single();
        Check(job.status=="ReviewRequired"&&store.HasNarrativeContent(2,Guide),"automatic failure was not stored for playback");
        Check(store.NarrativeRequiresReview(2,Guide,out var storedReason)&&storedReason.Contains("카드"),"review status/reason not persisted");
        Check(job.contentHash==store.NarrativeContentHash(2,Guide)&&job.auditArchived&&(job.sectionCheckpoint?.inputs?.Count??0)==0,"review queue did not finalize or retain ownership");
        var saved=LocalStoryTestArchive.Load(job.reportPath);
        Check(saved.saved&&saved.reviewRequired&&!saved.correctionValid&&saved.runtimeStage=="failed-output","archive hid failed application status");
        Check(LocalStoryTestPanel.DisplayOutput(saved).Contains("검사 실패 문구 포함"),"test viewer did not identify failed output");
        var replay=Store(root); // Read through a fresh storage cache and actual gameplay loader.
        object previous=SwapStore(replay);
        try
        {
            Check(new EncounterContentRepository(2).TryLoadCards(Guide,out var cards,out var error),error);
            Check(cards.MainStory[0].text.Contains("검사 실패 문구")&&cards.MainStory[8].options[0]=="loki_question_god","game loader replaced failed wording with source");
            Check(JsonUtility.ToJson(cards.MainStory[8].choiceActions)==JsonUtility.ToJson(original.MainStory[8].choiceActions),"choice rewards/branches changed");
            Check(cards.MainStory[3].guideAction.kind=="take_knowledge"&&cards.MainStory[3].guideAction.amount==5,"knowledge mechanic changed");
            Check(new EncounterContentRepository(1).TryLoadCards(Guide,out var current,out error)&&current.MainStory[0].text==original.MainStory[0].text,"current run was overwritten");
            Check(replay.SetNarrativeContentEnabled(2,Guide,false),"cannot disable failed preview");
            Check(new EncounterContentRepository(2).TryLoadCards(Guide,out cards,out error)&&cards.MainStory[0].text==original.MainStory[0].text,"disabled failed preview remains active");
        }
        finally{RestoreStore(previous);}
        Check(File.ReadAllText(Path.Combine(root,"Runs","Run_01","Progress.json"))==progressBefore,"actual play fixture was changed by generation");

        var malformed=Report();
        await LocalStorySections.Generate(malformed,CancellationToken.None,(input,repair,ct,stream)=>Task.FromResult(new LocalStoryModelOutput {json="{",backend="IsolatedFake"}));
        Check(malformed.sectionsGenerated==6&&malformed.reviewGeneratedCards==0&&malformed.sectionOutputs.All(s=>s.sourceFallback),"unparseable sections not labelled as source fallback");
        bool rejected=false;try{LocalStoryGameApply.BuildFailedPreview(malformed,out _);}catch(FormatException){rejected=true;}
        Check(rejected,"all-malformed generation was presented as generated dialogue");
        string malformedFile=store.SaveLocalStoryTest(malformed);rejected=false;
        try{LocalStoryGameApply.BuildFailedPreview(LocalStoryTestArchive.Load(malformedFile),out _);}catch(FormatException){rejected=true;}
        Check(rejected,"archive reload lost the all-malformed output flag");

        var manual=Store(root+"_manual");var manualReport=Report();manualReport.rawOutput=Patch(failed);
        LocalStoryGameApply.Save(manualReport,"draft",false,2,manual);
        Check(manual.NarrativeRequiresReview(2,Guide,out _),"manual failed output did not retain its review marker");
        Check(!manual.CanReplaceNarrativeContent(2,Guide,manual.NarrativeContentHash(2,Guide)),"manual failed preview can be overwritten automatically");
        Check(manual.TryLoadGeneratedContent(2,"Encounters",Guide+"/Story",out EncounterCardRoot manualView)&&manualView.MainStory[8].options[0]=="loki_question_god","test apply changed failed wording");
        Check(manual.SaveGeneratedContentForTest(2,"Encounters",Guide+"/Story",Source())&&!manual.NarrativeRequiresReview(2,Guide,out _),"valid replacement inherited failure status");
        Check(manual.SetNarrativeContentEnabled(2,Guide,true,true)&&manual.NarrativeRequiresReview(2,Guide,out _),"restoring failed preview lost its review status");

        var legacy=Store(root+"_legacy");legacy.SaveProfile(new ProfileData {currentRun=1});legacy.SaveRunCheckpoint(1,"Progress",new GameProgress());
        LocalStoryDialogueQueue.QueuePaths(legacy,1,"Guide",new[]{Guide},"legacy failed output");
        var old=Report();old.allowFailedOutput=false;old.rawOutput=Patch(failed);old.error="내부 선택 ID 출력";
        old.reviewRequired=false;string oldFile=legacy.SaveLocalStoryTest(old);
        var oldQueue=LocalStoryDialogueQueue.Load(legacy,1);oldQueue.jobs[0].status="Failed";oldQueue.jobs[0].reportPath=oldFile;oldQueue.jobs[0].error=old.error;
        LocalStoryDialogueQueue.Save(legacy,oldQueue);
        Check(legacy.ApplySavedFailedOutputs()==1,"existing failed result was not applied");
        Check(legacy.ApplySavedFailedOutputs()==0&&LocalStoryDialogueQueue.Load(legacy,1).jobs.Single().status=="ReviewRequired","existing failure migration not idempotent");
        Check(legacy.TryLoadGeneratedContent(2,"Encounters",Guide+"/Story",out EncounterCardRoot legacyView)&&legacyView.MainStory[8].options[0]=="loki_question_god","legacy failure wording was replaced");
        var wholeRun=Store(root+"_completed");wholeRun.SaveProfile(new ProfileData {currentRun=2});wholeRun.SaveRunCheckpoint(1,"Progress",new GameProgress {isCompleted=true});
        var request=new LocalStoryRevisionRequest {sourceRun=1,targetRun=2,chapterId="Initial",scenarioPaths=new[]{"Initial/Initial_01"},
            npcContexts=new[]{new LocalStoryNpcContext {characterId="Guide",name="로키",model="Gemma",autoGenerate=true}},
            playedCards=new System.Collections.Generic.List<PlayedEncounterCardRecord>{new PlayedEncounterCardRecord {encounterPath=Guide}}};
        await LocalStoryRuntimePipeline.Execute(request,wholeRun,(r,ct)=>{r.rawOutput=Patch(failed);LocalStoryRewrite.EvaluateDraft(r,r.original,r.interactionMemory);return Task.CompletedTask;},CancellationToken.None);
        var summary=wholeRun.LoadRunData<LocalStoryRevisionRequest>(1,"LocalStoryRevisionRequest");
        Check(summary.status=="ReviewRequired"&&summary.generationResults.Single().status=="ReviewRequired","completed-run path disguised review as success");
        Check(summary.generationError.Contains("내부 이벤트/선택 ID")&&wholeRun.NarrativeRequiresReview(2,Guide,out _),"completed-run failure reason or playback lost");
        Check(JsonUtility.ToJson(Source())==canonical,"source data changed");
        return new {passed=true,checks,root,drafts,repairs,failedTextApplied=true,allCardsGenerated=true,bindingsPreserved=true,legacyFailuresApplied=true};
    }
}
