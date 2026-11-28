using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OneWay.Battle3D;

public static class VerifySpecialHoldEntry
{
    const string StatePath="Assets/3DBattle/Authoring~/SpecialHoldEditorState.json";
    public sealed class EditorState { public SceneSetup[] scenes; public string playStartScene; public Dictionary<string,string> sceneHashes; }
    static string Hash(string path) { using(var s=File.OpenRead(path))using(var h=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(h.ComputeHash(s)); }
    public static object PreparePlayMode()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||AnimationMode.InAnimationMode())throw new Exception("Edit mode required");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new Exception("Unsaved scene must remain open; cannot switch to isolated test scene");
        var state=new EditorState{scenes=EditorSceneManager.GetSceneManagerSetup(),playStartScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            sceneHashes=new[]{"Assets/Scenes/3DBattleScene.unity","Assets/Scenes/3DBattleDebugScene.unity"}.ToDictionary(p=>p,Hash)};
        File.WriteAllText(StatePath,JsonConvert.SerializeObject(state,Formatting.Indented));
        EditorSceneManager.playModeStartScene=null;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.isPlaying=true;
        return "Starting isolated play-mode tests";
    }
    public static object Stop() { EditorApplication.isPlaying=false;return "Stopping isolated tests"; }
    public static object Restore()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Wait until play mode stops");
        var state=JsonConvert.DeserializeObject<EditorState>(File.ReadAllText(StatePath));
        EditorSceneManager.RestoreSceneManagerSetup(state.scenes);
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(state.playStartScene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(state.playStartScene);
        foreach(var pair in state.sceneHashes)if(Hash(pair.Key)!=pair.Value)throw new Exception("Scene file changed: "+pair.Key);
        return new{restored=state.scenes.Select(s=>s.path).ToArray(),sceneFilesUnchanged=true};
    }
    static int checks;
    static void Check(bool value,string message) { checks++;if(!value)throw new Exception(message); }
    static void Equal(float actual,float expected,string message)=>Check(Mathf.Abs(actual-expected)<.001f,message+" expected="+expected+" actual="+actual);
    sealed class Fixture:IDisposable
    {
        public readonly GameObject root;
        public readonly NewPlayerAttackCombo combo;
        public readonly DebugPlayerSpecialCombo energy;
        public readonly BattlePlayerController player;
        public readonly HashSet<int> normalMotions=new HashSet<int>();
        public bool held, sawSpecial;
        readonly float dt;
        readonly bool holdFirst;
        public Fixture(float step=.01f,bool setHeldBeforePress=false)
        {
            dt=step;holdFirst=setHeldBeforePress;
            root=new GameObject("Special entry test player");root.SetActive(false);
            root.AddComponent<BattlePlayerHealth>();
            energy=root.AddComponent<DebugPlayerSpecialCombo>();energy.alwaysFullGauge=false;
            energy.ConfigureDecisionEnergy(150,5,25);energy.finisherHoldSeconds=.8f;
            player=root.AddComponent<BattlePlayerController>();
            combo=root.AddComponent<NewPlayerAttackCombo>();
            combo.motions=AssetDatabase.LoadAssetAtPath<NewPlayerAttackSet>("Assets/3DBattle/NewPlayer/Runtime/Data/PlayerCombatAttackSet.asset");
            Check(combo.motions!=null,"Actual combat motions available");
            combo.readMouseInput=false;combo.playbackSpeed=1.6f;
            root.SetActive(true);player.enabled=false;energy.currentGauge=100;
        }
        public void Down()
        {
            held=true;
            if(holdFirst)combo.SetAttackHeld(true);
            combo.RequestAttack();
            combo.SetAttackHeld(true);
        }
        public void Up() { held=false;combo.SetAttackHeld(false); }
        public void Step(float seconds)
        {
            combo.SetAttackHeld(held);combo.Tick(seconds);
            sawSpecial|=combo.IsSpecialCombo;
            if(combo.IsActive&&!combo.IsSpecialCombo&&!combo.InPreparation)normalMotions.Add(combo.SequenceIndex);
        }
        public void Advance(float seconds) { while(seconds>.000001f){float t=Mathf.Min(dt,seconds);Step(t);seconds-=t;} }
        public void Until(Func<bool> predicate,string message)
        {
            for(int i=0;i<6000&&!predicate();i++)Step(dt);
            Check(predicate(),message+" seq="+combo.SequenceIndex+" frame="+combo.Frame+" prep="+combo.InPreparation);
        }
        public void Click() { Down();Advance(.03f);Up(); }
        public void ReachNormal(int index)
        {
            Click();Until(()=>combo.IsActive&&!combo.InPreparation&&combo.SequenceIndex==0,"Reach normal one");
            for(int next=1;next<=index;next++)
            {
                Click();int wanted=next;
                Until(()=>combo.IsActive&&!combo.InPreparation&&combo.SequenceIndex==wanted,"Reach normal "+(next+1));
            }
        }
        public void WaitForDash()
        {
            Until(()=>combo.IsSpecialCombo&&combo.SequenceIndex==4&&!combo.InPreparation,"Special dash starts");
            Equal(energy.Gauge,50,"Exactly one fixed cost at first dash");
        }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(root); }
    }
    public static object Verify()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");
        checks=0;var scenarios=new List<string>();
        foreach(bool holdFirst in new[]{false,true})
        using(var f=new Fixture(.01f,holdFirst))
        {
            f.Down();f.Advance(.19f);Check(!f.combo.IsActive,"No normal motion while deciding idle hold");Equal(f.energy.Gauge,100,"No cost before recognition");
            f.Advance(.011f);Check(f.combo.IsSpecialCombo,"Idle hold starts special directly");Check(f.normalMotions.Count==0,"Normal four-hit combo skipped");
            if(f.combo.InPreparation)Equal(f.energy.Gauge,100,"No cost during entry preparation");
            f.WaitForDash();
            f.Until(()=>f.combo.IsHoldingFinisher,"Held special reaches finisher");
            f.Until(()=>!f.combo.IsActive,"Finisher completes");Equal(f.energy.Gauge,50,"No second deduction");
            f.Advance(1);Check(!f.combo.IsActive,"Holding after special does not loop");
            scenarios.Add("idle hold, full special, both input callback orders: "+holdFirst);
        }
        using(var f=new Fixture())
        {
            f.Click();Check(f.combo.IsActive&&!f.combo.IsSpecialCombo&&f.combo.SequenceIndex==0,"Short click starts normal one");
            f.Until(()=>!f.combo.IsActive,"Single normal ends");Check(!f.sawSpecial,"Short click never starts special");Equal(f.energy.Gauge,100,"Normal click costs no energy");
            scenarios.Add("short click starts normal attack");
        }
        using(var f=new Fixture())
        {
            f.energy.currentGauge=49;f.Down();Check(f.combo.IsActive&&!f.combo.IsSpecialCombo,"Insufficient energy starts normal immediately");
            f.Until(()=>!f.combo.IsActive,"Insufficient-energy normal ends");f.Advance(.5f);Check(!f.sawSpecial,"Insufficient energy blocks special");Equal(f.energy.Gauge,49,"No energy deducted");
            scenarios.Add("energy below 50");
        }
        using(var f=new Fixture())
        {
            f.ReachNormal(1);f.Down();f.Until(()=>!f.combo.IsActive,"Second-attack hold finishes");f.Advance(.5f);
            Check(f.normalMotions.Contains(2)&&!f.normalMotions.Contains(3),"Second attack press queues only third");Check(!f.sawSpecial,"Second attack cannot arm special");
            scenarios.Add("hold from normal two stops after normal three");
        }
        foreach(float step in new[]{.01f,1f/30f,.05f})
        using(var f=new Fixture(step))
        {
            f.ReachNormal(2);f.Down();f.Until(()=>!f.combo.IsActive,"Third-attack hold finishes");f.Advance(.6f);
            Check(f.normalMotions.Contains(3),"Third press queues jump");Check(!f.sawSpecial&&!f.combo.IsActive,"Third-attack hold invalid through jump and recovery");Equal(f.energy.Gauge,100,"Invalid hold costs nothing");
            scenarios.Add("third-to-fourth carried hold rejected, dt="+step);
        }
        using(var f=new Fixture())
        {
            f.ReachNormal(2);f.Down();f.Until(()=>f.combo.SequenceIndex==3&&!f.combo.InPreparation,"Reach jump while holding third press");
            f.Up();f.Down();f.WaitForDash();Check(f.sawSpecial,"Fresh fourth press enters special");
            scenarios.Add("release and freshly hold during fourth attack");
        }
        using(var f=new Fixture())
        {
            f.ReachNormal(2);f.Down();f.Until(()=>f.combo.SequenceIndex==3&&f.combo.InPreparation,"Reach transition to jump");
            f.Up();f.Down();f.Until(()=>!f.combo.IsActive,"Transition hold finishes");f.Advance(.6f);
            Check(!f.sawSpecial,"Press during transition is earlier than actual fourth attack");
            scenarios.Add("press before fourth motion begins rejected");
        }
        using(var f=new Fixture())
        {
            f.Down();f.Advance(.201f);Check(f.combo.IsSpecialCombo&&f.combo.InPreparation,"Special entry has not charged yet");
            f.Up();Check(!f.combo.IsActive,"Release cancels special entry");Equal(f.energy.Gauge,100,"No charge before first dash");
            scenarios.Add("release before first special dash");
        }
        using(var f=new Fixture())
        {
            f.Down();f.Advance(.1f);f.combo.Cancel();f.Up();f.Advance(.5f);Check(!f.combo.IsActive,"Cancel clears pending idle click");
            f.Down();f.Advance(.1f);typeof(NewPlayerAttackCombo).GetMethod("DropAttackHold",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f.combo,null);
            f.Up();f.Advance(.5f);Check(!f.combo.IsActive,"Focus loss clears pending idle click");
            scenarios.Add("cancel and focus-loss input cleanup");
        }
        var result=new{passed=true,checks,scenarios,scene="isolated unsaved play-mode scene",motions="PlayerCombatAttackSet",holdRecognitionSeconds=.2f};
        File.WriteAllText("Assets/3DBattle/Authoring~/SpecialHoldEntryVerification.json",JsonConvert.SerializeObject(result,Formatting.Indented));
        return result;
    }
}
