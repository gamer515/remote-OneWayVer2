using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class OmniBattleIntegrationSetup
{
    public static object CheckBattleSignals()
    {
        var preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/3DBattleScene.unity");
        try
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var controller = preview.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Battle3DSceneController>(true)).Single();
            var boss = controller.FindStage(1).boss;
            var health = controller.player.GetComponent<BattlePlayerHealth>();
            var update = typeof(Battle3DSceneController).GetMethod("Update", flags);
            int callbacks = 0, cases = 0;
            foreach (var state in new[] { (boss: 0f, player: 100f, won: true), (boss: 100f, player: 0f, won: false), (boss: 0f, player: 0f, won: false) })
            {
                typeof(BattleBoss).GetProperty("Health").SetValue(boss, state.boss);
                typeof(BattlePlayerHealth).GetProperty("Health").SetValue(health, state.player);
                typeof(Battle3DSceneController).GetField("battleReady", flags).SetValue(controller, true);
                typeof(Battle3DSceneController).GetField("returnRequested", flags).SetValue(controller, false);
                typeof(Battle3DSceneController).GetField("returnAt", flags).SetValue(controller, -1d);
                bool? outcome = null;
                typeof(Battle3DSceneController).GetField("finishFinalBattle", flags).SetValue(controller,
                    (Func<bool,bool>)(won => { callbacks++; outcome = won; return true; }));
                update.Invoke(controller, null);
                if (!controller.IsReturnPending || outcome != null) throw new Exception("Outcome delay was not respected");
                typeof(Battle3DSceneController).GetField("returnAt", flags).SetValue(controller, UnityEngine.Time.realtimeSinceStartupAsDouble - 1d);
                update.Invoke(controller, null); update.Invoke(controller, null);
                cases++;
                if (outcome != state.won || callbacks != cases) throw new Exception("Boss/player outcome or duplicate transition incorrect");
            }
            return new { cases, callbacks, victory = true, death = true, simultaneousDeathCountsAsDeath = true, sceneFilesModified = false };
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    public static object Configure()
    {
        const string path = "Assets/Scenes/3DBattleScene.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) throw new Exception("3D battle scene missing");
        var scenes = EditorBuildSettings.scenes.ToList();
        var existing = scenes.Find(s => s.path == path);
        if (existing == null) scenes.Add(new EditorBuildSettingsScene(path, true));
        else existing.enabled = true;
        EditorBuildSettings.scenes = scenes.ToArray();
        var global = EditorBuildSettings.globalScenes.ToList();
        var globalBattle = global.Find(s => s.path == path);
        if (globalBattle == null) global.Add(new EditorBuildSettingsScene(path, true));
        else globalBattle.enabled = true;
        EditorBuildSettings.globalScenes = global.ToArray();
        // Persist global settings as well as the currently active build-profile scene list.
        var settings = UnityEditor.Unsupported.GetSerializedAssetInterfaceSingleton("EditorBuildSettings");
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        if (!System.IO.File.ReadAllText("ProjectSettings/EditorBuildSettings.asset").Contains(path))
            throw new Exception("Final battle build registration was not persisted");
        var preview = EditorSceneManager.OpenPreviewScene(path);
        try
        {
            var controller = preview.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Battle3DSceneController>(true)).Single();
            var stage = controller.FindStage(1);
            if (controller.player == null || stage?.boss == null || stage.patternRunner == null || stage.playerSpawn == null || stage.bossSpawn == null)
                throw new Exception("Final boss B1 references incomplete");
            return new { finalBattleScene = path, enabledInBuild = true, bossStage = stage.StageIndex,
                player = controller.player.name, boss = stage.boss.name,
                combo = StoryGenerationSettings.Read().combo, mode = StoryGenerationSettings.Read().mode,
                keyConfigured = !string.IsNullOrWhiteSpace(StoryGenerationSettings.ApiKey()), buildRegistrationPersisted = true };
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
}
