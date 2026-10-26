using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Small categorized checkpoint + bounded history recovery pair. Reads original flat saves too.</summary>
public static class ProgressDocument
{
    private static readonly object Gate=new object();
    private static readonly Dictionary<string,string[]> Groups=new Dictionary<string,string[]> {
        ["progress"]=new[]{"runSeed","chapterIndex","episodeIndex","storyIndex","currentPosition","currentRotation","isCompleted","runEndReason","finalBattlePending","finalBattleStage","finalBattleWon"},
        ["encounter"]=new[]{"interactionPhase","activeStoryPath","activePlaceId","activeCardIndex","resolvedPlaceIds","storyVisit"},
        ["player"]=new[]{"remainingCoins","storedItemIds","inventoryStateVersion","worldInventoryItems"},
        ["world"]=new[]{"unlockedIds","relationships","appliedEffectIds"},
        ["effects"]=new[]{"guideProgress","influenceHistory","pendingEpisodes"}
    };
    private static void Write(string file,string text)
    {
        string temp=file+".tmp";File.WriteAllText(temp,text);
        if(File.Exists(file)) File.Replace(temp,file,null);else File.Move(temp,file);
    }
    public static string Encode(string path,GameProgress progress)
    {
        lock(Gate)
        {
            var flat=JObject.Parse(JsonUtility.ToJson(progress));
            var guide=(JObject)flat["guideProgress"];
            var run=guide?["runHistory"] as JArray??new JArray();
            var current=flat["currentEncounterHistory"] as JArray??new JArray();
            var references=new JArray();
            foreach(var record in current)
            {
                int index=-1;for(int i=run.Count-1;i>=0;i--) if(JToken.DeepEquals(run[i],record)) {index=i;break;}
                references.Add(index>=0?new JObject { ["index"]=index }:new JObject { ["record"]=record.DeepClone() });
            }
            var history=new JObject { ["storyEvents"]=flat["storyEvents"]?.DeepClone()??new JArray(),
                ["runHistory"]=run.DeepClone(),["currentEncounterHistory"]=references };
            string text=history.ToString(Newtonsoft.Json.Formatting.None),hash=LocalStoryDialogueQueue.Hash(text);
            string historyFile=Path.Combine(Path.GetDirectoryName(path),"History.json");
            if(!File.Exists(historyFile) || File.ReadAllText(historyFile)!=text)
            {
                if(File.Exists(historyFile))
                {
                    string previous=File.ReadAllText(historyFile), expected=null;
                    if(File.Exists(path)) expected=(string)JObject.Parse(File.ReadAllText(path))["historyHash"];
                    if(expected!=null && LocalStoryDialogueQueue.Hash(previous)!=expected)
                    {
                        string backup=Path.Combine(Path.GetDirectoryName(path),"History.previous.json");
                        if(!File.Exists(backup) || LocalStoryDialogueQueue.Hash(File.ReadAllText(backup))!=expected)
                            throw new InvalidDataException("이전 체크포인트의 기록을 보존할 수 없습니다.");
                        previous=File.ReadAllText(backup);
                    }
                    Write(Path.Combine(Path.GetDirectoryName(path),"History.previous.json"),previous);
                }
                Write(historyFile,text);
            }
            guide?.Remove("runHistory");flat.Remove("storyEvents");flat.Remove("currentEncounterHistory");
            var result=new JObject { ["schemaVersion"]=2,["historyHash"]=hash };
            foreach(var group in Groups)
            {
                var value=new JObject();foreach(string field in group.Value) if(flat[field]!=null) {value[field]=flat[field];flat.Remove(field);}
                result[group.Key]=value;
            }
            if(flat.Count>0) result["other"]=flat; // Future fields must never disappear silently.
            return result.ToString(Newtonsoft.Json.Formatting.Indented);
        }
    }
    public static GameProgress Read(string path)
    {
        var root=JObject.Parse(File.ReadAllText(path));
        if(root["schemaVersion"]==null) return JsonUtility.FromJson<GameProgress>(root.ToString());
        if((int)root["schemaVersion"]!=2) throw new InvalidDataException("지원하지 않는 진행 저장 버전");
        var flat=new JObject();
        foreach(string group in Groups.Keys.Concat(new[]{"other"}))
            if(root[group] is JObject section) foreach(var p in section.Properties()) flat[p.Name]=p.Value.DeepClone();
        string text=null;
        foreach(string name in new[]{"History.json","History.previous.json"})
        {
            string file=Path.Combine(Path.GetDirectoryName(path),name);
            if(File.Exists(file)) {string candidate=File.ReadAllText(file);if(LocalStoryDialogueQueue.Hash(candidate)==(string)root["historyHash"]) {text=candidate;break;}}
        }
        if(text==null) throw new InvalidDataException("진행도와 일치하는 플레이 기록이 없습니다. 기존 파일을 보존합니다.");
        var history=JObject.Parse(text);
        flat["storyEvents"]=history["storyEvents"].DeepClone();
        flat["guideProgress"]??=new JObject();flat["guideProgress"]["runHistory"]=history["runHistory"].DeepClone();
        flat["currentEncounterHistory"]=new JArray(((JArray)history["currentEncounterHistory"]).Select(r=>r["index"]!=null?
            history["runHistory"][(int)r["index"]].DeepClone():r["record"].DeepClone()));
        return JsonUtility.FromJson<GameProgress>(flat.ToString());
    }
}
