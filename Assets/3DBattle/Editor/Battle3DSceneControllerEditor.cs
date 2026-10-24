using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(Battle3DSceneController))]
    public sealed class Battle3DSceneControllerEditor : UnityEditor.Editor
    {
        int patternIndex;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var controller = (Battle3DSceneController)target;
            var stages = controller.stages.Where(s => s != null && s.settings != null).ToArray();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Battle Preview / Debug", EditorStyles.boldLabel);
            if (stages.Length == 0)
            {
                EditorGUILayout.HelpBox("Assign stage objects to Stages first.", MessageType.Info);
                return;
            }
            int current = System.Array.FindIndex(stages, s => s.StageIndex == controller.SelectedStageIndex);
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Battle", current,
                stages.Select(s => $"{s.DisplayName} (ID {s.StageIndex})").ToArray());
            if (EditorGUI.EndChangeCheck() && selected >= 0)
            {
                SelectStage(controller, stages[selected].StageIndex);
                patternIndex = 0;
            }
            if (GUILayout.Button(Application.isPlaying ? "Restart Current Battle" : "Apply Scene Preview"))
                SelectStage(controller, controller.SelectedStageIndex);

            var stage = controller.CurrentStage;
            if (stage == null) return;
            EditorGUILayout.ObjectField("Stage Settings", stage.settings, typeof(BattleStageSettings), false);
            var patterns = stage.settings.patterns;
            if (patterns == null || patterns.Length == 0)
            {
                EditorGUILayout.HelpBox("Add pattern assets to Stage Settings > Patterns.", MessageType.Info);
                return;
            }
            patternIndex = Mathf.Clamp(patternIndex, 0, patterns.Length - 1);
            patternIndex = EditorGUILayout.Popup("Boss Attack", patternIndex,
                patterns.Select((p, i) => p != null ? $"{i + 1}. {p.displayName}" : "(Missing pattern)").ToArray());
            EditorGUILayout.ObjectField("Pattern Settings", patterns[patternIndex], typeof(BossAttackPattern), false);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || patterns[patternIndex] == null))
            {
                if (GUILayout.Button("Run Selected Attack"))
                    if (!controller.RunPattern(patternIndex)) Debug.LogWarning("Attack could not start. Check boss/player health and stage references.", controller);
                if (GUILayout.Button("Stop Attack")) controller.StopPattern();
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Battle selection previews the Scene without Play. Attacks run in Play mode. Play-mode changes are temporary.", MessageType.Info);
            else
            {
                var runner = stage.patternRunner;
                EditorGUILayout.LabelField("Running", runner != null && runner.IsRunning ? runner.CurrentPattern.displayName : "None");
                var health = controller.player.GetComponent<BattlePlayerHealth>();
                if (health != null) EditorGUILayout.LabelField("Player HP", $"{health.Health:0} / {health.maxHealth:0}");
                Repaint();
            }
        }

        static void SelectStage(Battle3DSceneController controller, int index)
        {
            if (!Application.isPlaying)
            {
                Undo.RegisterFullObjectHierarchyUndo(controller.gameObject, "Preview battle stage");
                if (controller.followCamera != null) Undo.RecordObject(controller.followCamera.transform, "Preview battle camera");
            }
            if (!controller.SelectStage(index))
                Debug.LogError("Invalid stage: check unique IDs, boss, runner and spawn references.", controller);
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
                SceneView.RepaintAll();
            }
        }
    }
}
