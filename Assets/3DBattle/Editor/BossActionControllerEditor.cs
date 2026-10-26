using UnityEditor;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(BossActionController))]
    public sealed class BossActionControllerEditor : UnityEditor.Editor
    {
        BossAttackPattern testAttack;
        public override void OnInspectorGUI()
        {
            var controller = (BossActionController)target;
            EditorGUILayout.HelpBox("행동 시작·완료·중단의 공통 제어기입니다. 공중에서는 착지까지 중단을 보류합니다.", MessageType.Info);
            EditorGUILayout.LabelField("현재 행동", controller.Current.ToString());
            EditorGUILayout.LabelField("공중 / 중단 예약", controller.Airborne + " / " + controller.StopQueued);
            EditorGUILayout.LabelField("최근 결과", controller.LastResult);
            testAttack = (BossAttackPattern)EditorGUILayout.ObjectField("테스트 공격", testAttack, typeof(BossAttackPattern), false);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("12번 점프 시작")) controller.RequestJump(false);
                if (GUILayout.Button("17번 도약 시작")) controller.RequestJump(true);
                using (new EditorGUI.DisabledScope(testAttack == null))
                    if (GUILayout.Button("선택한 공격 시작")) controller.RequestAttack(testAttack);
                if (GUILayout.Button("행동 중단 요청")) controller.RequestStop();
            }
            if (Application.isPlaying) Repaint();
        }
    }
}
