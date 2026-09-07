using UnityEngine;

namespace AangFaceAsset
{
    /// <summary>Controls the ten named morph targets on the generated head.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class FaceExpressionController : MonoBehaviour
    {
        public enum Expression { Neutral, Smile, Angry, Sad, Surprised }

        public SkinnedMeshRenderer faceRenderer;
        [Header("Expression (usually choose one)")]
        [Range(0, 100)] public float smile;
        [Range(0, 100)] public float angry;
        [Range(0, 100)] public float sad;
        [Range(0, 100)] public float surprised;
        [Header("Eyes / mouth / brows")]
        [Range(0, 100)] public float blinkLeft;
        [Range(0, 100)] public float blinkRight;
        [Range(0, 100)] public float jawOpen;
        [Range(0, 100)] public float mouthWide;
        [Range(0, 100)] public float mouthPucker;
        [Range(0, 100)] public float browRaise;

        private static readonly string[] ShapeNames =
        {
            "Smile", "Angry", "Sad", "Surprised", "BlinkLeft", "BlinkRight",
            "JawOpen", "MouthWide", "MouthPucker", "BrowRaise"
        };
        private readonly int[] indices = new int[10];
        private Mesh cachedMesh;

        private void Reset() { faceRenderer = GetComponentInChildren<SkinnedMeshRenderer>(); }
        private void OnEnable() { cachedMesh = null; Apply(); }
        private void LateUpdate() { Apply(); }
        private void OnValidate() { cachedMesh = null; }

        public void Apply()
        {
            if (!faceRenderer || !faceRenderer.sharedMesh) return;
            if (cachedMesh != faceRenderer.sharedMesh)
            {
                cachedMesh = faceRenderer.sharedMesh;
                for (int i = 0; i < ShapeNames.Length; i++)
                    indices[i] = cachedMesh.GetBlendShapeIndex(ShapeNames[i]);
            }
            Set(0, smile); Set(1, angry); Set(2, sad); Set(3, surprised);
            Set(4, blinkLeft); Set(5, blinkRight); Set(6, jawOpen);
            Set(7, mouthWide); Set(8, mouthPucker); Set(9, browRaise);
        }

        private void Set(int slot, float value)
        {
            if (indices[slot] >= 0)
                faceRenderer.SetBlendShapeWeight(indices[slot], Mathf.Clamp(value, 0, 100));
        }

        /// <summary>Changes the four mood sliders; eye/mouth controls remain available.</summary>
        public void SetExpression(Expression expression, float weight = 100)
        {
            smile = angry = sad = surprised = 0;
            weight = Mathf.Clamp(weight, 0, 100);
            switch (expression)
            {
                case Expression.Smile: smile = weight; break;
                case Expression.Angry: angry = weight; break;
                case Expression.Sad: sad = weight; break;
                case Expression.Surprised: surprised = weight; break;
            }
            Apply();
        }

        public void ResetExpression()
        {
            smile = angry = sad = surprised = blinkLeft = blinkRight = 0;
            jawOpen = mouthWide = mouthPucker = browRaise = 0;
            Apply();
        }
    }
}
