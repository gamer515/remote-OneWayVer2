using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    // Blender samples are retargeted onto a temporary copy of our own prefab.
    // No shared FBX importer, original clip, or project setting is edited.
    public static class BattleComboAuthoring
    {
        const string Folder = "Assets/3DBattle/Animations/";
        [Serializable] public class BonePose { public string name; public float[] position; public float[] rotation; }
        [Serializable] public class Sample { public float time; public float forwardTravel; public BonePose[] bones; }
        [Serializable] public class ClipData { public string name; public float duration; public Sample[] samples; }
        [Serializable] public class Export { public BonePose[] reference; public ClipData[] clips; }
        static Vector3 Position(BonePose b) => new Vector3(-b.position[0], b.position[2], -b.position[1]);
        // Reflection from Blender's Z-up bone space to the source FBX's Unity basis.
        static Quaternion Rotation(BonePose b) => new Quaternion(b.rotation[1], -b.rotation[3], b.rotation[2], b.rotation[0]);

        [MenuItem("Tools/3D Battle/Rebuild Sword Combo from Blender Samples")]
        public static void Build() => BuildSelected(null);

        [MenuItem("Tools/3D Battle/Rebuild Only VoltSlash01 from Blender Samples")]
        public static void BuildFirstSlash() => BuildSelected("VoltSlash01");

        [MenuItem("Tools/3D Battle/Rebuild New Normal Attack")]
        public static void BuildNewNormalAttack() => BuildSelected("voltslashnew01");

        public static void ConfigureNormalAttackSpeed()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "KnightBattle.controller");
            if (!controller.parameters.Any(p => p.name == "NormalAttackSpeed"))
                controller.AddParameter(new AnimatorControllerParameter {
                    name = "NormalAttackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1.2f });
            var state = controller.layers[0].stateMachine.states.Single(s => s.state.name == "AttackNew").state;
            state.speed = 1f;
            state.speedParameter = "NormalAttackSpeed";
            state.speedParameterActive = true;
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
        }

        [MenuItem("Tools/3D Battle/Rebuild VoltSlash01 and 02 from Blender Samples")]
        public static void BuildFirstTwoSlashes()
        {
            BuildSelected("VoltSlash01");
            BuildSelected("VoltSlash02");
        }

        static void BuildSelected(string onlyClip)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            Export data = JsonUtility.FromJson<Export>(File.ReadAllText("Assets/3DBattle/Authoring~/combo_samples.json"));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Prefabs/BattleKnight.prefab");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "KnightBattle.controller");
            if (prefab == null || controller == null) throw new InvalidOperationException("Battle knight assets are missing.");
            var idle = controller.animationClips.First(c => c.name.EndsWith("Idle_swordRight"));
            GameObject rig = UnityEngine.Object.Instantiate(prefab);
            rig.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                idle.SampleAnimation(rig, 0f);
                var reference = data.reference.ToDictionary(b => b.name);
                Transform[] bones = rig.GetComponentsInChildren<Transform>().Where(t => reference.ContainsKey(t.name)).ToArray();
                var positions = bones.ToDictionary(t => t.name, t => t.position);
                var rotations = bones.ToDictionary(t => t.name, t => t.rotation);
                float scale = Vector3.Distance(positions["Head"], positions["Hips"]) /
                    Vector3.Distance(Position(reference["Head"]), Position(reference["Hips"]));
                var machine = controller.layers[0].stateMachine;
                for (int index = 0; index < data.clips.Length; index++)
                {
                    ClipData source = data.clips[index];
                    if (onlyClip != null && source.name != onlyClip) continue;
                    var curves = bones.ToDictionary(t => t.name, t => Enumerable.Range(0, 7).Select(_ => new AnimationCurve()).ToArray());
                    var previous = new Dictionary<string, Quaternion>();
                    foreach (Sample sample in source.samples)
                    {
                        var frame = sample.bones.ToDictionary(b => b.name);
                        foreach (Transform bone in bones)
                        {
                            BonePose pose = frame[bone.name];
                            BonePose origin = reference[bone.name];
                            // Extract forward travel; CharacterController applies it with collision.
                            bone.SetPositionAndRotation(positions[bone.name] +
                                (Position(pose) - Position(origin) - Vector3.forward * sample.forwardTravel) * scale,
                                Rotation(pose) * Quaternion.Inverse(Rotation(origin)) * rotations[bone.name]);
                            Quaternion q = bone.localRotation;
                            if (previous.TryGetValue(bone.name, out Quaternion old) && Quaternion.Dot(old, q) < 0f)
                                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                            previous[bone.name] = q;
                            Vector3 p = bone.localPosition;
                            float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
                            for (int c = 0; c < 7; c++) curves[bone.name][c].AddKey(sample.time, values[c]);
                        }
                    }
                    string path = Folder + source.name + ".anim";
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
                    clip.ClearCurves();
                    clip.name = source.name;
                    clip.frameRate = 60f;
                    string[] props = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                        "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                    foreach (Transform bone in bones)
                        for (int c = 0; c < props.Length; c++)
                        {
                            AnimationCurve curve = curves[bone.name][c];
                            for (int k = 0; k < curve.length; k++)
                            {
                                AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                                AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                            }
                            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(
                                AnimationUtility.CalculateTransformPath(bone, rig.transform), typeof(Transform), props[c]), curve);
                        }
                    clip.EnsureQuaternionContinuity();
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    EditorUtility.SetDirty(clip);
                    AssetDatabase.SaveAssetIfDirty(clip);
                    string stateName = source.name == "voltslashnew01" ? "AttackNew"
                        : index == 0 ? "Attack" : "Attack" + (index + 1);
                    AnimatorState state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName)
                        ?? machine.AddState(stateName, new Vector3(330, 80 + index * 70));
                    state.motion = clip;
                    state.speed = 1f;
                    foreach (var transition in state.transitions) state.RemoveTransition(transition);
                    EditorUtility.SetDirty(state);
                }
                EditorUtility.SetDirty(machine);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                if (onlyClip == null || onlyClip == "voltslashnew01") ConfigureNormalAttackSpeed();
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }

        public static string ValidateClips()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "KnightBattle.controller");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Prefabs/BattleKnight.prefab");
            GameObject rig = UnityEngine.Object.Instantiate(prefab);
            rig.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var sword = rig.GetComponentsInChildren<Transform>().Single(t => t.name == "Sword");
                var result = new List<string>();
                for (int i = 1; i <= 4; i++)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "VoltSlash0" + i + ".anim");
                    if (clip == null || clip.length < .4f) throw new Exception("Missing/short combo clip " + i);
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        if (rig.transform.Find(binding.path) == null) throw new Exception("Missing bone " + binding.path);
                    clip.SampleAnimation(rig, 0);
                    Vector3 start = sword.position;
                    float maxTravel = 0;
                    for (int f = 0; f <= 60; f++)
                    {
                        clip.SampleAnimation(rig, clip.length * f / 60f);
                        maxTravel = Mathf.Max(maxTravel, Vector3.Distance(start, sword.position));
                        foreach (Transform t in rig.GetComponentsInChildren<Transform>())
                            if (float.IsNaN(t.position.x) || float.IsInfinity(t.position.x)) throw new Exception("Invalid pose");
                    }
                    if (maxTravel < .25f) throw new Exception("Sword does not swing " + i);
                    string name = i == 1 ? "Attack" : "Attack" + i;
                    var state = controller.layers[0].stateMachine.states.Single(s => s.state.name == name).state;
                    if (state.motion != clip || state.speed != 1) throw new Exception("Wrong combo state " + name);
                    result.Add(name + ": " + clip.length.ToString("0.00") + "s, sword travel " + maxTravel.ToString("0.00"));
                }
                return string.Join("; ", result);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }

        public static string RenderPreview()
        {
            const int width = 360, height = 360;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Prefabs/BattleKnight.prefab");
            GameObject rig = UnityEngine.Object.Instantiate(prefab);
            GameObject cameraObject = new GameObject("Combo Preview Camera");
            rig.hideFlags = cameraObject.hideFlags = HideFlags.HideAndDontSave;
            var atlas = new Texture2D(width * 4, height * 3, TextureFormat.RGB24, false);
            var rt = new RenderTexture(width, height, 24);
            RenderTexture previous = RenderTexture.active;
            try
            {
                rig.transform.SetPositionAndRotation(new Vector3(1000, 0, 1000), Quaternion.identity);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 2.15f;
                Vector3 target = rig.transform.position + Vector3.up * 1.2f;
                camera.transform.position = target + new Vector3(4, 2.5f, 6);
                camera.transform.LookAt(target);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.065f, .085f, .12f);
                camera.targetTexture = rt;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 30;
                var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
                var baked = new List<Mesh>();
                var bakedObjects = new List<GameObject>();
                foreach (var skin in skins)
                {
                    var meshObject = new GameObject("Preview baked skin");
                    meshObject.transform.SetParent(skin.transform, false);
                    var mesh = new Mesh();
                    meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    meshObject.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                    baked.Add(mesh);
                    bakedObjects.Add(meshObject);
                    skin.enabled = false;
                }
                var tile = new Texture2D(width, height, TextureFormat.RGB24, false);
                try
                {
                    float[] phases = { .18f, .45f, .65f };
                    for (int i = 0; i < 4; i++)
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "VoltSlash0" + (i + 1) + ".anim");
                        for (int row = 0; row < 3; row++)
                        {
                            clip.SampleAnimation(rig, clip.length * phases[row]);
                            for (int s = 0; s < skins.Length; s++) skins[s].BakeMesh(baked[s], true);
                            camera.Render();
                            RenderTexture.active = rt;
                            tile.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                            tile.Apply();
                            atlas.SetPixels(i * width, (2-row) * height, width, height, tile.GetPixels());
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tile);
                    foreach (var mesh in baked) UnityEngine.Object.DestroyImmediate(mesh);
                }
                atlas.Apply();
                string path = "Assets/3DBattle/Authoring~/ComboPreview.png";
                File.WriteAllBytes(path, atlas.EncodeToPNG());
                return path;
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(rig);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(atlas);
            }
        }
    }
}
