using UnityEngine;

namespace OneWay.Battle3D
{
    // Isolated visual preview: no combat, target selection, hitboxes or phase triggers.
    public sealed class B2RetreatVanishPreview : MonoBehaviour
    {
        public Transform movingRoot;
        public GameObject model;
        public AnimationClip backwardClip;
        public Renderer[] bodyRenderers;
        public ParticleSystem[] smokeLayers;
        [Header("Timing (seconds)")]
        [Min(0)] public float standSeconds = .3f;
        [Min(.1f)] public float retreatSeconds = 1.333333f;
        [Min(1)] public int retreatCycles = 1;
        [Min(0)] public float retreatDistance = 3.5f;
        [Min(0)] public float smokeStart = .65f;
        [Min(0)] public float hideAt = 1.55f;
        [Min(0)] public float bodyFadeSeconds = .22f;
        [Min(.1f)] public float smokeEnd = 2.8f;
        [Min(0)] public float repeatPause = .85f;
        [Header("Preview")]
        public bool playOnStart = true;
        public bool loop = true;

        float elapsed;
        bool playing;
        Material[][] originalMaterials;
        Material[][] fadeMaterials;
        public float Duration => Mathf.Max(smokeEnd, hideAt, standSeconds + retreatSeconds) + repeatPause;
        public float PreviewTime { get; private set; }
        public bool IsPlaying => playing;

        void Start() { if (playOnStart) PlayPreview(); }
        void Update()
        {
            if (!playing) return;
            elapsed += Time.deltaTime;
            if (loop) elapsed = Mathf.Repeat(elapsed, Duration);
            else if (elapsed >= Duration) { elapsed = Duration; playing = false; }
            SamplePreview(elapsed);
        }

        public void PlayPreview() { elapsed = 0; playing = true; SamplePreview(0); }
        public void StopPreview() { playing = false; elapsed = 0; SamplePreview(0); }
        void OnDisable() { StopPreview(); ReleaseFadeMaterials(); }
        void OnDestroy() { ReleaseFadeMaterials(); }

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
                        var source = originalMaterials[i][m];
                        if (!source) continue;
                        var material = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                        material.SetFloat("_Surface", 1);
                        material.SetFloat("_ZWrite", 0);
                        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        material.SetOverrideTag("RenderType", "Transparent");
                        material.renderQueue = 2990;
                        material.SetShaderPassEnabled("ShadowCaster", false);
                        fadeMaterials[i][m] = material;
                    }
                }
            }
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                var r = bodyRenderers[i]; if (!r) continue;
                r.enabled = alpha > 0;
                if (originalMaterials == null) continue;
                if (alpha >= 1 || alpha <= 0) { r.sharedMaterials = originalMaterials[i]; continue; }
                for (int m = 0; m < fadeMaterials[i].Length; m++)
                {
                    var material = fadeMaterials[i][m]; if (!material) continue;
                    foreach (var property in new[] { "_BaseColor", "_Color" })
                    {
                        if (!material.HasProperty(property)) continue;
                        var color = originalMaterials[i][m].GetColor(property);color.a *= alpha;
                        material.SetColor(property, color);
                    }
                }
                r.sharedMaterials = fadeMaterials[i];
            }
        }
        void ReleaseFadeMaterials()
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

        public void SamplePreview(float time)
        {
            PreviewTime = Mathf.Clamp(time, 0, Duration);
            if (!movingRoot || !model || !backwardClip) return;
            float progress = Mathf.Clamp01((Mathf.Min(PreviewTime, hideAt) - standSeconds) / Mathf.Max(.1f, retreatSeconds));
            movingRoot.localPosition = Vector3.back * (retreatDistance * progress);
            float cycle = progress >= 1 ? 1 : Mathf.Repeat(progress * Mathf.Max(1, retreatCycles), 1);
            backwardClip.SampleAnimation(model, cycle * backwardClip.length);
            float opacity = bodyFadeSeconds <= 0 ? (PreviewTime < hideAt ? 1 : 0)
                : 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(hideAt - bodyFadeSeconds, hideAt, PreviewTime));
            ApplyVisibility(opacity);

            if (smokeLayers == null) return;
            for (int i = 0; i < smokeLayers.Length; i++)
            {
                var ps = smokeLayers[i];
                if (!ps) continue;
                float localTime = PreviewTime - smokeStart - i * .018f;
                var main = ps.main;
                main.startLifetime = Mathf.Max(.1f, smokeEnd - smokeStart - i * .018f);
                // Explicit simulation makes edit-mode scrubbing and repeated previews deterministic.
                if (localTime <= 0 || PreviewTime >= smokeEnd)
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                else
                    ps.Simulate(localTime, false, true, false);
            }
        }
    }
}
