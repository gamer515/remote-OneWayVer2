using UnityEngine;

namespace OneWay.Battle3D
{
    // Apply after animation, before grounding (-30), travel (-10), and weapon queries (0).
    [DefaultExecutionOrder(-40)]
    public sealed class BossAttackFacingCorrection : MonoBehaviour
    {
        public Animator animator;
        public Transform skeletonRoot;
        public BossWeaponAttackPattern attack;
        public AnimationCurve yawDegrees = AnimationCurve.Constant(0, 1, 0);
        Quaternion originalRotation, appliedRotation;
        bool applied;

        void Update() => ClearCorrection();
        void LateUpdate()
        {
            if (animator == null || attack == null || attack.clip == null) return;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash != Animator.StringToHash(attack.animationState) || animator.IsInTransition(0))
            { ClearCorrection(); return; }
            ApplyAtTime(state.normalizedTime * attack.clip.length);
        }

        public void ApplyAtTime(float seconds)
        {
            ClearCorrection();
            if (skeletonRoot == null || attack == null || attack.clip == null ||
                seconds < 0 || seconds >= attack.clip.length) return;
            float yaw = yawDegrees.Evaluate(seconds);
            if (Mathf.Abs(yaw) < .00001f) return;
            originalRotation = skeletonRoot.localRotation;
            skeletonRoot.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * skeletonRoot.rotation;
            appliedRotation = skeletonRoot.localRotation;
            applied = true;
        }

        public void ClearCorrection()
        {
            // An Animator/manual preview may already have replaced the corrected pose.
            if (applied && skeletonRoot != null && Quaternion.Angle(skeletonRoot.localRotation, appliedRotation) < .01f)
                skeletonRoot.localRotation = originalRotation;
            applied = false;
        }
        void OnDisable() => ClearCorrection();
    }
}
