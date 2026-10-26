using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OneWay.Battle3D.Editor
{
    public static class BattleSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/3DBattleScene.unity";
        const string MaterialPath = "Assets/3DBattle/Materials";
        const string KnightModelPath = "Assets/UnityAssets/KnightAsset/KnightCharacter.fbx";
        const string DungeonPath = "Assets/UnityAssets/Buildings_And_Terrains/Dungeon_FBX format/";

        [MenuItem("Tools/3D Battle/Build Battle Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open 3DBattleScene first.");
            if (scene.isDirty) throw new InvalidOperationException("Save or discard your scene edits before building.");
            if (GameObject.Find("Battle3D") != null) throw new InvalidOperationException("Battle3D already exists; this builder will not replace it.");
            if (!AssetDatabase.IsValidFolder(MaterialPath)) AssetDatabase.CreateFolder("Assets/3DBattle", "Materials");
            Material floor = Material("Arena", new Color(0.68f, 0.72f, 0.75f));
            Material wall = Material("Wall", new Color(0.19f, 0.25f, 0.33f));
            Material red = Material("Player", new Color(0.95f, 0.075f, 0.09f));
            Material blue = Material("Boss", new Color(0.025f, 0.38f, 0.95f));
            Material green = Material("Range", Color.white, true);
            Material attack = Material("Attack", Color.white, true);
            Transform root = new GameObject("Battle3D").transform;
            Primitive("Arena", PrimitiveType.Cube, root, new Vector3(0f, -0.3f, 0f), new Vector3(36f, 0.6f, 28f), floor);
            Primitive("Wall North", PrimitiveType.Cube, root, new Vector3(0f, 0.65f, 14f), new Vector3(37f, 1.3f, 1f), wall);
            Primitive("Wall South", PrimitiveType.Cube, root, new Vector3(0f, 0.65f, -14f), new Vector3(37f, 1.3f, 1f), wall);
            Primitive("Wall East", PrimitiveType.Cube, root, new Vector3(18f, 0.65f, 0f), new Vector3(1f, 1.3f, 28f), wall);
            Primitive("Wall West", PrimitiveType.Cube, root, new Vector3(-18f, 0.65f, 0f), new Vector3(1f, 1.3f, 28f), wall);

            GameObject playerObject = new GameObject("Player");
            playerObject.transform.SetParent(root);
            playerObject.transform.position = new Vector3(-4f, 0f, -4f);
            GameObject body = Primitive("Red Capsule", PrimitiveType.Capsule, playerObject.transform,
                new Vector3(0f, 1f, 0f), Vector3.one, red);
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            CharacterController controller = playerObject.AddComponent<CharacterController>();
            controller.center = Vector3.up;
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.skinWidth = 0.04f;
            controller.minMoveDistance = 0f;
            controller.stepOffset = 0.2f;
            BattlePlayerController player = playerObject.AddComponent<BattlePlayerController>();

            GameObject bossObject = new GameObject("Boss");
            bossObject.transform.SetParent(root);
            bossObject.transform.position = new Vector3(4f, 0f, 4f);
            GameObject bossBody = Primitive("Blue Boss", PrimitiveType.Capsule, bossObject.transform,
                new Vector3(0f, 1.7f, 0f), new Vector3(3.3f, 1.7f, 3.3f), blue);
            BattleBoss boss = bossObject.AddComponent<BattleBoss>();
            boss.body = bossBody.GetComponent<Renderer>();
            boss.hitbox = bossBody.GetComponent<Collider>();
            player.boss = boss;

            Camera camera = Camera.main;
            if (camera == null) { camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera"; }
            camera.orthographic = true;
            camera.orthographicSize = 11.5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 120f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.11f, 0.16f);
            camera.GetUniversalAdditionalCameraData().renderShadows = true;
            BattleFollowCamera follow = camera.GetComponent<BattleFollowCamera>();
            if (follow == null) follow = camera.gameObject.AddComponent<BattleFollowCamera>();
            follow.target = player.transform;
            follow.Snap();
            player.viewCamera = camera;

            Light light = new GameObject("Battle Sun").AddComponent<Light>();
            light.transform.SetParent(root);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(48f, -30f, 0f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.7f, 0.8f);
            RenderSettings.sun = light;

            BattleCombatFeedback feedback = new GameObject("Combat Feedback").AddComponent<BattleCombatFeedback>();
            feedback.transform.SetParent(root);
            feedback.player = player;
            feedback.boss = boss;
            feedback.rangeRing = Line("Attack Range", root, green, 0.055f);
            feedback.chargeRing = Line("Charge", root, attack, 0.09f);
            feedback.chargeRing.startColor = feedback.chargeRing.endColor = new Color(1f, 0.65f, 0.05f);
            feedback.chargeRing.enabled = false;
            feedback.attackLine = Line("Attack Effect", root, attack, 0.1f);
            feedback.attackLine.positionCount = 2;
            feedback.attackLine.enabled = false;
            Vector3[] ring = new Vector3[97];
            for (int i = 0; i < ring.Length; i++)
            {
                float angle = i / 96f * Mathf.PI * 2f;
                ring[i] = player.transform.position + new Vector3(Mathf.Cos(angle) * player.attackRange, 0.055f, Mathf.Sin(angle) * player.attackRange);
            }
            feedback.rangeRing.positionCount = ring.Length;
            feedback.rangeRing.SetPositions(ring);
            feedback.rangeRing.startColor = feedback.rangeRing.endColor = new Color(0.1f, 0.65f, 0.25f);
            player.feedback = feedback;
            ApplyCharacterModels(player, boss);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Scene save failed.");
            Selection.activeGameObject = playerObject;
        }

        [MenuItem("Tools/3D Battle/Apply Knight and Dungeon Boss")]
        public static void UpgradeCharacters()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open the saved 3DBattleScene before applying characters.");
            BattlePlayerController player = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BattlePlayerController>(true)).Single();
            ApplyCharacterModels(player, player.boss);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Scene save failed.");
            Selection.activeGameObject = player.gameObject;
        }

        static void ApplyCharacterModels(BattlePlayerController player, BattleBoss boss)
        {
            EnsureFolder("Prefabs");
            EnsureFolder("Animations");
            AnimationClip idle = Clip(KnightModelPath, "HumanArmature|Idle_swordRight");
            AnimationClip run = Clip(KnightModelPath, "HumanArmature|Run_swordRight");
            AnimationClip normal = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/UnityAssets/KnightAsset/KnightAnim/HumanArmature_Idle_swordAttack.anim");
            if (normal == null) throw new InvalidOperationException("Existing knight attack clip is missing.");
            AnimationClip special = Clip(KnightModelPath, "HumanArmature|swordAttackJump");
            AnimatorController knightController = Controller("KnightBattle",
                ("Idle", idle, 1f, true), ("Run", run, 1.15f, true),
                ("Attack", normal, 1.5f, false), ("Special", special, 1.25f, false));
            AnimationClip bossIdle = Clip(DungeonPath + "character-human.fbx", "idle");
            AnimatorController bossController = Controller("BossBattle",
                ("Idle", bossIdle, 1f, true),
                ("Walk", Clip(DungeonPath + "character-human.fbx", "walk"), 0.8f, true),
                ("Dead", Clip(DungeonPath + "character-human.fbx", "die"), 1f, false));

            GameObject knightPrefab = CreateKnightPrefab(knightController, idle);
            // Preserve the dedicated combo when characters are rebuilt from the menu.
            if (System.IO.File.Exists("Assets/3DBattle/Authoring~/combo_samples.json"))
                BattleComboAuthoring.Build();
            GameObject bossPrefab = CreateBossPrefab(bossController, bossIdle);
            // Only replace our visual children; keep the live combat roots and their references.
            RemoveChild(player.transform, "Red Capsule");
            RemoveChild(player.transform, "Knight Visual");
            RemoveChild(boss.transform, "Blue Boss");
            RemoveChild(boss.transform, "Boss Visual");
            GameObject knight = (GameObject)PrefabUtility.InstantiatePrefab(knightPrefab, player.transform);
            knight.name = "Knight Visual";
            player.animator = knight.GetComponent<Animator>();
            player.normalAnimationSeconds = normal.length / 1.5f;
            player.specialAnimationSeconds = special.length / 1.25f;
            player.transform.rotation = Quaternion.LookRotation(boss.transform.position - player.transform.position);
            CharacterController motor = player.GetComponent<CharacterController>();
            motor.height = 2.4f;
            motor.center = Vector3.up * 1.2f;
            // Keep collision radius and range unchanged; only the character's height changes.
            GameObject bossVisual = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab, boss.transform);
            bossVisual.name = "Boss Visual";
            boss.animator = bossVisual.GetComponent<Animator>();
            boss.bodyParts = bossVisual.GetComponentsInChildren<Renderer>();
            boss.body = boss.bodyParts.First(r => r.name == "body-mesh");
            CapsuleCollider hitbox = boss.GetComponent<CapsuleCollider>();
            if (hitbox == null) hitbox = boss.gameObject.AddComponent<CapsuleCollider>();
            hitbox.height = 4.4f;
            hitbox.radius = 1.65f;
            hitbox.center = Vector3.up * 2.2f;
            boss.hitbox = hitbox;
            boss.transform.rotation = Quaternion.LookRotation(player.transform.position - boss.transform.position);
            idle.SampleAnimation(knight, 0f);
            bossIdle.SampleAnimation(bossVisual, 0f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(knight.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(bossVisual.transform);
            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(boss);
        }

        static GameObject CreateKnightPrefab(AnimatorController controller, AnimationClip idle)
        {
            GameObject visual = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Decision_YYS/Prafabs/People/KnightCharacter_Copy.prefab"));
            try
            {
                visual.name = "Knight Visual";
                visual.transform.position = Vector3.zero;
                visual.transform.rotation = Quaternion.identity;
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                // The source includes a second copy of the body mesh used by another scene.
                RemoveChild(visual.transform, "Knight_Center");
                Animator animator = visual.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                idle.SampleAnimation(visual, 0f);
                NormalizeHeight(visual, 2.4f);
                Renderer armor = visual.GetComponentsInChildren<Renderer>().First(r => r.name == "Knight");
                Material redArmor = OwnedMaterial("KnightArmor", armor.sharedMaterials[0], new Color(0.55f, 0.1f, 0.12f), 0.55f);
                foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.name != "Knight" && renderer.name != "ShoulderPads") continue;
                    Material[] materials = renderer.sharedMaterials;
                    materials[0] = redArmor;
                    renderer.sharedMaterials = materials;
                }
                return PrefabUtility.SaveAsPrefabAsset(visual, "Assets/3DBattle/Prefabs/BattleKnight.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(visual); }
        }

        static GameObject CreateBossPrefab(AnimatorController controller, AnimationClip idle)
        {
            GameObject visual = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DungeonPath + "character-human.fbx"));
            try
            {
                visual.name = "Boss Visual";
                Animator animator = visual.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                idle.SampleAnimation(visual, 0f);
                NormalizeHeight(visual, 4.4f);
                Renderer body = visual.GetComponentsInChildren<Renderer>().First(r => r.name == "body-mesh");
                body.sharedMaterial = OwnedMaterial("BossArmor", body.sharedMaterial, new Color(0.3f, 0.55f, 1f), 0.25f);
                Transform rightArm = visual.GetComponentsInChildren<Transform>().First(t => t.name == "arm-right");
                Transform leftArm = visual.GetComponentsInChildren<Transform>().First(t => t.name == "arm-left");
                float scale = visual.transform.localScale.x;
                GameObject spear = MountEquipment("weapon-spear.fbx", "Commander's Spear", rightArm,
                    new Vector3(0.65f, 0f, 0f), visual.transform, new Vector3(0f, -0.28f, 0f) * scale);
                GameObject shield = MountEquipment("shield-rectangle.fbx", "Commander's Shield", leftArm,
                    new Vector3(-0.65f, 0f, 0f), visual.transform, new Vector3(0f, -0.27f, 0.12f) * scale);
                Material equipment = OwnedMaterial("BossEquipment", spear.GetComponentInChildren<Renderer>().sharedMaterial,
                    new Color(1f, 0.8f, 0.45f), 0.45f);
                foreach (Renderer renderer in spear.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = equipment;
                foreach (Renderer renderer in shield.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = equipment;
                return PrefabUtility.SaveAsPrefabAsset(visual, "Assets/3DBattle/Prefabs/DungeonBoss.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(visual); }
        }

        static GameObject MountEquipment(string file, string name, Transform bone, Vector3 handOffset,
            Transform visual, Vector3 offset)
        {
            GameObject equipment = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DungeonPath + file));
            equipment.name = name;
            equipment.transform.localScale = Vector3.one * visual.localScale.x;
            equipment.transform.position = bone.TransformPoint(handOffset) + offset;
            equipment.transform.rotation = visual.rotation;
            equipment.transform.SetParent(bone, true);
            return equipment;
        }

        static void NormalizeHeight(GameObject visual, float height)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            visual.transform.localScale *= height / Mathf.Max(0.01f, bounds.size.y);
            bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            visual.transform.position -= Vector3.up * bounds.min.y;
        }

        static Material OwnedMaterial(string name, Material source, Color tint, float metallic)
        {
            string path = MaterialPath + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
            material.color = tint;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static AnimationClip Clip(string path, string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            if (clip == null) throw new InvalidOperationException("Missing existing clip: " + name);
            return clip;
        }

        static AnimatorController Controller(string name, params (string name, AnimationClip clip, float speed, bool loop)[] clips)
        {
            string path = "Assets/3DBattle/Animations/" + name + ".controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (var entry in clips)
            {
                AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == entry.name)
                    ?? machine.AddState(entry.name);
                state.motion = entry.clip;
                state.speed = entry.speed;
                state.writeDefaultValues = true;
                foreach (AnimatorStateTransition transition in state.transitions) state.RemoveTransition(transition);
                // Some supplied clips are not imported as looping. Loop through the controller
                // without changing the shared FBX importer or original animation asset.
                if (entry.loop)
                {
                    AnimatorStateTransition transition = state.AddTransition(state);
                    transition.hasExitTime = true;
                    transition.exitTime = 1f;
                    transition.duration = 0f;
                    EditorUtility.SetDirty(transition);
                }
                if (entry.name == "Idle") machine.defaultState = state;
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        static void EnsureFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder("Assets/3DBattle/" + folder)) AssetDatabase.CreateFolder("Assets/3DBattle", folder);
        }

        static void RemoveChild(Transform root, string name)
        {
            Transform child = root.Find(name);
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        static Material Material(string name, Color color, bool unlit = false)
        {
            string path = MaterialPath + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Required URP shader is missing.");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.22f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static LineRenderer Line(string name, Transform parent, Material material, float width)
        {
            LineRenderer line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent);
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.startWidth = line.endWidth = width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCornerVertices = line.numCapVertices = 3;
            return line;
        }

        // Run in Play Mode. Exercises the same input state machine and physics as Update,
        // using temporary clones so the visible battle state is preserved.
        public static string ValidateRuntime()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            BattlePlayerController source = UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
            var checks = new List<string>();
            GameObject fixture = new GameObject("Battle Validation Fixture");
            try
            {
                BattlePlayerController player = UnityEngine.Object.Instantiate(source, fixture.transform);
                player.enabled = false;
                player.feedback = null;
                BattleBoss boss = UnityEngine.Object.Instantiate(source.boss, fixture.transform);
                boss.enabled = false;
                boss.patrolEnabled = false;
                player.boss = boss;
                CharacterController motor = player.GetComponent<CharacterController>();
                Vector3 centre = new Vector3(1000f, 0f, 1000f);
                motor.enabled = false;
                player.transform.position = centre;
                motor.enabled = true;
                boss.transform.position = centre + Vector3.forward * 4f;
                Physics.SyncTransforms();
                void Check(bool condition, string name)
                {
                    if (!condition) throw new InvalidOperationException("FAILED: " + name);
                    checks.Add(name);
                }
                void Input(bool hover = true, bool down = false, bool up = false, float dt = 0f, bool right = false) =>
                    player.ProcessPointerFrame(centre + Vector3.forward * 10f, true, hover, right, down, up, dt);

                float health = boss.Health;
                Check(player.BossInRange, "Boss surface inside range");
                Input(down: true);
                Check(boss.Health == health, "No damage on initial press");
                Input(up: true, dt: 0.1f);
                Check(boss.Health == health - player.normalDamage, "Short click is one normal attack");
                Check(player.animator != null && !player.animator.applyRootMotion, "Knight animation is wired without root motion");
                player.animator.Update(0f);
                Check(player.animator.GetCurrentAnimatorStateInfo(0).IsName(player.UsesNewNormalAttack ? "AttackNew" : "Attack"), "Normal input starts the supplied sword attack");
                if (player.UsesNewNormalAttack) Check(player.NormalAttackStartFrame == 0, "New normal attack begins at frame zero");
                Transform sword = player.animator.GetComponentsInChildren<Transform>().First(t => t.name == "Sword");
                Vector3 swordPosition = sword.position;
                Quaternion swordRotation = sword.rotation;
                player.animator.Update(0.2f);
                Check(Vector3.Distance(swordPosition, sword.position) > 0.01f || Quaternion.Angle(swordRotation, sword.rotation) > 1f,
                    "Attack clip actually moves the equipped sword");
                Input(dt: 1f);
                health = boss.Health;
                Input(down: true);
                Input(hover: false, dt: player.chargeSeconds + 0.1f);
                Check(player.Charge01 == 1f, "Moving cursor away preserves charge");
                Input(hover: false, up: true);
                Check(boss.Health == health - player.specialDamage, "Hold release is one special attack");
                player.animator.Update(0.1f);
                Check(player.animator.GetCurrentAnimatorStateInfo(0).IsName("Special"), "Charged input starts the supplied jump attack");
                Input(dt: 1f);
                health = boss.Health;
                Input(down: true);
                boss.transform.position = centre + Vector3.forward * 10f;
                Physics.SyncTransforms();
                Input(dt: 1f);
                Check(player.State == BattlePlayerController.PressState.Cancelled, "Leaving range cancels charge");
                boss.transform.position = centre + Vector3.forward * 4f;
                Physics.SyncTransforms();
                Input(up: true);
                Check(boss.Health == health && !player.IsDashing, "Reentering range does not revive cancelled attack");
                Input(down: true);
                Input(dt: 1f, right: true);
                Input(up: true);
                Check(boss.Health == health && player.IsMoving, "Right click cancels charge and starts movement");
                player.CancelInput();
                Input(hover: false, down: true);
                Check(player.IsDashing, "Ground click dashes with boss in range");
                player.Move(player.dashDuration);
                player.CancelInput();
                motor.enabled = false;
                player.transform.position = centre;
                motor.enabled = true;
                boss.transform.position = centre + Vector3.forward * 10f;
                Physics.SyncTransforms();
                Input(dt: 1f);
                Input(down: true);
                Check(player.IsDashing, "Out-of-range boss click dashes");
                player.Move(player.dashDuration);
                Input(up: true);
                Check(boss.Health == health, "Dash release does not attack");
                player.CancelInput();
                motor.enabled = false;
                player.transform.position = centre;
                motor.enabled = true;
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(fixture.transform);
                wall.transform.position = centre + new Vector3(0f, 1f, 2f);
                wall.transform.localScale = new Vector3(10f, 3f, 0.3f);
                Physics.SyncTransforms();
                Input(dt: 1f);
                Input(hover: false, down: true);
                player.Move(player.dashDuration);
                Check(player.transform.position.z < centre.z + 1.5f, "Dash cannot cross a thin wall");
                player.CancelInput();
                wall.SetActive(false);
                motor.enabled = false;
                player.transform.position = centre;
                motor.enabled = true;
                boss.transform.position = centre + Vector3.forward * 4f;
                Physics.SyncTransforms();
                Input(dt: 1f);
                Input(hover: false, down: true);
                player.Move(player.dashDuration);
                Check(player.transform.position.z < centre.z + 2f, "Dash cannot cross the boss");
                player.CancelInput();
                Check(player.State == BattlePlayerController.PressState.Idle && !player.IsDashing && !player.IsMoving,
                    "Focus-loss cleanup clears held input and movement");
                // Four-step combo, single-slot input buffer, timeout and movement cancellation.
                boss.ResetBoss();
                boss.transform.position = centre + Vector3.forward * 4f;
                Physics.SyncTransforms();
                Input(dt: 2f);
                health = boss.Health;
                Input(down: true); Input(up: true, dt: .05f);
                Check(player.ComboStep == 1 && boss.Health == health - player.normalDamage, "Combo begins with slash one");
                Input(down: true); Input(up: true, dt: .05f);
                Check(player.ComboStep == 1 && boss.Health == health - player.normalDamage, "Fast click buffers without early damage");
                float firstComboGate = player.ComboChainDelay(0);
                Input(dt: firstComboGate);
                Check(player.ComboStep == 2 && boss.Health == health - player.normalDamage * 2, "Buffered slash two fires once");
                if (player.UsesNewNormalAttack) Check(player.NormalAttackStartFrame == 6, "Repeated normal attack begins at frame six");
                Input(down: true); Input(up: true, dt: .05f); Input(dt: player.ComboChainDelay(1));
                Check(player.ComboStep == 3, "Third tap starts spinning slash");
                if (player.UsesNewNormalAttack) { Input(down: true); Input(up: true, dt: .05f); Input(dt: player.ComboChainDelay(1)); }
                else { Input(dt: .6f); Input(down: true); Input(up: true, dt: .05f); }
                Check(player.ComboStep == 4 && boss.Health == health - player.normalDamage * 4, "Fourth tap finishes four-hit chain");
                Input(dt: 2f); Input(down: true); Input(up: true, dt: .05f);
                Check(player.ComboStep == 1, "Combo timeout restarts at slash one");
                if (player.UsesNewNormalAttack) Check(player.NormalAttackStartFrame == 0, "Stopped chain restarts at frame zero");
                Input(down: true); Input(up: true, dt: .05f);
                health = boss.Health;
                Input(right: true); Input(dt: 1f);
                Check(player.ComboStep == 0 && boss.Health == health, "Movement discards buffered attack");
                player.CancelInput();
                // The first two slash windows close at their authored chain frames.
                boss.ResetBoss(); boss.transform.position = centre + Vector3.forward * 4f;
                Physics.SyncTransforms();
                Input(dt: 2f); Input(down: true); Input(up: true, dt: .05f);
                Input(dt: player.ComboChainDelay(0));
                Input(down: true); Input(up: true, dt: .02f);
                Check(player.ComboStep == 1, "Press at slash-one deadline restarts slash one");
                Input(dt: player.ComboChainDelay(0) - .01f);
                Input(down: true); Input(up: true, dt: .02f);
                Check(player.ComboStep == 2, "Press before deadline may release just after it");
                Input(dt: player.ComboChainDelay(1));
                Input(down: true); Input(up: true, dt: .02f);
                Check(player.ComboStep == 1, "Press at slash-two deadline restarts slash one");
                Input(down: true); Input(up: true, dt: .02f); Input(dt: player.ComboChainDelay(0));
                Check(player.ComboStep == 2, "Early buffered press still chains to slash two");
                Input(dt: player.ComboChainDelay(1) + .03f);
                Check(player.ComboStep == 2, "Unqueued slash two continues its recovery");
                Input(down: true); Input(up: true, dt: .02f);
                Check(player.ComboStep == 1, "Press during slash-two recovery restarts slash one");
                player.CancelInput();
                boss.TakeDamage(boss.maxHealth);
                Check(!boss.IsAlive && !player.BossInRange, "Defeated boss cannot be attacked");
                Check(boss.bodyParts.Length == 4, "Boss body, head, spear and shield receive damage feedback");
                Check(boss.animator.HasState(0, Animator.StringToHash("Base Layer.Walk")) &&
                    boss.animator.HasState(0, Animator.StringToHash("Base Layer.Dead")), "Boss movement and death clips are connected");
                Transform spear = boss.GetComponentsInChildren<Transform>().First(t => t.name == "Commander's Spear");
                Transform shield = boss.GetComponentsInChildren<Transform>().First(t => t.name == "Commander's Shield");
                Check(spear.parent.name == "arm-right" && shield.parent.name == "arm-left", "Boss equipment follows the correct arm bones");
                return "PASS (" + checks.Count + "): " + string.Join("; ", checks);
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }
    }
}
