using UnityEditor;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(NewPlayerAttackCombo))]
    public sealed class NewPlayerAttackComboEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var combo = (NewPlayerAttackCombo)target;
            EditorGUILayout.HelpBox("Left click: attack / queue. Right click: roll, then run. Wheel down: shield mode; left click: parry. Wheel up: attack mode.", MessageType.Info);
            if (!Application.isPlaying) return;
            EditorGUILayout.LabelField("Combo step", (combo.SequenceIndex + 1).ToString());
            EditorGUILayout.LabelField("Motion", (combo.AttackIndex + 1).ToString());
            EditorGUILayout.LabelField("Frame / queued", $"{combo.Frame:F1} / {combo.Queued}");
            if (GUILayout.Button("Attack / Queue next")) combo.RequestAttack();
            if (GUILayout.Button("Cancel and return to idle")) combo.Cancel();
            Repaint();
        }
    }
}
