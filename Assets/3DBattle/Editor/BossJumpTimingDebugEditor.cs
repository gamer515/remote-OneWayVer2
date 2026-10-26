using UnityEditor;
using UnityEngine;

namespace OneWay.Battle3D.Editor
{
    [CustomEditor(typeof(BossJumpTimingDebug), true)]
    public sealed class BossJumpTimingDebugEditor : UnityEditor.Editor
    {
        bool ownsPreview, playing;
        float time;
        double lastTick;
        BossJumpTimingDebug data;
        GameObject sampledObject;
        Vector3 initialPosition, initialScale;
        Quaternion initialRotation;

        void OnEnable()
        {
            data = (BossJumpTimingDebug)target;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }
        void OnDisable()
        {
            Stop();
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
        }
        void PlayModeChanged(PlayModeStateChange state) => Stop();

        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(ownsPreview))
            {
                serializedObject.Update();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("animator"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("clip"));
                serializedObject.ApplyModifiedProperties();
            }
            serializedObject.Update();
            foreach (var name in new[] { "previewSpeed", "takeoffTime", "landingTime", "leftFoot", "rightFoot" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox("Play를 누르지 않고 Scene 뷰에서 확인합니다. 노란 구는 발 위치입니다. 마지막 발이 바닥을 떠나는 시점과 첫 발이 다시 닿는 시점을 표시하세요. 보스 루트의 위치는 고정하며 뼈 애니메이션만 미리 봅니다.", MessageType.Info);
            if (data.clip == null || data.animator == null) return;
            EditorGUILayout.LabelField("클립 길이 / FPS", $"{data.clip.length:F3} s / {data.clip.frameRate:F0}");
            using (new EditorGUI.DisabledScope(Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                float next = EditorGUILayout.Slider("현재 시간 (초)", time, 0f, data.clip.length);
                if (EditorGUI.EndChangeCheck()) { playing = false; Seek(next); }
                EditorGUILayout.LabelField("현재 프레임", Mathf.RoundToInt(time * data.clip.frameRate).ToString());
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(playing ? "일시정지" : "재생"))
                {
                    if (playing) playing = false;
                    else if (Begin()) { if (time >= data.clip.length) time = 0f; playing = true; lastTick = EditorApplication.timeSinceStartup; Sample(); }
                }
                if (GUILayout.Button("처음")) { playing = false; Seek(0f); }
                if (GUILayout.Button("종료 / 원래 자세")) Stop();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("이전 프레임")) { playing = false; Seek(time - 1f / Mathf.Max(1f, data.clip.frameRate)); }
                if (GUILayout.Button("다음 프레임")) { playing = false; Seek(time + 1f / Mathf.Max(1f, data.clip.frameRate)); }
                EditorGUILayout.EndHorizontal();
                using (new EditorGUI.DisabledScope(!ownsPreview))
                {
                    if (GUILayout.Button("현재 시간을 이륙 시점으로 표시")) Mark(true);
                    if (GUILayout.Button("현재 시간을 착지 시점으로 표시")) Mark(false);
                }
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(data.takeoffTime < 0f))
                    if (GUILayout.Button("이륙 시점 보기")) { playing = false; Seek(data.takeoffTime); }
                using (new EditorGUI.DisabledScope(data.landingTime < 0f))
                    if (GUILayout.Button("착지 시점 보기")) { playing = false; Seek(data.landingTime); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.HelpBox(data.HasValidInterval
                ? $"공중 구간: {data.takeoffTime:F3} ~ {data.landingTime:F3}초 / 길이 {data.landingTime - data.takeoffTime:F3}초 (원본 1배속 기준)"
                : "이륙·착지 미지정 또는 구간 오류: 0 ≤ 이륙 < 착지 ≤ 클립 길이로 지정하세요.", data.HasValidInterval ? MessageType.Info : MessageType.Warning);
        }
        bool Begin()
        {
            if (ownsPreview) return true;
            if (Application.isPlaying || data == null || data.animator == null || data.clip == null) return false;
            if (AnimationMode.InAnimationMode())
            {
                Debug.LogWarning("다른 Animation 미리보기를 먼저 종료하세요.", data);
                return false;
            }
            sampledObject = data.animator.gameObject;
            var t = sampledObject.transform;
            initialPosition = t.localPosition; initialRotation = t.localRotation; initialScale = t.localScale;
            AnimationMode.StartAnimationMode();
            ownsPreview = true;
            return true;
        }
        void Seek(float value)
        {
            if (!Begin()) return;
            time = Mathf.Clamp(value, 0f, data.clip.length);
            Sample();
        }
        void Sample()
        {
            if (!ownsPreview || sampledObject == null || data == null || data.clip == null) { Stop(); return; }
            AnimationMode.BeginSampling();
            try { AnimationMode.SampleAnimationClip(sampledObject, data.clip, time); }
            finally { AnimationMode.EndSampling(); }
            var t = sampledObject.transform;
            t.localPosition = initialPosition; t.localRotation = initialRotation; t.localScale = initialScale;
            SceneView.RepaintAll(); Repaint();
        }
        void Tick()
        {
            if (!ownsPreview || !playing) return;
            double now = EditorApplication.timeSinceStartup;
            time = Mathf.Min(data.clip.length, time + (float)(now - lastTick) * Mathf.Max(.05f, data.previewSpeed));
            lastTick = now;
            if (time >= data.clip.length) playing = false;
            Sample();
        }
        void Mark(bool takeoff)
        {
            Undo.RecordObject(data, "Mark jump timing");
            if (takeoff) data.takeoffTime = time; else data.landingTime = time;
            EditorUtility.SetDirty(data);
            PrefabUtility.RecordPrefabInstancePropertyModifications(data);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(data.gameObject.scene);
        }
        void Stop()
        {
            playing = false;
            if (ownsPreview) AnimationMode.StopAnimationMode();
            ownsPreview = false;
            sampledObject = null;
            SceneView.RepaintAll();
        }
    }
}
