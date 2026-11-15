using UnityEditor;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(B2PhaseSmokeAttack))]
    public sealed class B2PhaseSmokeAttackEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var phase = (B2PhaseSmokeAttack)target;
            var combat = phase.combat != null ? phase.combat : phase.GetComponent<B2CombatController>();
            EditorGUILayout.LabelField("페이즈 2 테스트", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(combat == null || !combat.CanForcePhaseTwo))
            {
                if (GUILayout.Button(new GUIContent("페이즈 2 바로 시작", "체력을 변경하지 않고 현재 행동을 중단한 뒤, 쓰러졌다 일어나는 동작부터 시작합니다."), GUILayout.Height(30)))
                    combat.ForcePhaseTwo();
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Play 모드에서 B2 전투를 선택한 후 사용할 수 있습니다.", MessageType.Info);
            else
            {
                EditorGUILayout.LabelField("현재 상태", combat != null ? combat.State + " / " + phase.CurrentPhase : "B2 Combat Controller 연결 필요");
                EditorGUILayout.LabelField("참격 발사", $"{phase.ShotsEmitted} / {phase.shotCount}");
                EditorGUILayout.HelpBox("인트로와 페이즈2 진행 중에는 버튼이 비활성화됩니다. 종료 후 다시 테스트할 수 있습니다.", MessageType.Info);
                Repaint();
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
