using UnityEditor;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(BossApproachController))]
    public sealed class BossApproachControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var approach = (BossApproachController)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("상태", approach.Status);
            EditorGUILayout.LabelField("수평 거리", approach.Distance.ToString("F2"));
            EditorGUILayout.LabelField("걷기 유지 시간", approach.WalkElapsed.ToString("F2"));
            EditorGUILayout.HelpBox("기본 접근: 달리기. 20 이상: 점프. 점프 전체 모션 종료 후 5 이상: 무장 걷기. 걷는 동안 5 이상을 5초 유지하면 달리기. Automatic Approach를 끄면 수동 디버깅할 수 있습니다.", MessageType.Info);
            if (UnityEngine.Application.isPlaying) Repaint();
        }
    }
}
