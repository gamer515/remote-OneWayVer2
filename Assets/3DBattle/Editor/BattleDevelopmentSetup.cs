using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneWay.Battle3D.Editor
{
    public static class BattleDevelopmentSetup
    {
        [MenuItem("Tools/3D Battle/Set Up Stage Development")]
        public static void Setup()
        {
            var scene = SceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/3DBattleScene.unity")
                throw new InvalidOperationException("Open 3DBattleScene in Edit mode first.");
            var root = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BattlePlayerController>(true))
                .FirstOrDefault()?.transform.parent;
            if (root == null) throw new InvalidOperationException("Existing Battle3D player not found.");
            if (root.GetComponent<Battle3DSceneController>() != null)
                throw new InvalidOperationException("Stage development is already configured. Edit the existing controller.");
            var player = root.GetComponentInChildren<BattlePlayerController>(true);
            var boss = root.GetComponentInChildren<BattleBoss>(true);
            var arena = root.Find("Arena");
            if (boss == null || arena == null) throw new InvalidOperationException("Existing boss or Arena missing.");

            Folder("Assets/3DBattle", "Settings");
            Folder("Assets/3DBattle/Settings", "Stages");
            Folder("Assets/3DBattle/Settings", "Patterns");
            ConfigureAttackAnimation();
            var fan = Pattern("FrontalStrike", "Frontal Strike", 5f, 110f, 0.8f, 20f);
            var circle = Pattern("RadialSlam", "Radial Slam", 7f, 360f, 1.2f, 30f);
            var quick = Pattern("QuickStrike", "Quick Strike", 4f, 75f, 0.35f, 10f);

            Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Set up 3D battle stages");
            var controller = Undo.AddComponent<Battle3DSceneController>(root.gameObject);
            controller.player = player;
            controller.feedback = root.GetComponentInChildren<BattleCombatFeedback>(true);
            controller.followCamera = player.viewCamera.GetComponent<BattleFollowCamera>();
            if (player.GetComponent<BattlePlayerHealth>() == null) Undo.AddComponent<BattlePlayerHealth>(player.gameObject);
            var stageRoot = new GameObject("Stages").transform;
            Undo.RegisterCreatedObjectUndo(stageRoot.gameObject, "Create stage root");
            stageRoot.SetParent(root, false);
            var first = new GameObject("B1").AddComponent<BattleStage>();
            Undo.RegisterCreatedObjectUndo(first.gameObject, "Create B1");
            first.transform.SetParent(stageRoot, false);
            Undo.SetTransformParent(arena, first.transform, "Move arena to B1");
            Undo.SetTransformParent(boss.transform, first.transform, "Move boss to B1");
            // Older scene layouts keep arena boundary walls next to the arena.
            foreach (Transform child in root.Cast<Transform>().ToArray())
                if (child.name.StartsWith("Wall ", StringComparison.Ordinal))
                    Undo.SetTransformParent(child, first.transform, "Move wall to B1");
            first.boss = boss;
            first.playerSpawn = Spawn("Player Spawn", first.transform, player.transform);
            first.bossSpawn = Spawn("Boss Spawn", first.transform, boss.transform);
            first.patternRunner = Undo.AddComponent<BossPatternRunner>(boss.gameObject);
            var line = new GameObject("Attack Telegraph").AddComponent<LineRenderer>();
            line.transform.SetParent(boss.transform, false);
            line.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/3DBattle/Materials/Attack.mat");
            line.startWidth = line.endWidth = 0.09f;
            line.startColor = line.endColor = new Color(1f, 0.25f, 0.05f);
            line.enabled = false;
            first.patternRunner.telegraph = line;
            controller.stages = new BattleStage[3];
            controller.stages[0] = first;
            for (int i = 1; i <= 3; i++)
            {
                var stage = i == 1 ? first : UnityEngine.Object.Instantiate(first, stageRoot);
                stage.name = "B" + i;
                if (i > 1) Undo.RegisterCreatedObjectUndo(stage.gameObject, "Create stage template");
                string path = $"Assets/3DBattle/Settings/Stages/B{i}.asset";
                if (AssetDatabase.LoadAssetAtPath<BattleStageSettings>(path) != null)
                    throw new InvalidOperationException("Settings already exist: " + path);
                var settings = ScriptableObject.CreateInstance<BattleStageSettings>();
                settings.stageIndex = i;
                settings.displayName = "B" + i;
                settings.bossHealth = boss.maxHealth + (i - 1) * 250f;
                settings.patterns = i == 1 ? new BossAttackPattern[] { fan, circle }
                    : i == 2 ? new BossAttackPattern[] { quick, fan, circle }
                    : new BossAttackPattern[] { circle, quick, fan };
                AssetDatabase.CreateAsset(settings, path);
                stage.settings = settings;
                controller.stages[i - 1] = stage;
            }
            controller.SelectStage(1);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root.gameObject;
        }

        static Transform Spawn(string name, Transform parent, Transform source)
        {
            var result = new GameObject(name).transform;
            result.SetParent(parent, false);
            result.SetPositionAndRotation(source.position, source.rotation);
            return result;
        }

        public static void ConfigureAttackAnimation()
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                "Assets/3DBattle/Animations/BossBattle.controller");
            var clip = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/UnityAssets/Buildings_And_Terrains/Dungeon_FBX format/character-human.fbx")
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == "attack-melee-right");
            if (controller == null || clip == null) throw new InvalidOperationException("Boss attack animation is missing.");
            var machine = controller.layers[0].stateMachine;
            if (machine.states.Any(s => s.state.name == "Attack")) return;
            Undo.RegisterCompleteObjectUndo(controller, "Add boss attack animation");
            machine.AddState("Attack").motion = clip;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }

        static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static BossAreaAttackPattern Pattern(string name, string label, float radius, float angle, float windup, float damage)
        {
            string path = "Assets/3DBattle/Settings/Patterns/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<BossAreaAttackPattern>(path);
            if (existing != null) return existing;
            var pattern = ScriptableObject.CreateInstance<BossAreaAttackPattern>();
            pattern.displayName = label;
            pattern.radius = radius;
            pattern.angle = angle;
            pattern.windupSeconds = windup;
            pattern.damage = damage;
            AssetDatabase.CreateAsset(pattern, path);
            return pattern;
        }
    }
}
