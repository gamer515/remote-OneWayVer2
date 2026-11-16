using System;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OneWay.Battle3D;

public static class PromoteDebugPlayer
{
    const string Source = "Assets/Scenes/3DBattleDebugScene.unity";
    const string Target = "Assets/Scenes/3DBattleScene.unity";
    const string AttackSet = "Assets/3DBattle/NewPlayer/PlayableAttacks/PlayerCombatAttackSet.asset";
    static BattlePlayerController Player(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<BattlePlayerController>(true))
        .Single(p => p.name == "New Player - Attack Combo");
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Copy<T>(T source, GameObject destination) where T : Component
    {
        Check(source != null, "Missing source " + typeof(T).Name);
        var component = destination.GetComponent<T>();
        if (component == null) component = (T)Undo.AddComponent(destination, typeof(T));
        else Undo.RecordObject(component, "Apply tested player settings");
        EditorUtility.CopySerialized(source, component);
        EditorUtility.SetDirty(component);
    }
    public static string Apply()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Edit mode required.");
        for (int i=0; i<SceneManager.sceneCount; i++)
            Check(!SceneManager.GetSceneAt(i).isDirty, "Unsaved scene: " + SceneManager.GetSceneAt(i).path);
        var source = EditorSceneManager.OpenPreviewScene(Source);
        try
        {
            var src = Player(source);
            var sourceCombo = src.GetComponent<NewPlayerAttackCombo>();
            Check(src.GetComponent<DebugPlayerCombatFeel>() != null && src.GetComponent<DebugPlayerSpecialCombo>() != null,
                "Source combat components missing.");
            var target = EditorSceneManager.OpenScene(Target, OpenSceneMode.Single);
            var dst = Player(target);
            var position = dst.transform.position; var rotation = dst.transform.rotation;
            var boss = dst.boss; var feedback = dst.feedback; var camera = dst.viewCamera;
            var body = dst.GetComponent<BattleArenaBody>();
            var playerJson = EditorJsonUtility.ToJson(dst);
            var bodyJson = EditorJsonUtility.ToJson(body);
            var battle = target.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Battle3DSceneController>(true)).Single();
            var battleJson = EditorJsonUtility.ToJson(battle);
            Check(battle.player == dst, "Scene controller does not reference the playable player.");
            Directory.CreateDirectory("Temp/PlayerPromotion");
            File.Copy(Target,"Temp/PlayerPromotion/3DBattleScene.before.unity",true);

            var set = AssetDatabase.LoadAssetAtPath<NewPlayerAttackSet>(AttackSet);
            if (set == null)
            {
                set = UnityEngine.Object.Instantiate(sourceCombo.motions);
                set.name = "PlayerCombatAttackSet";
                AssetDatabase.CreateAsset(set,AttackSet);
            }
            else
            {
                Undo.RecordObject(set,"Apply tested player motion data");
                EditorUtility.CopySerialized(sourceCombo.motions,set);
                set.name = "PlayerCombatAttackSet";
                EditorUtility.SetDirty(set);
            }
            Copy(sourceCombo,dst.gameObject);
            dst.GetComponent<NewPlayerAttackCombo>().motions = set;
            Copy(src.GetComponent<DebugPlayerCombatFeel>(),dst.gameObject);
            Copy(src.GetComponent<DebugPlayerSpecialCombo>(),dst.gameObject);

            var follow = camera.GetComponent<BattleFollowCamera>();
            var targetTransform = follow.target;
            var ignored = follow.ignoredColliders;
            Copy(src.viewCamera.GetComponent<BattleFollowCamera>(),camera.gameObject);
            follow.target = targetTransform;
            follow.ignoredColliders = ignored;
            Undo.RecordObjects(new UnityEngine.Object[]{camera,camera.transform},"Apply tested player framing");
            camera.fieldOfView = src.viewCamera.fieldOfView;
            follow.Snap();
            Check(dst.transform.position == position && dst.transform.rotation == rotation,"Player spawn changed.");
            Check(dst.boss == boss && dst.feedback == feedback && dst.viewCamera == camera,"Scene connections changed.");
            Check(EditorJsonUtility.ToJson(dst) == playerJson && EditorJsonUtility.ToJson(body) == bodyJson,
                "Existing player or arena connections changed.");
            Check(EditorJsonUtility.ToJson(battle) == battleJson,"Battle configuration changed.");
            Check(follow.target == dst.transform,"Camera target is not the playable player.");
            AssetDatabase.SaveAssetIfDirty(set);
            EditorSceneManager.MarkSceneDirty(target);
            Check(EditorSceneManager.SaveScene(target),"Failed to save battle scene.");
            Selection.activeGameObject = dst.gameObject;
            return Report();
        }
        finally { EditorSceneManager.ClosePreviewScene(source); }
    }
    public static string Report()
    {
        var scene = SceneManager.GetSceneByPath(Target);
        var player = Player(scene);
        var combo = player.GetComponent<NewPlayerAttackCombo>();
        var feel = player.GetComponent<DebugPlayerCombatFeel>();
        var special = player.GetComponent<DebugPlayerSpecialCombo>();
        var follow = player.viewCamera.GetComponent<BattleFollowCamera>();
        return JsonConvert.SerializeObject(new {
            scene=scene.path, scene.isDirty, player=player.name, player.gameObject.activeInHierarchy,
            player.moveSpeed,player.rollDistance,combo.playbackSpeed,combo.attackRetreatDistance,
            combo.skillDashDistance,combo.jumpHeightMultiplier,
            attackSet=AssetDatabase.GetAssetPath(combo.motions),combo.motions.order,
            feel.jumpCameraFollow,feel.hitStop,feel.lightHitStop,feel.heavyHitStop,
            special.finisherHoldSeconds,special.finisherPoseFrame,special.alwaysFullGauge,
            cameraOffset=new[]{follow.offset.x,follow.offset.y,follow.offset.z},
            cameraLookOffset=new[]{follow.lookOffset.x,follow.lookOffset.y,follow.lookOffset.z},
            referencesPreserved=true
        },Formatting.Indented);
    }
}
