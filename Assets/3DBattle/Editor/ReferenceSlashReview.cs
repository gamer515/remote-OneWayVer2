using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    public static class ReferenceSlashReview
    {
        const string Output = "Assets/3DBattle/Authoring~/";
        public static string Render()
        {
            GameObject rig = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3DBattle/Prefabs/BattleKnight.prefab"));
            var cameraObject = new GameObject("Reference review camera");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rig.hideFlags = cameraObject.hideFlags = floor.hideFlags = HideFlags.HideAndDontSave;
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.color = new Color(.14f, .18f, .23f);
            var rt = new RenderTexture(400, 400, 24);
            var tile = new Texture2D(400, 400, TextureFormat.RGB24, false);
            var atlas = new Texture2D(2000, 800, TextureFormat.RGB24, false);
            var skins = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
            var meshes = skins.Select(_ => new Mesh()).ToArray();
            RenderTexture old = RenderTexture.active;
            try
            {
                Vector3 origin = new Vector3(1000, 0, 1000);
                rig.transform.SetPositionAndRotation(origin, Quaternion.identity);
                floor.transform.position = origin + new Vector3(0, -.09f, 0);
                floor.transform.localScale = new Vector3(9, .1f, 9);
                floor.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                for (int i = 0; i < skins.Length; i++)
                {
                    var obj = new GameObject("Baked review mesh");
                    obj.transform.SetParent(skins[i].transform, false);
                    obj.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                    obj.AddComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials;
                    skins[i].enabled = false;
                }
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 1.65f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.045f, .065f, .09f);
                camera.targetTexture = rt;
                camera.nearClipPlane = .1f; camera.farClipPlane = 30;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/3DBattle/Animations/VoltSlash01.anim");
                void Frame(float normalized)
                {
                    float u = Mathf.InverseLerp(.15f, .62f, normalized);
                    rig.transform.position = origin + Vector3.forward * (.55f * u * u * (3-2*u));
                    clip.SampleAnimation(rig, normalized * clip.length);
                    for (int i = 0; i < skins.Length; i++) skins[i].BakeMesh(meshes[i], true);
                    camera.Render(); RenderTexture.active = rt;
                    tile.ReadPixels(new Rect(0, 0, 400, 400), 0, 0); tile.Apply();
                }
                int[] frames = { 0, 14, 24, 32, 41 };
                for (int row = 0; row < 2; row++)
                {
                    Vector3 target = origin + new Vector3(0, 1.05f, .2f);
                    camera.transform.position = target + (row == 0 ? new Vector3(0, 1.2f, 7) : new Vector3(7, 1.2f, .2f));
                    camera.transform.LookAt(target);
                    for (int col = 0; col < frames.Length; col++)
                    {
                        Frame(frames[col] / 54f);
                        atlas.SetPixels(col * 400, (1-row)*400, 400, 400, tile.GetPixels());
                    }
                }
                atlas.Apply(); File.WriteAllBytes(Output + "ReferenceSlashPoses.png", atlas.EncodeToPNG());
                Directory.CreateDirectory(Output + "ReferenceFrames~");
                Vector3 focus = origin + new Vector3(0, 1.05f, .2f);
                camera.transform.position = focus + new Vector3(2.5f, 1.5f, 7);
                camera.transform.LookAt(focus);
                for (int frame = 0; frame <= 54; frame++)
                {
                    Frame(frame / 54f);
                    File.WriteAllBytes(Output + "ReferenceFrames~/" + frame.ToString("D3") + ".png", tile.EncodeToPNG());
                }
                return "Rendered five reference poses from front and side, plus 55 animation frames.";
            }
            finally
            {
                RenderTexture.active = old;
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(rig); UnityEngine.Object.DestroyImmediate(floor);
                UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(groundMaterial);
                UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tile); UnityEngine.Object.DestroyImmediate(atlas);
            }
        }

        public static string ValidateStep()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            var source = UnityEngine.Object.FindFirstObjectByType<BattlePlayerController>();
            var fixture = new GameObject("Reference step validation");
            try
            {
                var player = UnityEngine.Object.Instantiate(source, fixture.transform);
                player.enabled = false; player.feedback = null;
                var boss = UnityEngine.Object.Instantiate(source.boss, fixture.transform);
                boss.enabled = false; boss.patrolEnabled = false; player.boss = boss;
                var motor = player.GetComponent<CharacterController>();
                Vector3 start = new Vector3(1000,0,1000);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.transform.SetParent(fixture.transform);
                ground.transform.position = start + new Vector3(0,-.5f,0);
                ground.transform.localScale = new Vector3(10,1,10);
                void Reset()
                {
                    player.CancelInput(); motor.enabled = false;
                    player.transform.SetPositionAndRotation(start, Quaternion.identity); motor.enabled = true;
                    boss.transform.position = start + Vector3.forward * 4;
                    boss.ResetBoss(); boss.transform.position = start + Vector3.forward * 4;
                    Physics.SyncTransforms();
                    player.ProcessPointerFrame(start, true, true, false, false, false, 2);
                    player.ProcessPointerFrame(start, true, true, false, true, false, 0);
                    player.ProcessPointerFrame(start, true, true, false, false, true, .05f);
                }
                Reset();
                for (int f=0; f<60; f++) player.Move(1f/60);
                float travel = player.transform.position.z - start.z;
                if (Mathf.Abs(travel-player.firstSlashStepDistance)>.015f) throw new Exception("Wrong step distance " + travel);
                Reset(); player.Move(.1f);
                if (Mathf.Abs(player.transform.position.z-start.z)>.01f) throw new Exception("Moved before anticipation ended");
                player.CancelInput(); player.Move(1);
                if (Mathf.Abs(player.transform.position.z-start.z)>.01f) throw new Exception("Cancelled step continued");
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(fixture.transform);
                wall.transform.position = start + new Vector3(0,1,.8f);
                wall.transform.localScale = new Vector3(4,3,.2f);
                Reset();
                for (int f=0; f<60; f++) player.Move(1f/60);
                if (player.transform.position.z-start.z > .3f) throw new Exception("Step crossed wall");
                return "PASS: configured forward step; anticipation stays planted; cancellation stops advance; wall blocks step.";
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }
    }
}
