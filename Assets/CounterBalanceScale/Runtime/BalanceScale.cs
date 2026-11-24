using UnityEngine;

namespace CounterBalanceScale
{
    [DisallowMultipleComponent]
    public sealed class BalanceScale : MonoBehaviour
    {
        [SerializeField] private Transform beam;
        [SerializeField] private Transform leftAnchor;
        [SerializeField] private Transform rightAnchor;
        [field: SerializeField] public CoinTray LeftTray { get; private set; }
        [field: SerializeField] public CoinTray RightTray { get; private set; }

        [SerializeField, Range(0f, 25f)] private float maximumAngle = 15f;
        [SerializeField, Min(0.001f)] private float weightDifferenceAtMaximum = 5f;
        [SerializeField, Min(0.01f)] private float responseSpeed = 7f;

        public float Angle { get; private set; }
        public float MaximumAngle => Mathf.Clamp(maximumAngle, 0f, 25f);

        private void LateUpdate()
        {
            if (!beam || !leftAnchor || !rightAnchor || !LeftTray || !RightTray)
                return;

            float difference = LeftTray.TotalWeight - RightTray.TotalWeight;
            float target = Mathf.Clamp(difference / Mathf.Max(0.001f, weightDifferenceAtMaximum), -1f, 1f)
                * MaximumAngle;
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, responseSpeed) * Time.deltaTime);
            Angle = Mathf.Lerp(Angle, target, blend);
            if (Mathf.Abs(Angle - target) < 0.001f)
                Angle = target;

            beam.localRotation = Quaternion.AngleAxis(Angle, Vector3.forward);
            Quaternion level = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            LeftTray.transform.SetPositionAndRotation(leftAnchor.position, level);
            RightTray.transform.SetPositionAndRotation(rightAnchor.position, level);
        }
    }
}