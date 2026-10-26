using UnityEngine;

namespace OneWay.Battle3D
{
    // Runs after character animation and arena constraints, including boss root travel.
    [DefaultExecutionOrder(200)]
    public sealed class BossBodyBarrier : MonoBehaviour
    {
        public BattlePlayerController player;
        public Transform torso;
        public CapsuleCollider bodyCollider;
        [Min(.1f)] public float bodyRadius = 1.65f;
        [Min(.2f)] public float bodyHeight = 4f;

        void LateUpdate()
        {
            if (bodyCollider == null || player == null) return;
            var anchor = torso != null ? torso.position : transform.position;
            anchor.y = transform.position.y + bodyHeight * .5f;
            var shape = bodyCollider.transform;
            shape.SetPositionAndRotation(anchor, Quaternion.identity);
            var scale = shape.parent != null ? shape.parent.lossyScale : Vector3.one;
            shape.localScale = new Vector3(1f / Mathf.Abs(scale.x), 1f / Mathf.Abs(scale.y), 1f / Mathf.Abs(scale.z));
            bodyCollider.radius = bodyRadius;
            bodyCollider.height = Mathf.Max(bodyHeight, bodyRadius * 2f);
            var motor = player.GetComponent<CharacterController>();
            if (motor == null || !motor.enabled) return;
            Physics.SyncTransforms();
            if (!Physics.ComputePenetration(motor, motor.transform.position, motor.transform.rotation,
                bodyCollider, shape.position, shape.rotation, out var direction, out var distance)) return;
            direction.y = 0f;
            if (direction.sqrMagnitude < .0001f)
            {
                direction = player.transform.position - anchor;
                direction.y = 0f;
                if (direction.sqrMagnitude < .0001f) direction = -transform.forward;
            }
            var push = direction.normalized * (distance + .02f);
            motor.Move(push);
            var playerBody = player.GetComponent<BattleArenaBody>();
            if (playerBody != null) playerBody.MoveTo(player.transform.position);
            player.SyncBodyPosition();
            // If the arena wall prevents separating the player, move the boss back instead.
            Physics.SyncTransforms();
            if (Physics.ComputePenetration(motor, motor.transform.position, motor.transform.rotation,
                bodyCollider, shape.position, shape.rotation, out direction, out distance))
            {
                direction.y = 0f;
                var bossBody = GetComponent<BattleArenaBody>();
                var position = transform.position - direction.normalized * (distance + .02f);
                if (bossBody != null) bossBody.MoveTo(position);
                else transform.position = position;
            }
        }
    }
}
