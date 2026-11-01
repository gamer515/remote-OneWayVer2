using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class DecisionCleanupRegression
{
    static void Check(bool ok,string message) {if(!ok) throw new InvalidOperationException(message);}
    public static object Run()
    {
        var paths=LocalStoryTestPanel.DiscoverPaths();
        var expected=ChapterContentCatalog.Stories.Select(s=>(string)s["path"]).Distinct().OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        Check(paths.SequenceEqual(expected),"Test UI omitted a current catalog story.");
        Check(paths.Contains("Chapter_1/Chapter_1_01/Encounters/brook"),"Brook is absent from the test UI.");
        Check(new LocalStoryTestReport().model.StartsWith("Gemma",StringComparison.Ordinal),"Default report is not Gemma.");
        Check(LocalStoryTestModels.ModelFor(new LocalStoryTestReport{model="Gemma"})==LocalStoryTestModel.Gemma,"Gemma selection failed.");
        Check(LocalStoryTestModels.ModelFor(new LocalStoryTestReport{model="OmniRoute / explain"})==LocalStoryTestModel.OmniRoute,"OmniRoute selection failed.");
        Check(LocalStoryTestModels.ModelFor(new LocalStoryTestReport{model="Qwen3-4B-Instruct-2507 Q4_K_M"})==LocalStoryTestModel.Qwen,"Historical Qwen report is unreadable.");
        foreach(string name in new[]{null,"","unrecognized-model"})
        {
            bool rejected=false;try{LocalStoryTestModels.ModelFor(new LocalStoryTestReport{model=name});}catch(ArgumentException){rejected=true;}
            Check(rejected,"Unknown model silently selected a backend.");
        }
        Check(typeof(StoryRelayManager).GetMethod("Relay")==null,"Retired empty relay is still callable.");
        Check(typeof(AIAPIClient).GetMethod("ProcessPacket")==null,"Retired Gemini generation remains callable.");
        string folder=Path.Combine(Path.GetTempPath(),"OneWayCleanupRegression_"+Guid.NewGuid().ToString("N"));
        var store=(SaveIOService)Activator.CreateInstance(typeof(SaveIOService),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{folder},null);
        try
        {
            var e=new StoryEventRecord{eventKey="positive",kind="choice",characterId="Brook",relationshipId="Brook",choiceRole="Positive"};
            store.SaveRunData(1,"Progress",new GameProgress{isCompleted=true,storyEvents=new List<StoryEventRecord>{e,e}});
            store.SaveRunData(2,"Progress",new GameProgress{isCompleted=false,storyEvents=new List<StoryEventRecord>{e}});
            var before=Directory.GetFiles(folder,"*",SearchOption.AllDirectories).ToDictionary(f=>f,File.ReadAllText);
            var rules=new[]{
                new LocalStoryDreamRule{ruleId="completed",characterId="Brook",kind="choice",choiceRole="Positive",impression="completed",enabled=true,runCompletedOnly=true},
                new LocalStoryDreamRule{ruleId="current",characterId="Brook",kind="choice",choiceRole="Positive",impression="current",enabled=true},
                new LocalStoryDreamRule{enabled=false}};
            var memories=LocalStoryDialogueQueue.SelectDreamMemories(store,2,rules);
            Check(memories.Count==3,"Memory filtering or duplicate suppression changed.");
            Check(memories.Select(m=>m.ruleId+":"+m.sourceRun).SequenceEqual(new[]{"completed:1","current:1","current:2"}),"Memory order/completion rules changed.");
            Check(before.All(p=>File.ReadAllText(p.Key)==p.Value),"Memory selection modified saved files.");
            return new {passed=true,catalogStories=paths.Count,memoryRecords=memories.Count,unknownModelsRejected=true,playerSavesTouched=false,modelInferenceRun=false};
        }
        finally
        {
            string safe=Path.GetFullPath(folder),prefix=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar+"OneWayCleanupRegression_";
            if(!safe.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe temporary cleanup path.");
            if(Directory.Exists(safe))Directory.Delete(safe,true);
        }
    }
}
