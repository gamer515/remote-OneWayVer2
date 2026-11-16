using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class B2SlashTrailLayout : MonoBehaviour
    {
        public Transform top;
        public Transform upperMiddle;
        public Transform center;
        public Transform lowerMiddle;
        public Transform bottom;

        // Scale only along the ribbons' backward axis; their height/width stays unchanged.
        public void ApplyLengths(float topLength, float upperLength, float centerLength,
            float lowerLength, float bottomLength)
        {
            // Attachment depths follow the curved crescent, with a small overlap
            // so the transparent ends of the ribbons do not leave a visible gap.
            SetLength(top, topLength, .85f);
            SetLength(upperMiddle, upperLength, 1.3f);
            SetLength(center, centerLength, 1.45f);
            SetLength(lowerMiddle, lowerLength, 1.2f);
            SetLength(bottom, bottomLength, .65f);
        }

        static void SetLength(Transform row, float length, float attachmentDepth)
        {
            if (row == null) return;
            float value = Mathf.Max(0, length);
            float scale = Mathf.Max(.001f, value);
            row.localScale = new Vector3(1, 1, scale);
            // The authored 10-unit ribbon is centered at z=-4.13:
            // its leading end is z=0.87. Keep that end on the blade when scaling.
            var position = row.localPosition;
            position.z = attachmentDepth - .87f * scale;
            row.localPosition = position;
            row.gameObject.SetActive(value > 0);
        }
    }
}
