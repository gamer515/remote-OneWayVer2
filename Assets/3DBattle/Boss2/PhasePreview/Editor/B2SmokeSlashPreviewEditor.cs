using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(B2SmokeSlashPreview))]
    public sealed class B2SmokeSlashPreviewEditor : UnityEditor.Editor
    {
        bool playing;
        float cursor;
        double previousTime;
        B2SmokeSlashPreview Preview => target as B2SmokeSlashPreview;
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
        void ResetPreview()
        {
            playing = false; cursor = 0;
            if (Preview && !Application.isPlaying) Preview.StopPreview();
        }
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
            EditorGUILayout.HelpBox("보스 없이 연기 속에서 참격 한 발이 날아오는 미리보기입니다. Play 모드 없이 재생할 수 있습니다.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorUtility.IsPersistent(target)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("미리보기 재생 / 다시 재생", GUILayout.Height(28)))
                    {
                        if (Application.isPlaying) Preview.PlayPreview();
                        else { playing = true; cursor = 0; previousTime = EditorApplication.timeSinceStartup; Preview.SamplePreview(0); }
                    }
                    if (GUILayout.Button("정지 / 초기화", GUILayout.Height(28)))
                    { ResetPreview(); if (Application.isPlaying) Preview.StopPreview(); SceneView.RepaintAll(); }
                }
                if (!Application.isPlaying)
                {
                    EditorGUI.BeginChangeCheck();
                    float value = EditorGUILayout.Slider("미리보기 시간 (초)", cursor, 0, Preview.Duration);
                    if (EditorGUI.EndChangeCheck()) { playing = false; cursor = value; Preview.SamplePreview(value); SceneView.RepaintAll(); }
                }
                if (GUILayout.Button("발사 지점 보기") && SceneView.lastActiveSceneView)
                    SceneView.lastActiveSceneView.LookAt(Preview.transform.TransformPoint(new Vector3(0, 4, 10)), Preview.transform.rotation * Quaternion.LookRotation(new Vector3(-33, -8, -10)), 23);
            }
            EditorGUILayout.Space(); DrawDefaultInspector();
        }
    }
}
