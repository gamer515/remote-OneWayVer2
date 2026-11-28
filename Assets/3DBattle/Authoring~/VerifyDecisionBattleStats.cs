using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class VerifyDecisionBattleStats
{
    public static object Inspect() => new {
        playing=Application.isPlaying, compiling=EditorApplication.isCompiling,
        animation=AnimationMode.InAnimationMode(),
        scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{
            var s=SceneManager.GetSceneAt(i); return new {s.path,s.isDirty};
        }).ToArray()
    };
    public static object Refresh() { AssetDatabase.Refresh(); return "Refresh requested"; }

    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int assertions;
    static void Check(bool value, string message)
    {
        assertions++;
        if(!value) throw new Exception(message);
    }
    static void Equal(float actual,float expected,string message)=>Check(Mathf.Abs(actual-expected)<.0001f,message+": expected "+expected+", got "+actual);
    sealed class Fixture : IDisposable
    {
        public readonly Scene scene;
        public readonly Battle3DSceneController battle;
        public readonly BattlePlayerController player;
        public readonly BattlePlayerHealth health;
        public readonly DebugPlayerSpecialCombo energy;
        public readonly BattleParryEffect parry;
        public Fixture()
        {
            scene=EditorSceneManager.NewPreviewScene();
            var root=new GameObject("Decision stat verification"){hideFlags=HideFlags.HideAndDontSave};
            SceneManager.MoveGameObjectToScene(root,scene);
            battle=root.AddComponent<Battle3DSceneController>();
            var go=new GameObject("Verification player"){hideFlags=HideFlags.HideAndDontSave};
            SceneManager.MoveGameObjectToScene(go,scene);go.transform.SetParent(root.transform);
            player=go.AddComponent<BattlePlayerController>();
            health=go.AddComponent<BattlePlayerHealth>();
            energy=go.AddComponent<DebugPlayerSpecialCombo>();energy.alwaysFullGauge=false;
            parry=go.AddComponent<BattleParryEffect>();
            battle.player=player;
        }
        public void Initialize(Battle3DStartData data)
        {
            if(data!=null)Battle3DEntry.SetNext(data);
            typeof(Battle3DSceneController).GetMethod("InitializeStartData",Private).Invoke(battle,null);
            // This is the same health/energy reset used when SelectStage starts or restarts a fight.
            player.ResetForBattle(null);
        }
        public void Dispose()=>EditorSceneManager.ClosePreviewScene(scene);
    }
    public static object Verify()
    {
        Check(!Application.isPlaying&&!AnimationMode.InAnimationMode(),"Edit mode without preview required");
        var pending=typeof(Battle3DEntry).GetField("pendingData",BindingFlags.Static|BindingFlags.NonPublic);
        var savedPending=pending.GetValue(null);
        var originalScenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).ToArray();
        var dirty=originalScenes.Select(s=>s.isDirty).ToArray();
        var sourceSceneHashes=new Dictionary<string,string>();
        foreach(var path in new[]{"Assets/Scenes/MainMenuScene.unity","Assets/Scenes/DecisionScene.unity","Assets/Scenes/3DBattleScene.unity","Assets/Scenes/3DBattleDebugScene.unity"})
            using(var stream=File.OpenRead(path))using(var sha=System.Security.Cryptography.SHA256.Create())sourceSceneHashes[path]=Convert.ToBase64String(sha.ComputeHash(stream));
        assertions=0;pending.SetValue(null,null);
        var checkedCases=new List<object>();
        try
        {
            // Inclusive boundaries, zero, and an uncapped Decision value above the highest band.
            var cases=new[]{new[]{0,70,7,3,20},new[]{1,70,7,3,20},new[]{5,70,7,3,20},
                new[]{6,100,10,5,25},new[]{10,100,10,5,25},new[]{11,130,13,7,27},
                new[]{15,130,13,7,27},new[]{16,150,15,10,30},new[]{100,150,15,10,30}};
            foreach(var c in cases)
            {
                using(var f=new Fixture())
                {
                    f.Initialize(new Battle3DStartData(2,c[0],c[0],c[0],c[0]));
                    Equal(f.battle.StartData.StageIndex,2,"Stage preserved");
                    Check(!Battle3DEntry.TryTake(out _),"Entry consumed once");
                    Equal(f.health.maxHealth,c[1],"Max health at "+c[0]);Equal(f.health.Health,c[1],"Health refilled after mapping");
                    Equal(f.player.moveSpeed,c[2],"Move speed at "+c[0]);Equal(f.energy.maxGauge,c[1],"Energy maximum");
                    Equal(f.energy.Gauge,50,"Fixed starting energy");Equal(f.energy.SpecialCost,50,"Fixed cost");
                    Check(f.energy.Ready,"Every starting allocation can afford one special");
                    Check(f.energy.TryConsume(),"Consume starting special");Equal(f.energy.Gauge,0,"Cost deducted once");
                    Check(!f.energy.TryConsume(),"Insufficient energy rejected");Equal(f.energy.Gauge,0,"No negative energy");
                    f.energy.ChargeFromAttackHit();Equal(f.energy.Gauge,c[3],"Attack gain is fixed");
                    f.parry.NotifySuccess(false);Equal(f.energy.Gauge,c[3]+c[4],"Parry gain uses same energy rules");
                    f.energy.currentGauge=f.energy.maxGauge-1;f.energy.ChargeFromAttackHit();Equal(f.energy.Gauge,c[1],"Attack charge cap");
                    f.energy.currentGauge=f.energy.maxGauge-1;f.parry.NotifySuccess(false);Equal(f.energy.Gauge,c[1],"Parry charge cap");
                    int specials=0;while(f.energy.TryConsume()){specials++;Check(specials<=3,"Finite energy consumption");}
                    Equal(specials,c[1]/50,"Stored special count");Equal(f.energy.Gauge,c[1]%50,"Remainder retained");
                    f.player.ResetForBattle(null);Equal(f.energy.Gauge,50,"Restart energy");Equal(f.health.Health,c[1],"Restart health");
                    Equal(f.player.normalDamage,25,"Damage unchanged");
                    checkedCases.Add(new{ability=c[0],health=c[1],speed=c[2],maximumEnergy=c[1],attackGain=c[3],parryGain=c[4]});
                }
            }
            using(var f=new Fixture())
            {
                // Independent abilities: intelligence changes capacity, not charm's recovery amounts.
                f.energy.alwaysFullGauge=true;
                f.Initialize(new Battle3DStartData(1,5,16,6,11));
                Equal(f.health.Health,70,"Mixed strength");Equal(f.player.moveSpeed,15,"Mixed speed");
                Equal(f.energy.maxGauge,100,"Mixed intelligence");Equal(f.energy.Gauge,50,"Decision rules override full-gauge test toggle");
                f.energy.currentGauge=0;f.energy.ChargeFromAttackHit();f.parry.NotifySuccess(false);Equal(f.energy.Gauge,34,"Mixed charm gains");
                f.Initialize(new Battle3DStartData(1,16,1,16,6));
                Equal(f.health.Health,150,"Second strength");Equal(f.player.moveSpeed,7,"Second speed");
                Equal(f.energy.maxGauge,150,"Second intelligence");f.energy.currentGauge=0;
                f.energy.ChargeFromAttackHit();Equal(f.energy.Gauge,5,"High intelligence does not multiply attack recovery");
                f.parry.NotifySuccess(false);Equal(f.energy.Gauge,30,"High intelligence does not multiply parry recovery");
                f.health.TakeGimmickDamage(1000);float energyBefore=f.energy.Gauge;
                f.energy.ChargeFromAttackHit();f.parry.NotifySuccess(false);Equal(f.energy.Gauge,energyBefore,"No recovery while defeated");
            }
            using(var f=new Fixture())
            {
                // No Decision entry must preserve custom inspector settings, not just 100/10/100 defaults.
                f.health.maxHealth=137;f.player.moveSpeed=11.5f;
                f.energy.maxGauge=160;f.energy.startingEnergyPercent=35;f.energy.specialCostPercent=30;
                f.energy.attackHitEnergyPercent=4;f.energy.parryEnergyPercent=12;
                f.Initialize(null);
                Equal(f.battle.StartData.Trait1,1,"Fallback data still available");
                Equal(f.health.Health,137,"No-entry health preserved");Equal(f.player.moveSpeed,11.5f,"No-entry speed preserved");
                Equal(f.energy.maxGauge,160,"No-entry maximum preserved");Equal(f.energy.Gauge,56,"No-entry start percentage preserved");
                Equal(f.energy.SpecialCost,48,"No-entry cost percentage preserved");
                f.energy.currentGauge=0;f.energy.ChargeFromAttackHit();Equal(f.energy.Gauge,6.4f,"No-entry attack percentage preserved");
                f.parry.NotifySuccess(false);Equal(f.energy.Gauge,25.6f,"No-entry parry percentage preserved");
                f.energy.alwaysFullGauge=true;f.energy.ResetGauge();Equal(f.energy.Gauge,160,"Direct debug full-gauge mode preserved");
                f.energy.TryConsume();Equal(f.energy.Gauge,160,"Direct debug unlimited specials preserved");
            }
        }
        finally { pending.SetValue(null,savedPending); }
        Check(SceneManager.sceneCount==originalScenes.Length,"No scene added or removed");
        for(int i=0;i<originalScenes.Length;i++)Check(originalScenes[i].isDirty==dirty[i],"Scene dirty flag preserved");
        foreach(var pair in sourceSceneHashes)
            using(var stream=File.OpenRead(pair.Key))using(var sha=System.Security.Cryptography.SHA256.Create())Check(Convert.ToBase64String(sha.ComputeHash(stream))==pair.Value,"Scene file unchanged: "+pair.Key);
        var result=new{passed=true,assertions,cases=checkedCases,mixedAbilities=true,noEntryPreservesInspector=true,fixedStartAndCost=50,sceneFilesUnchanged=true};
        File.WriteAllText("Assets/3DBattle/Authoring~/DecisionBattleStatsVerification.json",JsonConvert.SerializeObject(result,Formatting.Indented));
        return result;
    }
}
