using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;
using Object = UnityEngine.Object;

public static class PromoteEditedFinisherToBattle
{
    const string SourceScene = "Assets/Scenes/3DBattleDebugScene.unity";
    const string TargetScene = "Assets/Scenes/3DBattleScene.unity";
    const string Folder = "Assets/3DBattle/NewPlayer/PlayableAttacks/";
    const string AttackPath = Folder + "PlayerFinisher_RetreatRush.anim";
    const string PrepPath = Folder + "PlayerFinisher_Preparation.anim";
    const string SetPath = Folder + "PlayerCombatAttackSet.asset";
    const string ControllerPath = Folder + "NewPlayerAttackCombo.controller";
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static BattlePlayerController Player(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(g => g.GetComponentsInChildren<BattlePlayerController>(true))
        .Single(p => p.name == "New Player - Attack Combo");
    static JToken Data(Object obj)
    {
        var json = JObject.Parse(EditorJsonUtility.ToJson(obj));
        return json.Properties().Single().Value;
    }
    static bool SameClip(AnimationClip a, AnimationClip b)
    {
        var left = Data(a); var right = Data(b);
        left["m_Name"] = right["m_Name"] = "clip";
        return JToken.DeepEquals(left, right);
    }
    public static string Inspect() => JsonConvert.SerializeObject(new {
        animationPreview = AnimationMode.InAnimationMode(),
        paused = EditorApplication.isPaused,
        playing = EditorApplication.isPlaying,
        playModeStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
        scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .Select(s => new { s.path, s.isLoaded, s.isDirty, active = s == SceneManager.GetActiveScene() }).ToArray()
    });
    public static string Finish()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        Check(SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == TargetScene,
            "Only 3DBattleScene should remain open.");
        EditorApplication.isPaused = false;
        return Inspect();
    }
    static AnimationClip CopyClip(AnimationClip source, string path)
    {
        Check(source != null && !EditorUtility.IsDirty(source), "Source clip has unsaved changes.");
        Check(BattleCharacterAssetPaths.LoadMain(path) == null, "Destination already exists: " + path);
        Check(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path), "Copy failed: " + path);
        var clip = BattleCharacterAssetPaths.Load<AnimationClip>(path);
        clip.name = Path.GetFileNameWithoutExtension(path);
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssetIfDirty(clip);
        Check(SameClip(source, clip), "Copied animation differs from source.");
        return clip;
    }
    static void AddState(AnimatorController controller, AnimationClip clip)
    {
        var machine = controller.layers[0].stateMachine;
        Check(!machine.states.Any(s => s.state.name == clip.name), "Animator state already exists.");
        Undo.RecordObject(machine, "Connect promoted finisher animation");
        var state = machine.AddState(clip.name);
        state.motion = clip;
        state.writeDefaultValues = true;
        EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(controller);
    }
    public static string Apply()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Edit mode required.");
        Check(!AnimationMode.InAnimationMode(), "Stop animation preview before applying.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Check(!SceneManager.GetSceneAt(i).isDirty, "Unsaved scene: " + SceneManager.GetSceneAt(i).path);
        var scene = SceneManager.GetSceneByPath(TargetScene);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
        Check(scene.IsValid() && scene.isLoaded && !scene.isDirty, "Clean, loaded 3DBattleScene required.");
        Check(SceneManager.sceneCount == 1, "Keep only the production scene loaded.");
        var preview = EditorSceneManager.OpenPreviewScene(SourceScene);
        try
        {
            var src = Player(preview); var dst = Player(scene);
            var sourceCombo = src.GetComponent<NewPlayerAttackCombo>();
            var combo = dst.GetComponent<NewPlayerAttackCombo>();
            var sourceRules = src.GetComponent<DebugPlayerSpecialCombo>();
            var rules = dst.GetComponent<DebugPlayerSpecialCombo>();
            var sourceFeel = src.GetComponent<DebugPlayerCombatFeel>();
            var feel = dst.GetComponent<DebugPlayerCombatFeel>();
            var controller = dst.animator.runtimeAnimatorController as AnimatorController;
            Check(AssetDatabase.GetAssetPath(combo.motions) == SetPath && AssetDatabase.GetAssetPath(controller) == ControllerPath,
                "Unexpected production assets.");
            Check(sourceCombo.motions.attacks[0].clip.name == "DebugFinisher_RetreatRush_UserEdit" &&
                sourceRules.finisherPreparation != null, "Edited Debug motions are not connected.");
            var protectedObjects = dst.GetComponents<Component>().Where(c => c != feel && c != rules).Cast<Object>()
                .Concat(dst.GetComponentsInChildren<Transform>(true))
                .Concat(dst.viewCamera.GetComponents<Component>()).ToArray();
            var protectedJson = protectedObjects.Select(EditorJsonUtility.ToJson).ToArray();
            Directory.CreateDirectory("Temp/FinisherPromotion");
            foreach (var path in new[] { TargetScene, SetPath, ControllerPath })
                File.Copy(path, "Temp/FinisherPromotion/" + Path.GetFileName(path), true);

            var attack = CopyClip(sourceCombo.motions.attacks[0].clip, AttackPath);
            var prep = CopyClip(sourceRules.finisherPreparation, PrepPath);
            Undo.RecordObjects(new Object[] { combo.motions, rules, feel }, "Apply edited Debug finisher to battle");
            EditorUtility.CopySerialized(sourceCombo.motions, combo.motions);
            combo.motions.name = "PlayerCombatAttackSet";
            combo.motions.attacks[0].clip = attack;
            EditorUtility.CopySerialized(sourceRules, rules);
            rules.finisherPreparation = prep;
            EditorUtility.CopySerialized(sourceFeel, feel);
            AddState(controller, attack);
            AddState(controller, prep);
            foreach (var obj in new Object[] { combo.motions, rules, feel }) EditorUtility.SetDirty(obj);
            AssetDatabase.SaveAssetIfDirty(combo.motions);
            AssetDatabase.SaveAssetIfDirty(controller);
            for (int i = 0; i < protectedObjects.Length; i++)
                Check(EditorJsonUtility.ToJson(protectedObjects[i]) == protectedJson[i],
                    "Unrelated object changed: " + protectedObjects[i].name + " / " + protectedObjects[i].GetType().Name);
            EditorSceneManager.MarkSceneDirty(scene);
            Check(EditorSceneManager.SaveScene(scene), "Scene save failed.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        return Verify();
    }
    public static string Verify()
    {
        var scene = SceneManager.GetSceneByPath(TargetScene);
        Check(scene.IsValid() && scene.isLoaded, "Production scene is not loaded.");
        var preview = EditorSceneManager.OpenPreviewScene(SourceScene);
        try
        {
            var src = Player(preview); var dst = Player(scene);
            var sourceSet = src.GetComponent<NewPlayerAttackCombo>().motions;
            var set = dst.GetComponent<NewPlayerAttackCombo>().motions;
            var sourceRules = src.GetComponent<DebugPlayerSpecialCombo>();
            var rules = dst.GetComponent<DebugPlayerSpecialCombo>();
            Check(SameClip(sourceSet.attacks[0].clip, set.attacks[0].clip), "Finisher curves differ.");
            Check(SameClip(sourceRules.finisherPreparation, rules.finisherPreparation), "Preparation curves differ.");
            var a = Data(sourceSet); var b = Data(set);
            a["m_Name"] = b["m_Name"];
            a["attacks"][0]["clip"] = b["attacks"][0]["clip"].DeepClone();
            Check(JToken.DeepEquals(a, b), "Attack settings differ.");
            a = Data(sourceRules); b = Data(rules);
            if (b["m_GameObject"] != null) a["m_GameObject"] = b["m_GameObject"].DeepClone();
            a["finisherPreparation"] = b["finisherPreparation"].DeepClone();
            Check(JToken.DeepEquals(a, b), "Special-combo settings differ.");
            a = Data(src.GetComponent<DebugPlayerCombatFeel>()); b = Data(dst.GetComponent<DebugPlayerCombatFeel>());
            if (b["m_GameObject"] != null) a["m_GameObject"] = b["m_GameObject"].DeepClone();
            Check(JToken.DeepEquals(a, b), "Attack timing settings differ.");
            var controller = dst.animator.runtimeAnimatorController as AnimatorController;
            var states = controller.layers[0].stateMachine.states.Select(s => s.state).ToArray();
            foreach (var clip in set.attacks.Select(m => m.clip).Concat(set.links.Select(m => m.clip))
                .Concat(new[] { set.entry.clip, rules.finisherPreparation }))
                Check(states.Any(s => s.name == clip.name && s.motion == clip), "Animator state missing for " + clip.name);
            var model = dst.transform.Find("NewPlayer Visual/Knight Model Unified");
            Check(model.localPosition == Vector3.zero, "Player model must remain centered.");
            return JsonConvert.SerializeObject(new {
                passed = true, scene = scene.path, scene.isDirty,
                clip = AssetDatabase.GetAssetPath(set.attacks[0].clip), preparation = AssetDatabase.GetAssetPath(rules.finisherPreparation),
                copiedCurvesExact = true, allAttackSettingsMatchDebug = true,
                rules.finisherHoldSeconds, rules.finisherDashStartFrame, rules.finisherDashEndFrame,
                damageWindows = set.attacks[0].damageWindows.Select(w => new { start = w.x, end = w.y }).ToArray(),
                modelPosition = new[] { model.localPosition.x, model.localPosition.y, model.localPosition.z },
                loadedScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Select(s => s.path).ToArray()
            }, Formatting.Indented);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
}
