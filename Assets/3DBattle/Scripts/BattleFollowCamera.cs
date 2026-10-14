using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class BattleFollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(8f, 15f, -11f);
        public Vector3 lookOffset = new Vector3(0f, 0.5f, 0f);
        [Min(0.01f)] public float smoothTime = 0.16f;
        Vector3 velocity;

        void Start() => Snap();
        public void Snap()
        {
            if (target == null) return;
            transform.position = target.position + offset;
            transform.rotation = Quaternion.LookRotation(lookOffset - offset);
            velocity = Vector3.zero;
        }
        void LateUpdate()
        {
            if (target == null) return;
            transform.position = Vector3.SmoothDamp(transform.position, target.position + offset,
                ref velocity, smoothTime);
            transform.rotation = Quaternion.LookRotation(lookOffset - offset);
        }
    }
}
