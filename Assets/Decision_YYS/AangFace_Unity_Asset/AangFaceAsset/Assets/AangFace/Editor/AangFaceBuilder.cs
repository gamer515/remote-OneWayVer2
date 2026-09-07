using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AangFaceAsset.Editor
{
    /// <summary>Creates a native Mesh asset and a reusable Prefab without a third-party importer.</summary>
    public static class AangFaceBuilder
    {
        [Serializable] private sealed class ModelData
        {
            public string name;
            public float[] positions;
            public float[] normals;
            public float[] colors;
            public MaterialData[] materials;
            public SubmeshData[] submeshes;
            public ShapeData[] blendShapes;
        }
        [Serializable] private sealed class MaterialData { public string name; public float[] color; public float roughness; }
        [Serializable] private sealed class SubmeshData { public int[] triangles; }
        [Serializable] private sealed class ShapeData
        {
            public string name;
            public int[] indices;
            public float[] delta;
            public float[] normalDelta;
        }

        [MenuItem("Tools/Aang Face/Create Prefab")]
        public static void CreatePrefab()
        {
            string source = FindSource();
            if (source == null)
            {
                Debug.LogError("AangFaceMesh.json was not found. Import the complete AangFace package.");
                return;
            }
            GameObject root = null;
            try
            {
                ModelData data = JsonUtility.FromJson<ModelData>(File.ReadAllText(source));
                Validate(data);
                string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/AangFaceGenerated");
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
                var mesh = new Mesh { name = "AangFace", indexFormat = IndexFormat.UInt32 };
                int count = data.positions.Length / 3;
                mesh.vertices = Vectors(data.positions);
                mesh.normals = Vectors(data.normals);
                var vertexColors = new Color[count];
                for (int i = 0; i < count; i++)
                    vertexColors[i] = new Color(data.colors[i * 3], data.colors[i * 3 + 1], data.colors[i * 3 + 2], 1);
                mesh.colors = vertexColors;
                mesh.subMeshCount = data.submeshes.Length;
                for (int i = 0; i < data.submeshes.Length; i++)
                {
                    int[] triangles = (int[])data.submeshes[i].triangles.Clone();
                    // Source is right-handed. Reflect X and reverse winding for Unity.
                    for (int k = 0; k < triangles.Length; k += 3)
                    {
                        int temp = triangles[k + 1];
                        triangles[k + 1] = triangles[k + 2];
                        triangles[k + 2] = temp;
                    }
                    mesh.SetTriangles(triangles, i, false);
                }
                Bounds bounds = new Bounds(mesh.vertices[0], Vector3.zero);
                Vector3[] vertices = mesh.vertices;
                foreach (Vector3 p in vertices) bounds.Encapsulate(p);
                foreach (ShapeData shape in data.blendShapes)
                {
                    var dv = new Vector3[count];
                    var dn = new Vector3[count];
                    for (int i = 0; i < shape.indices.Length; i++)
                    {
                        int vertex = shape.indices[i];
                        dv[vertex] = Vector(shape.delta, i * 3);
                        dn[vertex] = Vector(shape.normalDelta, i * 3);
                        bounds.Encapsulate(vertices[vertex] + dv[vertex]);
                    }
                    mesh.AddBlendShapeFrame(shape.name, 100, dv, dn, null);
                }
                // One head joint makes attaching/rotating this model straightforward.
                var boneWeights = new BoneWeight[count];
                for (int i = 0; i < count; i++)
                    boneWeights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1 };
                mesh.boneWeights = boneWeights;
                mesh.bindposes = new[] { Matrix4x4.identity };
                bounds.Expand(0.06f);
                mesh.bounds = bounds;
                AssetDatabase.CreateAsset(mesh, folder + "/AangFaceMesh.asset");

                bool usesVertexColors;
                Shader shader = ChooseShader(source, folder, out usesVertexColors);
                var materials = new Material[data.materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    MaterialData input = data.materials[i];
                    var material = new Material(shader) { name = input.name };
                    Color color = usesVertexColors ? Color.white : new Color(input.color[0], input.color[1], input.color[2], input.color[3]);
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                    if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
                    if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1 - input.roughness);
                    if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 1 - input.roughness);
                    AssetDatabase.CreateAsset(material, folder + "/" + input.name + ".mat");
                    materials[i] = material;
                }

                root = new GameObject("AangFace");
                var headBone = new GameObject("HeadRoot").transform;
                headBone.SetParent(root.transform, false);
                var face = new GameObject("Face");
                face.transform.SetParent(root.transform, false);
                var renderer = face.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.sharedMaterials = materials;
                renderer.rootBone = headBone;
                renderer.bones = new[] { headBone };
                renderer.localBounds = bounds;
                renderer.updateWhenOffscreen = true;
                var controller = root.AddComponent<FaceExpressionController>();
                controller.faceRenderer = renderer;
                controller.Apply();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/AangFace.prefab");
                AssetDatabase.SaveAssets();
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log("AangFace v2 prefab created: " + folder + "/AangFace.prefab (10 blend shapes). Drag it into your scene.", prefab);
            }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { if (root) UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Shader ChooseShader(string modelSource, string outputFolder, out bool usesVertexColors)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            string name = pipeline ? pipeline.GetType().Name : "";
            usesVertexColors = false;
            if (name.IndexOf("HDRender", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.LogWarning("AangFace v2: HDRP uses flat-color Lit materials. To display iris/skin vertex-color detail, use a vertex-color HDRP Shader Graph.");
                Shader hdrp = Shader.Find("HDRP/Lit");
                if (!hdrp) throw new InvalidOperationException("HDRP/Lit shader unavailable.");
                return hdrp;
            }
            bool universal = name.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0;
            string packageRoot = Path.GetDirectoryName(Path.GetDirectoryName(modelSource));
            string sourcePath = Path.Combine(packageRoot, "Editor/ShaderSources/" + (universal ? "VertexLitURP.txt" : "VertexLitBuiltin.txt"));
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Import the complete package; the vertex-color shader source is missing.", sourcePath);
            string shaderPath = outputFolder + "/AangFaceVertexLit.shader";
            File.WriteAllText(shaderPath, File.ReadAllText(sourcePath));
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("The AangFace vertex-color shader did not compile. See the shader errors in the Console.");
            usesVertexColors = true;
            return shader;
        }

        private static string FindSource()
        {
            const string standard = "Assets/AangFace/Data/AangFaceMesh.json";
            if (File.Exists(standard)) return standard;
            foreach (string guid in AssetDatabase.FindAssets("AangFaceMesh t:TextAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == "AangFaceMesh.json") return path;
            }
            return null;
        }

        private static Vector3 Vector(float[] data, int offset)
        {
            return new Vector3(-data[offset], data[offset + 1], data[offset + 2]);
        }
        private static Vector3[] Vectors(float[] data)
        {
            var result = new Vector3[data.Length / 3];
            for (int i = 0; i < result.Length; i++) result[i] = Vector(data, i * 3);
            return result;
        }
        private static void Validate(ModelData data)
        {
            if (data == null || data.positions == null || data.positions.Length == 0 || data.positions.Length % 3 != 0)
                throw new InvalidDataException("Invalid vertex data.");
            if (data.normals == null || data.normals.Length != data.positions.Length)
                throw new InvalidDataException("Invalid normal data.");
            if (data.colors == null || data.colors.Length != data.positions.Length)
                throw new InvalidDataException("Expected v2 vertex colors. Import the v2 AangFaceMesh.json data file.");
            int count = data.positions.Length / 3;
            if (data.submeshes == null || data.materials == null || data.submeshes.Length != data.materials.Length)
                throw new InvalidDataException("Invalid materials/submeshes.");
            foreach (SubmeshData submesh in data.submeshes)
            {
                if (submesh.triangles == null || submesh.triangles.Length % 3 != 0)
                    throw new InvalidDataException("Invalid triangle data.");
                foreach (int index in submesh.triangles)
                    if (index < 0 || index >= count) throw new InvalidDataException("Triangle index out of range.");
            }
            if (data.blendShapes == null || data.blendShapes.Length != 10)
                throw new InvalidDataException("Expected ten blend shapes.");
            foreach (ShapeData shape in data.blendShapes)
            {
                if (shape.indices == null || shape.delta == null || shape.normalDelta == null ||
                    shape.delta.Length != shape.indices.Length * 3 || shape.normalDelta.Length != shape.delta.Length)
                    throw new InvalidDataException("Invalid blend shape: " + shape.name);
                foreach (int index in shape.indices)
                    if (index < 0 || index >= count) throw new InvalidDataException("Blend shape index out of range.");
            }
        }
    }

    [CustomEditor(typeof(FaceExpressionController))]
    public sealed class FaceExpressionControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var face = (FaceExpressionController)target;
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            Preset(face, "Neutral", FaceExpressionController.Expression.Neutral);
            Preset(face, "Smile", FaceExpressionController.Expression.Smile);
            Preset(face, "Angry", FaceExpressionController.Expression.Angry);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            Preset(face, "Sad", FaceExpressionController.Expression.Sad);
            Preset(face, "Surprised", FaceExpressionController.Expression.Surprised);
            if (GUILayout.Button("Blink"))
            {
                Undo.RecordObject(face, "Blink Aang face");
                face.blinkLeft = face.blinkRight = 100;
                face.Apply();
                EditorUtility.SetDirty(face);
            }
            EditorGUILayout.EndHorizontal();
            face.Apply();
        }
        private static void Preset(FaceExpressionController face, string text, FaceExpressionController.Expression expression)
        {
            if (!GUILayout.Button(text)) return;
            Undo.RecordObject(face, "Set Aang face expression");
            face.ResetExpression();
            face.SetExpression(expression);
            EditorUtility.SetDirty(face);
        }
    }
}
