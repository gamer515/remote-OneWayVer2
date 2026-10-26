using UnityEngine;

namespace OneWay.Battle3D
{
    [DefaultExecutionOrder(100)]
    public sealed class BattleArenaBody : MonoBehaviour
    {
        public Battle3DSceneController battle;
        [Min(0f)] public float radius = .5f;
        public Vector3 localCenter;
        [Tooltip("Optional torso anchor. Arms and weapons are deliberately excluded.")]
        public Transform torso;
        public Vector3 Constrain(Vector3 position)
        {
            var boundary = battle != null && battle.CurrentStage != null ? battle.CurrentStage.boundary : null;
            if (boundary == null) return position;
            Vector3 offset = torso != null ? torso.position - transform.position : transform.TransformVector(localCenter);
            return boundary.Constrain(position + offset, radius) - offset;
        }
        public void MoveTo(Vector3 position) => transform.position = Constrain(position);
        void LateUpdate() => MoveTo(transform.position);
    }
}
