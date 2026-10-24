using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(MeshyAnimationDebugger))]
    public sealed class MeshyAnimationDebuggerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var player = (MeshyAnimationDebugger)target;
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play Mode: automatically plays the clips in list order. Select this object to inspect, pause or scrub a clip.", MessageType.Info);
                if (GUILayout.Button("Reload clips from model FBX")) Populate(player);
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{player.CurrentIndex + 1} / {player.clips.Length}", player.CurrentClip == null ? "No clip" : player.CurrentClip.name);
            using (new EditorGUI.DisabledScope(!player.isActiveAndEnabled))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous")) player.Previous();
                if (GUILayout.Button(player.IsPlaying ? "Pause" : "Play")) player.TogglePause();
                if (GUILayout.Button("Restart")) player.Restart();
                if (GUILayout.Button("Next")) player.Next();
                EditorGUILayout.EndHorizontal();
                if (player.CurrentClip != null)
                {
                    EditorGUI.BeginChangeCheck();
                    float time = EditorGUILayout.Slider("Time (seconds)", player.CurrentTime, 0f, player.CurrentClip.length);
                    if (EditorGUI.EndChangeCheck()) player.Seek(time);
                }
            }
            Repaint();
        }

        public static void Populate(MeshyAnimationDebugger player)
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player.gameObject);
            if (string.IsNullOrEmpty(path)) throw new System.InvalidOperationException("Select the FBX model instance root.");
            Undo.RecordObject(player, "Load Meshy animation clips");
            player.clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name, System.StringComparer.Ordinal).ToArray();
            EditorUtility.SetDirty(player);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        }
    }
}
