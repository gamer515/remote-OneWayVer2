using UnityEditor;
using UnityEngine;
namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(BattleIntroWalkDebug))]
    public sealed class BattleIntroWalkDebugEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var debug = (BattleIntroWalkDebug)target;
            using (new EditorGUI.DisabledScope(debug.IsRunning)) DrawDefaultInspector();
            EditorGUILayout.HelpBox("거리 확인용입니다. 발 착지 횟수와 애니메이션은 아직 동기화하지 않습니다. Scene 뷰의 청록색 구슬이 발자국 간격입니다.", MessageType.Info);
            EditorGUILayout.LabelField("목표 거리", debug.TotalDistance.ToString("F2") + " units");
            EditorGUILayout.LabelField("예상 시간", (debug.TotalDistance / Mathf.Max(.01f, debug.moveSpeed)).ToString("F2") + " s");
            EditorGUILayout.LabelField("실제 전진 거리", debug.Travelled.ToString("F3"));
            EditorGUILayout.LabelField("상태", debug.Status);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                using (new EditorGUI.DisabledScope(debug.IsRunning))
                    if (GUILayout.Button("시작 위치에서 전진 테스트")) debug.Begin();
                using (new EditorGUI.DisabledScope(!debug.IsRunning))
                    if (GUILayout.Button("중단")) debug.Stop();
                if (GUILayout.Button("시작 위치로 복귀")) debug.ReturnToStart();
            }
            if (Application.isPlaying) Repaint();
        }
    }
}
