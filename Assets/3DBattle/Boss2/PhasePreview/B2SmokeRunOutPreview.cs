using UnityEngine;

namespace OneWay.Battle3D
{
    // A visual preview only; never controls combat, player targeting or damage.
    public sealed class B2SmokeRunOutPreview : MonoBehaviour
    {
        public Transform movingRoot;
        public GameObject model;
        public AnimationClip runClip;
        public Renderer[] bodyRenderers;
        public B2SmokeSlashPreview cloud;
        [Header("Emergence")]
        [Tooltip("연기가 퍼진 뒤 보스가 달려 나오기 시작하는 시간(초)")]
        [Min(0)] public float runStart = 3.6f;
        [Min(0)] public float appearSeconds = .35f;
        public Vector3 startPosition = new Vector3(0, 0, -1.5f);
        [Min(.1f)] public float runSeconds = 3;
        [Min(0)] public float runSpeed = 6;
        [Min(.01f)] public float animationSpeed = 1;
        [Header("Preview")]
        public bool playOnStart = true;
        public bool loop = true;
        [Range(.1f, 2)] public float playbackSpeed = 1;

        float elapsed;
        bool playing;
        Material[][] originalMaterials;
        Material[][] fadeMaterials;
        public float Duration => Mathf.Max(0, runStart) + Mathf.Max(.1f, runSeconds);
        public float PreviewTime { get; private set; }

        void Start() { if (playOnStart) PlayPreview(); }
        void Update()
        {
            if (!playing) return;
            elapsed += Time.deltaTime * playbackSpeed;
            if (loop) elapsed = Mathf.Repeat(elapsed, Duration);
            else if (elapsed >= Duration) { elapsed = Duration; playing = false; }
            SamplePreview(elapsed);
        }
        public void PlayPreview() { playing = true; elapsed = 0; SamplePreview(0); }
        public void StopPreview() { playing = false; elapsed = 0; SamplePreview(0); ReleaseMaterials(); }
        void OnDisable() { StopPreview(); }
        void OnDestroy() { ReleaseMaterials(); }

        public void SamplePreview(float time)
        {
            PreviewTime = Mathf.Clamp(time, 0, Duration);
            if (cloud) cloud.SampleCloud(PreviewTime);
            if (!movingRoot || !model || !runClip) return;
            float runTime = Mathf.Clamp(PreviewTime - runStart, 0, runSeconds);
            movingRoot.localPosition = startPosition + Vector3.forward * (runTime * runSpeed);
            runClip.SampleAnimation(model, Mathf.Repeat(runTime * animationSpeed, Mathf.Max(.001f, runClip.length)));
            float alpha = PreviewTime <= runStart ? 0 : appearSeconds <= 0 ? 1
                : Mathf.SmoothStep(0, 1, runTime / appearSeconds);
            ApplyVisibility(alpha);
        }

        void ApplyVisibility(float alpha)
        {
            if (bodyRenderers == null) return;
            if (alpha > 0 && alpha < 1 && originalMaterials == null)
            {
                originalMaterials = new Material[bodyRenderers.Length][];
                fadeMaterials = new Material[bodyRenderers.Length][];
                for (int i = 0; i < bodyRenderers.Length; i++)
                {
                    if (!bodyRenderers[i]) continue;
                    originalMaterials[i] = bodyRenderers[i].sharedMaterials;
                    fadeMaterials[i] = new Material[originalMaterials[i].Length];
                    for (int m = 0; m < originalMaterials[i].Length; m++)
                    {
                        var source = originalMaterials[i][m]; if (!source) continue;
                        var material = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                        material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0);
                        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = 2990;
                        material.SetShaderPassEnabled("ShadowCaster", false);
                        fadeMaterials[i][m] = material;
                    }
                }
            }
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                var renderer = bodyRenderers[i]; if (!renderer) continue;
                renderer.enabled = alpha > 0;
                if (originalMaterials == null) continue;
                if (alpha <= 0 || alpha >= 1) { renderer.sharedMaterials = originalMaterials[i]; continue; }
                for (int m = 0; m < fadeMaterials[i].Length; m++)
                {
                    var material = fadeMaterials[i][m]; if (!material) continue;
                    foreach (var property in new[] { "_BaseColor", "_Color" })
                    {
                        if (!material.HasProperty(property)) continue;
                        var color = originalMaterials[i][m].GetColor(property); color.a *= alpha;
                        material.SetColor(property, color);
                    }
                }
                renderer.sharedMaterials = fadeMaterials[i];
            }
        }
        void ReleaseMaterials()
        {
            if (originalMaterials == null) return;
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                if (bodyRenderers[i]) bodyRenderers[i].sharedMaterials = originalMaterials[i];
                if (fadeMaterials[i] == null) continue;
                foreach (var material in fadeMaterials[i]) if (material)
                { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            }
            originalMaterials = null; fadeMaterials = null;
        }
    }
}
