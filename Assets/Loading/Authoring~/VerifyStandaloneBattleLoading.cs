using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Loading;
using OneWay.Battle3D;

public static class VerifyStandaloneBattleLoading
{
    const string State="Assets/Loading/Authoring~/StandaloneLoadingEditorState.json";
    const string Report="Assets/Loading/Authoring~/StandaloneLoadingVerification.json";
    public sealed class EditorState
    {
        public SceneSetup[] scenes; public string playStart; public Dictionary<string,string> hashes;
        public int selectedStage; public string[] activeRoots;
    }
    static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=System.Security.Cryptography.SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(stream));}
    static T[] Components<T>(Scene scene) where T:Component=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
    static void Check(bool valid,string message){if(!valid)throw new Exception(message);}
    static Battle3DSceneController Battle()=>Components<Battle3DSceneController>(SceneManager.GetSceneByPath(LoadingScreenController.ScenePath(LoadingDestination.Battle))).Single();
    public static object Prepare()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode&&!AnimationMode.InAnimationMode(),"Edit mode required");
        for(int i=0;i<SceneManager.sceneCount;i++)Check(!SceneManager.GetSceneAt(i).isDirty,"Unsaved scene must remain open");
        var state=new EditorState{scenes=EditorSceneManager.GetSceneManagerSetup(),playStart=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            hashes=new[]{"Assets/Scenes/3DBattleScene.unity","Assets/Scenes/LoadingScene.unity","Assets/Scenes/DecisionScene.unity","Assets/Scenes/MainMenuScene.unity"}.ToDictionary(p=>p,Hash)};
        var scene=EditorSceneManager.OpenScene(LoadingScreenController.ScenePath(LoadingDestination.Battle),OpenSceneMode.Single);
        var battle=Components<Battle3DSceneController>(scene).Single();state.selectedStage=battle.SelectedStageIndex;
        state.activeRoots=scene.GetRootGameObjects().Where(g=>g.activeSelf).Select(g=>g.name).OrderBy(n=>n).ToArray();
        // Deliberately unsaved Inspector values: reloading the scene asset would lose them.
        battle.player.moveSpeed=11.25f;
        var hp=battle.player.GetComponent<BattlePlayerHealth>();hp.maxHealth=137;hp.ignoreDamage=true;
        var energy=battle.player.GetComponent<DebugPlayerSpecialCombo>();energy.maxGauge=140;energy.alwaysFullGauge=false;energy.startingEnergyPercent=50;
        File.WriteAllText(State,JsonConvert.SerializeObject(state,Formatting.Indented));
        EditorSceneManager.playModeStartScene=null;EditorApplication.isPlaying=true;
        return new{starting=true,state.selectedStage,unsavedHealth=137,unsavedSpeed=11.25f,unsavedEnergyCapacity=140};
    }
    public static async Task<object> ObserveStandalone()
    {
        var expected=JsonConvert.DeserializeObject<EditorState>(File.ReadAllText(State));
        var until=DateTime.UtcNow.AddSeconds(28);LoadingScreenController screen=null;
        while(DateTime.UtcNow<until)
        {
            if(Application.isPlaying)screen=UnityEngine.Object.FindFirstObjectByType<LoadingScreenController>();
            if(screen!=null&&screen.IsStandaloneBattleEntry)break;
            await Task.Delay(20);
        }
        Check(screen!=null&&screen.IsStandaloneBattleEntry,"Standalone entry not intercepted");
        var battle=Battle();int instance=battle.GetInstanceID();
        Check(!battle.gameObject.activeInHierarchy&&battle.StartData==null,"Battle started underneath loading");
        Check(screen.destination==LoadingDestination.Battle&&!screen.IsPreview&&screen.EffectiveFillStartDelay==8,"Incorrect standalone loading mode");
        Check(screen.battleInstructions.activeInHierarchy&&screen.SelectedTip=="","Guide missing");
        float firstProgress=-1;bool full=false;float observedAt=screen.DisplaySecondsElapsed;
        while(DateTime.UtcNow<until&&LoadingScreenController.IsTransitioning)
        {
            Check(string.IsNullOrEmpty(screen.LastError),screen.LastError);
            if(screen.DisplayedProgress>0&&firstProgress<0)firstProgress=screen.DisplaySecondsElapsed;
            if(screen.DisplaySecondsElapsed<7.95f)
            {
                Check(screen.DisplayedProgress==0,"Progress began before eight seconds");
                Check(battle.StartData==null&&!battle.gameObject.activeInHierarchy,"Battle ran during the wait");
            }
            full|=screen.DisplayedProgress>=1;
            await Task.Delay(20);
        }
        Check(!LoadingScreenController.IsTransitioning&&firstProgress>=7.95f&&full,"Standalone loading failed to finish correctly");
        Check(Battle().GetInstanceID()==instance,"Battle instances were reloaded");
        Check(battle.StartData!=null&&battle.SelectedStageIndex==expected.selectedStage,"Selected boss was lost");
        Check(Mathf.Abs(battle.player.moveSpeed-11.25f)<.001f,"Inspector speed lost");
        var hp=battle.player.GetComponent<BattlePlayerHealth>();var energy=battle.player.GetComponent<DebugPlayerSpecialCombo>();
        Check(hp.maxHealth==137&&hp.Health==137,"Inspector health lost or replaced by Decision defaults");
        Check(energy.maxGauge==140&&energy.Gauge==70,"Inspector energy lost");
        Check(SceneManager.GetActiveScene()==battle.gameObject.scene,"Wrong active scene");
        Check(!SceneManager.GetSceneByPath(LoadingScreenController.LoadingScenePath).isLoaded,"Loading scene leaked");
        string[] roots=battle.gameObject.scene.GetRootGameObjects().Where(g=>g.activeSelf).Select(g=>g.name).OrderBy(n=>n).ToArray();
        Check(roots.SequenceEqual(expected.activeRoots),"Originally inactive previews were enabled");
        await Task.Delay(1200);
        Check(!LoadingScreenController.IsTransitioning&&UnityEngine.Object.FindFirstObjectByType<LoadingScreenController>()==null,"Loading loop occurred");
        var result=new{passed=true,observedAt,firstProgress,fullBarDrawn=full,instancesPreserved=true,selectedStage=battle.SelectedStageIndex,hp.maxHealth,speed=battle.player.moveSpeed,energy=energy.Gauge,noLoop=true};
        File.WriteAllText(Report,JsonConvert.SerializeObject(result,Formatting.Indented));return result;
    }
    public static async Task<object> VerifyNormalEntry()
    {
        Check(Application.isPlaying&&!LoadingScreenController.IsTransitioning,"Idle Play mode required");
        var data=new Battle3DStartData(2,16,0,11,11);int count=0;
        void Loaded(Scene scene,LoadSceneMode mode){if(scene.path==LoadingScreenController.LoadingScenePath)count++;}
        SceneManager.sceneLoaded+=Loaded;
        try
        {
            Check(Battle3DEntry.OpenThroughLoading(data),"Normal entry rejected");
            Check(!Battle3DEntry.OpenThroughLoading(new Battle3DStartData(1,0,0,0,0)),"Duplicate request accepted");
            var until=DateTime.UtcNow.AddSeconds(28);LoadingScreenController screen=null;float firstProgress=-1;bool full=false,seen=false;
            while(DateTime.UtcNow<until)
            {
                screen=UnityEngine.Object.FindFirstObjectByType<LoadingScreenController>();
                if(screen!=null)
                {
                    seen=true;Check(!screen.IsStandaloneBattleEntry&&screen.EffectiveFillStartDelay==5,"Normal entry incorrectly uses eight seconds");
                    Check(string.IsNullOrEmpty(screen.LastError),screen.LastError);
                    if(screen.DisplayedProgress>0&&firstProgress<0)firstProgress=screen.DisplaySecondsElapsed;
                    if(screen.DisplaySecondsElapsed<4.95f)Check(screen.DisplayedProgress==0,"Normal loading delay skipped");
                    full|=screen.DisplayedProgress>=1;
                }
                if(seen&&!LoadingScreenController.IsTransitioning&&screen==null)break;
                await Task.Delay(20);
            }
            Check(!LoadingScreenController.IsTransitioning&&seen&&count==1&&full&&firstProgress>=4.95f,"Normal route incomplete or repeated");
            var battle=Battle();var received=battle.StartData;
            Check(received!=null&&received.StageIndex==data.StageIndex&&battle.SelectedStageIndex==2&&
                received.Trait1==data.Trait1&&received.Trait2==data.Trait2&&received.Trait3==data.Trait3&&received.Trait4==data.Trait4,"Decision data changed");
            var hp=battle.player.GetComponent<BattlePlayerHealth>();var energy=battle.player.GetComponent<DebugPlayerSpecialCombo>();
            Check(hp.maxHealth==150&&battle.player.moveSpeed==7&&energy.maxGauge==130&&energy.Gauge==50,"Decision stat mapping regressed");
            var result=new{passed=true,count,firstProgress,stage=battle.SelectedStageIndex,hp.maxHealth,speed=battle.player.moveSpeed,energy=energy.Gauge,capacity=energy.maxGauge};
            File.WriteAllText("Assets/Loading/Authoring~/NormalBattleLoadingVerification.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
        }
        finally{SceneManager.sceneLoaded-=Loaded;}
    }
    public static object Stop(){EditorApplication.isPlaying=false;return "Stopping verification";}
    public static object Restore()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode,"Wait until Play stops");
        var state=JsonConvert.DeserializeObject<EditorState>(File.ReadAllText(State));
        EditorSceneManager.RestoreSceneManagerSetup(state.scenes);
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(state.playStart)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(state.playStart);
        foreach(var pair in state.hashes)Check(Hash(pair.Key)==pair.Value,"Scene asset changed: "+pair.Key);
        return new{restored=state.scenes.Select(s=>s.path).ToArray(),sceneFilesUnchanged=true};
    }
}
