using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(B2SmokeRunOutPreview))]
    public sealed class B2SmokeRunOutPreviewEditor : UnityEditor.Editor
    {
        bool playing;
        float cursor;
        double previousTime;
        B2SmokeRunOutPreview Preview => target as B2SmokeRunOutPreview;
        void OnEnable()
        {
            EditorApplication.update += Tick;
            EditorSceneManager.sceneSaving += OnSaving;
            AssemblyReloadEvents.beforeAssemblyReload += ResetPreview;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }
        void OnDisable()
        {
            ResetPreview();
            EditorApplication.update -= Tick;
            EditorSceneManager.sceneSaving -= OnSaving;
            AssemblyReloadEvents.beforeAssemblyReload -= ResetPreview;
            EditorApplication.playModeStateChanged -= OnPlayMode;
        }
        void OnSaving(UnityEngine.SceneManagement.Scene scene, string path) { ResetPreview(); }
        void OnPlayMode(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingEditMode) ResetPreview(); }
        void ResetPreview() { playing = false; cursor = 0; if (Preview && !Application.isPlaying) Preview.StopPreview(); }
        void Tick()
        {
            if (!playing || !Preview || Application.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            cursor += (float)(now - previousTime) * Preview.playbackSpeed; previousTime = now;
            if (Preview.loop) cursor = Mathf.Repeat(cursor, Preview.Duration);
            else if (cursor >= Preview.Duration) { cursor = Preview.Duration; playing = false; }
            Preview.SamplePreview(cursor); SceneView.RepaintAll(); Repaint();
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("연기가 퍼진 뒤 보스가 달려 나오는 별도 미리보기입니다. Play 모드 없이 재생할 수 있습니다.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorUtility.IsPersistent(target)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("미리보기 재생 / 다시 재생", GUILayout.Height(28)))
                    {
                        if (Application.isPlaying) Preview.PlayPreview();
                        else { playing = true; cursor = 0; previousTime = EditorApplication.timeSinceStartup; Preview.SamplePreview(0); }
                    }
                    if (GUILayout.Button("정지 / 원위치", GUILayout.Height(28)))
                    { ResetPreview(); if (Application.isPlaying) Preview.StopPreview(); SceneView.RepaintAll(); }
                }
                if (!Application.isPlaying)
                {
                    EditorGUI.BeginChangeCheck();
                    float time = EditorGUILayout.Slider("미리보기 시간 (초)", cursor, 0, Preview.Duration);
                    if (EditorGUI.EndChangeCheck()) { playing = false; cursor = time; Preview.SamplePreview(time); SceneView.RepaintAll(); }
                }
                if (GUILayout.Button("등장 장면 보기") && SceneView.lastActiveSceneView)
                    SceneView.lastActiveSceneView.LookAt(Preview.transform.TransformPoint(new Vector3(0, 3.5f, 5)), Preview.transform.rotation * Quaternion.LookRotation(new Vector3(-18, -5, -22)), 17);
            }
            EditorGUILayout.Space(); DrawDefaultInspector();
        }
    }
}
