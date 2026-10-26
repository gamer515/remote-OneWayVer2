using UnityEngine;

namespace OneWay.Battle3D
{
    // Stores observations only; does not move the boss or run combat logic.
    public class BossJumpTimingDebug : MonoBehaviour
    {
        public Animator animator;
        public AnimationClip clip;
        [Min(.05f)] public float previewSpeed = .25f;
        [Tooltip("Clip time in seconds. -1 means not marked yet.")]
        public float takeoffTime = -1f;
        public float landingTime = -1f;
        public Transform leftFoot;
        public Transform rightFoot;
        public bool HasValidInterval => clip != null && takeoffTime >= 0f && landingTime > takeoffTime && landingTime <= clip.length;
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            if (leftFoot != null) Gizmos.DrawWireSphere(leftFoot.position, .06f);
            if (rightFoot != null) Gizmos.DrawWireSphere(rightFoot.position, .06f);
        }
    }
}
