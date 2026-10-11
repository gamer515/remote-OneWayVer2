using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class LocalStoryRuntimeCheck
{
    static int checks;
    static void Check(bool ok,string error) { if(!ok) throw new Exception(error); checks++; }
    static SaveIOService Storage(string root) => (SaveIOService)typeof(SaveIOService).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(string)},null).Invoke(new object[]{root});
    static string Patch(EncounterCardRoot source) => new JObject {["cards"]=new JArray(source.MainStory.Select((c,i) => new JObject {
        ["index"]=i,["text"]=c.text,["options"]=new JArray(c.options??Array.Empty<string>())}))}.ToString(Newtonsoft.Json.Formatting.None);
    static LocalStoryRevisionRequest Request() => new LocalStoryRevisionRequest {sourceRun=1,targetRun=2,chapterId="Initial",scenarioPaths=new[]{"Initial/Initial_01"},
        finalStats=new[]{8,4,4,4},events=new System.Collections.Generic.List<StoryEventRecord>{new StoryEventRecord {run=1,chapterId="Initial",characterId="Guide",characterName="로키",terrainName="Dungeon",
        kind="choice",eventKey="choice:1",displayedText="꺼져!",semanticText="꺼져!",choiceId="loki_insult",relationshipDelta=-2}}};
    static void Cell(string workbook,string sheet,int row,int column,string text)
    {
        using var zip=ZipFile.Open(workbook,ZipArchiveMode.Update);
        XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument doc; var entry=zip.GetEntry(sheet); using(var input=entry.Open()) doc=XDocument.Load(input);
        var data=doc.Root.Element(ns+"sheetData"); var r=data.Elements(ns+"row").FirstOrDefault(e => (int)e.Attribute("r")==row);
        if(r==null) { r=new XElement(ns+"row",new XAttribute("r",row)); data.Add(r); }
        string address=((char)('A'+column))+row.ToString();
        r.Elements(ns+"c").Where(c => (string)c.Attribute("r")==address).Remove();
        r.Add(new XElement(ns+"c",new XAttribute("r",address),new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t",text))));
        entry.Delete(); using var output=zip.CreateEntry(sheet).Open(); doc.Save(output);
    }
    public static string Regression()
    {
        checks=0; string root=Path.Combine(Path.GetTempPath(),"OneWayRuntime_"+Guid.NewGuid().ToString("N"));
        var store=Storage(root); var request=Request();
        Check(!LocalStoryRuntimePipeline.CanProcess(request,store),"missing profile accepted");
        store.SaveProfile(new ProfileData {currentRun=2});
        store.SaveRunCheckpoint(1,"Progress",new GameProgress {isCompleted=false,storyEvents=request.events});
        Check(!LocalStoryRuntimePipeline.CanProcess(request,store),"unfinished source promoted");
        store.SaveRunCheckpoint(1,"Progress",new GameProgress {isCompleted=true,storyEvents=request.events,unlockedIds=new System.Collections.Generic.List<string>{"confirmed_gate"}});
        Check(LocalStoryRuntimePipeline.CanProcess(request,store),"completed run refused");
        var contexts=LocalStoryNotebook.Contexts(store,LocalStoryNotebook.EarnedUnlocks(store));
        Check(contexts.Length==3 && contexts[0].model=="Gemma" && contexts[0].autoGenerate && !contexts[1].autoGenerate,"defaults wrong");
        string workbook=LocalStoryNotebook.PathFor(store);
        Cell(workbook,"xl/worksheets/sheet1.xml",2,5,"과거 기록이 해금되어 공개되었다.");
        Cell(workbook,"xl/worksheets/sheet1.xml",2,6,"secret_gate");
        Check(LocalStoryNotebook.Contexts(store,Array.Empty<string>())[0].revealedPast=="","hidden past leaked");
        Check(LocalStoryNotebook.Contexts(store,new[]{"secret_gate"})[0].revealedPast.Contains("공개"),"earned past omitted");
        string path=LocalStoryTestInputs.GuidePath, target=path+"/Redistribute";
        string[] rule={"gate_rule","Guide",path,"confirmed_gate",target,"실제 조건이 해금되었다.","0"};
        for(int i=0;i<rule.Length;i++) Cell(workbook,"xl/worksheets/sheet2.xml",2,i,rule[i]);
        Check(LocalStoryNotebook.ResolveOpeningPath(store,"Guide",path)==path,"inactive rule opened");
        Cell(workbook,"xl/worksheets/sheet2.xml",2,6,"1");
        Check(LocalStoryNotebook.ResolveOpeningPath(store,"Guide",path)==target,"earned rule not opened");
        Check(LocalStoryNotebook.ResolveOpeningPath(store,"Duelist",path)==path,"another NPC affected");
        Check(LocalStoryNotebook.Contexts(store,new[]{"confirmed_gate"})[0].publicFacts.Contains("해금"),"earned prompt context missing");
        Cell(workbook,"xl/worksheets/sheet2.xml",2,3,"not_earned");
        Check(LocalStoryNotebook.ResolveOpeningPath(store,"Guide",path)==path,"missing condition opened");
        Cell(workbook,"xl/worksheets/sheet2.xml",2,3,"");
        Check(LocalStoryNotebook.ResolveOpeningPath(store,"Guide",path)==path,"empty condition opened");
        int calls=0;
        Func<LocalStoryTestReport,CancellationToken,Task> fake=(report,token) => {
            calls++; Check(report.prompt.Contains("꺼져!") && report.prompt.Contains("실제 해금된"),"real choice/context not in prompt");
            var changed=JsonUtility.FromJson<EncounterCardRoot>(JsonUtility.ToJson(report.original));
            changed.MainStory[0].text="자네, 지난번 그 무례한 말은 잊지 않았네. 잠깐 멈춰 보게.";
            changed.MainStory[3].text="내가 세상 돌아가는 이치를 좀 알려줄까?";
            report.rawOutput=Patch(changed); LocalStoryRewrite.EvaluateDraft(report,report.original,report.interactionMemory);
            Check(report.valid,report.error); return Task.CompletedTask;
        };
        var field=typeof(SaveIOService).GetField("_instance",BindingFlags.NonPublic|BindingFlags.Static); var previous=field.GetValue(null);
        string original=Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text;
        try
        {
            field.SetValue(null,store);
            LocalStoryRuntimePipeline.Execute(request,store,fake,CancellationToken.None).GetAwaiter().GetResult();
            var saved=store.LoadRunData<LocalStoryRevisionRequest>(1,"LocalStoryRevisionRequest");
            Check(saved.status=="Ready" && saved.generationResults.Count==1,"run status wrong");
            Check(saved.generationResults[0].reportPath!=null && File.Exists(saved.generationResults[0].reportPath),"raw report missing");
            Check(new EncounterContentRepository(2).TryLoadCards(path,out var cards,out var error),error);
            Check(cards.MainStory[0].text.Contains("무례") && cards.MainStory[3].text.Contains("이치"),"new dialogue not used / core locked to original");
            Check(cards.MainStory[3].guideAction.amount==5 && cards.MainStory[7].coreMeaning!=null,"action/core metadata changed");
            Check(new EncounterContentRepository(1).TryLoadCards(path,out var first,out error) && !first.MainStory[0].text.Contains("무례"),"current/source run changed");
            LocalStoryRuntimePipeline.Execute(request,store,fake,CancellationToken.None).GetAwaiter().GetResult();
            Check(calls==1,"existing game application overwritten");
            string content=Path.Combine(root,"GeneratedContent/For_Run_0002/Encounters",path,"Story.json");
            File.Move(content,Path.Combine(Path.GetDirectoryName(content),"Story.disabled.json"));
            LocalStoryRuntimePipeline.Execute(request,store,fake,CancellationToken.None).GetAwaiter().GetResult();
            Check(calls==1 && !File.Exists(content),"disabled restoration ignored");
            Check(new EncounterContentRepository(2).TryLoadCards(path,out var restored,out error) && restored.MainStory[0].text==first.MainStory[0].text,"original restore failed");
            // Exercise failure separately. Never apply failed raw output or fabricate a successful review.
            var failedStore=Storage(root+"_failed"); failedStore.SaveProfile(new ProfileData {currentRun=2});
            failedStore.SaveRunCheckpoint(1,"Progress",new GameProgress {isCompleted=true});
            LocalStoryRuntimePipeline.Execute(Request(),failedStore,(report,token) => {report.rawOutput="{invalid"; report.error="invalid fixture"; return Task.CompletedTask;},CancellationToken.None).GetAwaiter().GetResult();
            var failed=failedStore.LoadRunData<LocalStoryRevisionRequest>(1,"LocalStoryRevisionRequest");
            Check(failed.status=="Failed" && failed.generationResults[0].reportPath!=null,"failed raw discarded");
            Check(!failedStore.TryLoadGeneratedContent(2,"Encounters",path+"/Story",out EncounterCardRoot rejected),"invalid output promoted");
            var correctionStore=Storage(root+"_correction_failed"); correctionStore.SaveProfile(new ProfileData {currentRun=2});
            correctionStore.SaveRunCheckpoint(1,"Progress",new GameProgress {isCompleted=true});
            LocalStoryRuntimePipeline.Execute(Request(),correctionStore,(report,token) => {report.rawOutput=Patch(report.original);
                LocalStoryRewrite.EvaluateDraft(report,report.original,report.interactionMemory);
                report.correctionAttempted=true; report.correctionError="invalid correction fixture"; return Task.CompletedTask;},CancellationToken.None).GetAwaiter().GetResult();
            Check(!correctionStore.TryLoadGeneratedContent(2,"Encounters",path+"/Story",out EncounterCardRoot badCorrection),"failed correction replaced with draft in game");
            Check(Resources.Load<TextAsset>("Story_Json_Data/"+path+"/Story").text==original,"canonical JSON changed");
        }
        finally {field.SetValue(null,previous);}
        return checks+" checks passed. Isolated simulated completion + fake inference; NOT a real playthrough/model quality test. Kept fixtures: "+root;
    }
    public static string ExportActualNotebook()
    {
        LocalStoryNotebook.Refresh(SaveIOService.Instance);
        return LocalStoryNotebook.PathFor(SaveIOService.Instance);
    }
    public static string ActualWritingReference()
    {
        string captured=LocalStoryRuntimePipeline.RuntimeExamples(LocalStoryTestInputs.GuidePath,SaveIOService.Instance,out string summary,out string[] sources);
        if(!captured.Contains("말투") || !sources.Any(s => s.Contains("SHA256=")) || captured.Contains("\"index\""))
            throw new Exception("actual human reference missing or full answer forwarded");
        return summary+" / "+string.Join("; ",sources);
    }
}
