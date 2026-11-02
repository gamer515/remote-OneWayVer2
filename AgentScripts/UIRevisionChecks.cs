using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class UIRevisionChecks
{
    public static object Learning()
    {
        if(!Application.isPlaying || !SaveIOService.Instance.StorageRoot.Contains("MineChapterReview"))throw new Exception("Isolated Play review required");
        var station=UnityEngine.Object.FindFirstObjectByType<MineLearningStation>();
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var fields=new[]{"capacities","minimumSeconds","maximumSeconds","randomDuration"}.Select(n=>typeof(MineLearningStation).GetField(n,hidden)).ToArray();
        var originals=fields.Select(f=>f.GetValue(station)).ToArray();
        originals[0]=((MineLearningStation.CapacityStep[])originals[0]).Select(c=>new MineLearningStation.CapacityStep {knowledgeCoins=c.knowledgeCoins,simultaneous=c.simultaneous,waiting=c.waiting}).ToArray();
        var checks=new List<string>();void Check(bool pass,string label){if(!pass)throw new Exception(label);checks.Add(label);}
        int[] coins={100,100,100,100};int saves=0;
        bool Spend(int ability,int cost){if(coins[ability]<cost)return false;coins[ability]-=cost;return true;}
        void Grant(int ability,int reward)=>coins[ability]+=reward;
        try
        {
            station.Initialize(new MineLearningProgress(),42,()=>coins,Spend,Grant,()=>saves++);
            Check(station.Capacity.simultaneous==1 && station.Capacity.waiting==5,"Initial learning capacity is one active and five waiting");
            for(int i=0;i<6;i++)Check(station.Request(i%4),"Book "+(i+1)+" is accepted and charged once");
            int paid=coins.Sum();Check(!station.Request(0)&&coins.Sum()==paid,"Full queue rejects entry without charging");
            Check(station.Progress.active.Count==1&&station.Progress.queued.Count==5,"Waiting books remain in the five-item FIFO queue");
            station.Tick(.61f,false);station.Tick(2,false);float remaining=station.Progress.active[0].remaining;
            station.Tick(10,true);Check(Mathf.Abs(station.Progress.active[0].remaining-remaining)<.001f,"Requirements pause remaining learning time");
            station.Tick(3.01f,false);Check(coins[0]==100,"One completed strength book awards two coins after its one-coin entry cost");
            Check(station.Progress.active[0].ability==1&&station.Progress.queued.Count==4,"Completion dispatches the next queued book in order");
            var restored=JsonUtility.FromJson<MineLearningProgress>(JsonUtility.ToJson(station.Progress));
            int before=coins.Sum();station.Initialize(restored,42,()=>coins,Spend,Grant,()=>saves++);
            Check(coins.Sum()==before && restored.active.Count==1&&restored.queued.Count==4,"Restoring active and queued books does not charge again");
            var so=new SerializedObject(station);var steps=so.FindProperty("capacities");steps.arraySize=2;
            var step=steps.GetArrayElementAtIndex(1);step.FindPropertyRelative("knowledgeCoins").intValue=20;step.FindPropertyRelative("simultaneous").intValue=2;step.FindPropertyRelative("waiting").intValue=7;so.ApplyModifiedPropertiesWithoutUndo();
            station.Tick(0,false);Check(station.Progress.active.Count==2&&station.Capacity.waiting==7,"Inspector knowledge thresholds expand active and queued capacity");
            so.Update();so.FindProperty("minimumSeconds").floatValue=2;so.FindProperty("maximumSeconds").floatValue=4;so.FindProperty("randomDuration").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();
            Check(station.Request(3)&&station.Progress.queued.Last().duration>=2&&station.Progress.queued.Last().duration<=4,"Random durations respect the Inspector range");
            coins[3]=0;before=coins.Sum();Check(!station.Request(3)&&coins.Sum()==before,"Insufficient learning coins are not charged");
            Check(saves>0,"Entry and completion request persistence");
            var result=new{passed=true,count=checks.Count,checks};System.IO.Directory.CreateDirectory("output/UIRevision");System.IO.File.WriteAllText("output/UIRevision/learning-checks.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));return result;
        }
        finally
        {
            for(int i=0;i<fields.Length;i++)fields[i].SetValue(station,originals[i]);
            var manager=UnityEngine.Object.FindFirstObjectByType<DecisionManager>();typeof(DecisionManager).GetMethod("InitializeLearning",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,null);
        }
    }
}
