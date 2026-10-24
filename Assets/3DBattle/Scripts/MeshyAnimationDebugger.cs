using System;
using UnityEngine;

namespace OneWay.Battle3D
{
    // Debug playback only. Disabling the component restores the original pose.
    [DisallowMultipleComponent]
    public sealed class MeshyAnimationDebugger : MonoBehaviour
    {
        public AnimationClip[] clips = Array.Empty<AnimationClip>();
        public bool playOnEnable = true;
        public bool autoAdvance = true;
        public bool loopPlaylist = true;
        [Min(.05f)] public float playbackSpeed = 1f;
        [Min(0f)] public float endHoldSeconds = .35f;
        public bool logClipChanges = true;
        public int CurrentIndex { get; private set; } = -1;
        public float CurrentTime { get; private set; }
        public bool IsPlaying { get; private set; }
        public AnimationClip CurrentClip => CurrentIndex >= 0 && CurrentIndex < clips.Length ? clips[CurrentIndex] : null;

        Transform[] parts;
        Vector3[] positions, scales;
        Quaternion[] rotations;
        Animator animator;
        bool animatorWasEnabled;
        float holdElapsed;

        void OnEnable()
        {
            parts = GetComponentsInChildren<Transform>(true);
            positions = new Vector3[parts.Length];
            rotations = new Quaternion[parts.Length];
            scales = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                positions[i] = parts[i].localPosition;
                rotations[i] = parts[i].localRotation;
                scales[i] = parts[i].localScale;
            }
            animator = GetComponent<Animator>();
            if (animator != null) { animatorWasEnabled = animator.enabled; animator.enabled = false; }
            if (playOnEnable) PlayClip(0);
        }

        void RestorePose()
        {
            if (parts == null) return;
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null)
                {
                    parts[i].localPosition = positions[i];
                    parts[i].localRotation = rotations[i];
                    parts[i].localScale = scales[i];
                }
        }

        void OnDisable()
        {
            IsPlaying = false;
            RestorePose();
            if (animator != null) animator.enabled = animatorWasEnabled;
        }

        public void PlayClip(int index)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || clips == null || clips.Length == 0) return;
            index = (index % clips.Length + clips.Length) % clips.Length;
            for (int count = 0; count < clips.Length && clips[index] == null; count++) index = (index + 1) % clips.Length;
            if (clips[index] == null) return;
            RestorePose();
            CurrentIndex = index;
            CurrentTime = holdElapsed = 0f;
            IsPlaying = true;
            Sample();
            if (logClipChanges) Debug.Log($"[Meshy Animation] {index + 1}/{clips.Length}: {CurrentClip.name} ({CurrentClip.length:F2}s)", this);
        }

        public void Next() => PlayClip(CurrentIndex + 1);
        public void Previous() => PlayClip(CurrentIndex - 1);
        public void Restart() => PlayClip(Mathf.Max(0, CurrentIndex));
        public void TogglePause() { if (CurrentClip != null) IsPlaying = !IsPlaying; else PlayClip(0); }

        public void Seek(float seconds)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || CurrentClip == null) return;
            IsPlaying = false;
            CurrentTime = Mathf.Clamp(seconds, 0f, CurrentClip.length);
            holdElapsed = 0f;
            Sample();
        }

        void Sample()
        {
            // Keep the scene placement while showing the FBX's actual bone animation.
            Vector3 position = transform.localPosition, scale = transform.localScale;
            Quaternion rotation = transform.localRotation;
            CurrentClip.SampleAnimation(gameObject, CurrentTime);
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
        }

        void LateUpdate()
        {
            if (!IsPlaying || CurrentClip == null) return;
            if (CurrentTime < CurrentClip.length)
            {
                CurrentTime = Mathf.Min(CurrentClip.length, CurrentTime + Time.unscaledDeltaTime * Mathf.Max(.05f, playbackSpeed));
                Sample();
                return;
            }
            holdElapsed += Time.unscaledDeltaTime;
            if (holdElapsed < Mathf.Max(0f, endHoldSeconds)) return;
            if (!autoAdvance || (!loopPlaylist && CurrentIndex == clips.Length - 1)) { IsPlaying = false; return; }
            Next();
        }
    }
}
