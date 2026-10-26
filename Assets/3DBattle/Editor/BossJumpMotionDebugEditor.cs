using UnityEditor;
using UnityEngine;
namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(BossJumpMotionDebug))]
    public sealed class BossJumpMotionDebugEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var d=(BossJumpMotionDebug)target;
            using(new EditorGUI.DisabledScope(d.IsRunning)) DrawDefaultInspector();
            EditorGUILayout.HelpBox("Play 모드에서 실행합니다. 12번은 Jump Timing의 이륙/착지 값을 사용합니다. Additional Height는 애니메이션 자체 높이에 추가되는 높이입니다. 이륙 순간 보스→플레이어 직선 위에 Landing Gap을 남기는 착지점을 확정합니다.",MessageType.Info);
            EditorGUILayout.LabelField("상태",d.Status);
            EditorGUILayout.LabelField("클립 시간",d.ClipTime.ToString("F3"));
            var p=d.GetComponent<BossActionPriority>();
            if(p!=null) EditorGUILayout.LabelField("우선순위 상태",$"공중 {p.Airborne} / 다운 {p.Down} / 피격 {p.HitReacting} / 대기 피격 {p.PendingHit}");
            using(new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                using(new EditorGUI.DisabledScope(d.IsRunning))
                    if(GUILayout.Button("점프 이동 테스트")) d.Begin();
                if(GUILayout.Button("중단 요청 (공중이면 착지 후)")) d.RequestStop();
                using(new EditorGUI.DisabledScope(p!=null && p.Airborne))
                    if(GUILayout.Button("보스 시작 위치 / 체력 초기화")) d.ResetToSpawn();
                var boss=d.GetComponent<BattleBoss>();
                if(GUILayout.Button("피격 기준 통과 테스트 (-최대 체력 34%)")) boss.TakeDamage(boss.maxHealth*.34f);
                if(GUILayout.Button("쓰러지기 테스트 (체력 0)")) boss.TakeDamage(boss.maxHealth);
            }
            if(Application.isPlaying) Repaint();
        }
    }
}
