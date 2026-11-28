using UnityEditor;
using UnityEngine;

namespace OneWay.Loading.Editor
{
    [CustomEditor(typeof(LoadingScreenController))]
    public sealed class LoadingScreenControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var screen = (LoadingScreenController)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("직접 실행: Preview Only를 끄면 Destination으로 실제 이동합니다. 켜면 촬영용 미리보기입니다.\nOpen()을 통한 요청은 항상 실제 로딩입니다.\nBattle은 마우스 조작 안내, Decision은 기존 LoadDecisionScene의 txt를 사용합니다. MainMenu는 문구 없이 표시합니다.", MessageType.Info);
            if (!Application.isPlaying && GUILayout.Button("선택한 목적지의 화면 확인"))
            {
                screen.RefreshPresentation();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            }
            if (!Application.CanStreamedLevelBeLoaded(LoadingScreenController.LoadingScenePath))
                EditorGUILayout.HelpBox("LoadingScene이 빌드 씬 목록에 없습니다. 에디터에서는 전환을 테스트할 수 있지만, 빌드 전에 Build Profiles > Scene List에 LoadingScene을 등록해야 합니다.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !screen.IsPreview))
                if (GUILayout.Button("미리보기 다시 재생 (문구 다시 선택)")) screen.ReplayPreview();
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("진행률", screen.DisplayedProgress.ToString("P0"));
                if (!string.IsNullOrEmpty(screen.LastError)) EditorGUILayout.HelpBox(screen.LastError, MessageType.Error);
                Repaint();
            }
        }
    }
}
