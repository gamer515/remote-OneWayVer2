using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(B2RetreatVanishPreview))]
    public sealed class B2RetreatVanishPreviewEditor : UnityEditor.Editor
    {
        bool playing;
        double startTime;
        float cursor;
        B2RetreatVanishPreview Preview => target as B2RetreatVanishPreview;

        void OnEnable()
        {
            EditorApplication.update += Tick;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload += ResetPose;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }
        void OnDisable()
        {
            ResetPose();
            EditorApplication.update -= Tick;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload -= ResetPose;
            EditorApplication.playModeStateChanged -= OnPlayMode;
        }
        void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) { ResetPose(); }
        void OnPlayMode(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) ResetPose(); }
        void ResetPose()
        {
            playing = false; cursor = 0;
            if (Preview && !Application.isPlaying) Preview.StopPreview();
        }
        void Tick()
        {
            if (!playing || !Preview || Application.isPlaying) return;
            float t = (float)(EditorApplication.timeSinceStartup - startTime);
            if (t > Preview.Duration && !Preview.loop) { t = Preview.Duration; playing = false; }
            cursor = Preview.loop ? Mathf.Repeat(t, Preview.Duration) : t;
            Preview.SamplePreview(cursor);
            SceneView.RepaintAll(); Repaint();
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("별도 미리보기입니다. 아래 재생 버튼으로 Play 모드에 들어가지 않고 Scene 창에서 확인할 수 있습니다.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorUtility.IsPersistent(target)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("미리보기 재생 / 다시 재생", GUILayout.Height(28)))
                    {
                        if (Application.isPlaying) Preview.PlayPreview();
                        else { playing = true; startTime = EditorApplication.timeSinceStartup; cursor = 0; Preview.SamplePreview(0); }
                    }
                    if (GUILayout.Button("정지 / 원위치", GUILayout.Height(28)))
                    { ResetPose(); if (Application.isPlaying) Preview.StopPreview(); SceneView.RepaintAll(); }
                }
                if (!Application.isPlaying)
                {
                    EditorGUI.BeginChangeCheck();
                    float t = EditorGUILayout.Slider("미리보기 시간 (초)", cursor, 0, Preview.Duration);
                    if (EditorGUI.EndChangeCheck()) { playing = false; cursor = t; Preview.SamplePreview(t); SceneView.RepaintAll(); }
                }
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
