using System;
using UnityEngine;

namespace OneWay.Battle3D
{
    // Consume authored foot travel after animation, before arena constraints and sword effects.
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(BattleBoss))]
    public sealed class B2AttackStepMovement : MonoBehaviour
    {
        [Serializable]
        public sealed class AttackStep
        {
            public string stateName;
            public AnimationClip clip;
            [Min(0f)] public float distanceMultiplier = 1f;
            [Tooltip("발의 접지 동작에서 계산한 누적 전진 거리입니다. 시간은 클립의 초 단위입니다.")]
            public AnimationCurve forwardDistance = AnimationCurve.Constant(0, 1, 0);
        }

        public Animator animator;
        public CapsuleCollider bodyCollider;
        public BattleArenaBody arenaBody;
        [Tooltip("모든 공격의 전진 거리 배율. 1은 발 동작에서 계산한 원래 보폭입니다.")]
        [Min(0f)] public float distanceMultiplier = 1f;
        [Min(0f)] public float collisionPadding = .03f;
        public LayerMask obstacleLayers = ~0;
        public AttackStep[] attacks = new AttackStep[0];
        BattleBoss boss;
        AttackStep currentAttack;
        float previousTime, previousDistance;
        readonly RaycastHit[] hits = new RaycastHit[32];
        readonly Collider[] overlaps = new Collider[32];

        void Awake() => boss = GetComponent<BattleBoss>();
        void OnEnable() => ResetTracking();
        void OnDisable() => ResetTracking();
        void LateUpdate() => Advance();

        public void ResetTracking()
        {
            currentAttack = null;
            previousTime = previousDistance = 0f;
        }

        AttackStep Find(AnimatorStateInfo state)
        {
            foreach (var attack in attacks)
                if (attack != null && attack.clip != null && state.IsName(attack.stateName)) return attack;
            return null;
        }

        public void Advance()
        {
            if (boss == null) boss = GetComponent<BattleBoss>();
            var combat = GetComponent<B2CombatController>();
            if (combat != null && combat.ControlsAnimation && combat.State != B2CombatState.Attack)
            { ResetTracking(); return; }
            if (boss == null || !boss.IsAlive || animator == null || !animator.isActiveAndEnabled)
            { ResetTracking(); return; }

            var state = animator.GetCurrentAnimatorStateInfo(0);
            var attack = Find(state);
            if (animator.IsInTransition(0))
            {
                var next = animator.GetNextAnimatorStateInfo(0);
                var nextAttack = Find(next);
                if (nextAttack != null) { state = next; attack = nextAttack; }
            }
            if (attack != currentAttack)
            {
                // Apply the last fraction of a completed attack, never the remainder of an interrupted one.
                var actual = animator.GetCurrentAnimatorStateInfo(0);
                if (currentAttack != null && actual.IsName("WaitAfter" + currentAttack.stateName))
                    Consume(currentAttack.clip.length);
                currentAttack = attack;
                previousTime = 0f;
                previousDistance = attack != null ? attack.forwardDistance.Evaluate(0) : 0f;
            }
            if (currentAttack == null) return;
            float time = Mathf.Clamp01(state.normalizedTime) * currentAttack.clip.length;
            if (time < previousTime)
            {
                // A restart/reset begins a new motion; never move backwards to its initial position.
                previousTime = 0f;
                previousDistance = currentAttack.forwardDistance.Evaluate(0);
            }
            Consume(time);
        }

        void Consume(float time)
        {
            float distance = currentAttack.forwardDistance.Evaluate(time);
            float step = Mathf.Max(0f, distance - previousDistance);
            // Blocked travel is consumed too, so it cannot build up and cause a later jump.
            previousDistance = distance;
            previousTime = time;
            float scale = Mathf.Abs(animator.transform.lossyScale.z);
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            MoveWorldDelta(forward * (step * scale * distanceMultiplier * currentAttack.distanceMultiplier));
        }

        bool Ignore(Collider obstacle, float groundY)
        {
            return obstacle == null || obstacle.transform.IsChildOf(transform) ||
                obstacle.bounds.max.y <= groundY + .02f;
        }

        public void MoveWorldDelta(Vector3 delta)
        {
            if (delta.sqrMagnitude < .00000001f) return;
            Vector3 origin = transform.position;
            if (arenaBody != null) delta = arenaBody.Constrain(origin + delta) - origin;
            delta.y = 0f;
            float length = delta.magnitude;
            if (length < .0001f) return;
            Vector3 direction = delta / length;
            if (bodyCollider != null && bodyCollider.enabled)
            {
                Physics.SyncTransforms();
                var shape = bodyCollider.transform;
                var scale = shape.lossyScale;
                float radius = bodyCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float halfSegment = Mathf.Max(0f, bodyCollider.height * Mathf.Abs(scale.y) * .5f - radius);
                Vector3 center = shape.TransformPoint(bodyCollider.center);
                Vector3 top = center + shape.up * halfSegment, bottom = center - shape.up * halfSegment;
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps, obstacleLayers, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length) return;
                for (int i = 0; i < count; i++)
                {
                    var obstacle = overlaps[i];
                    if (Ignore(obstacle, origin.y)) continue;
                    if (Physics.ComputePenetration(bodyCollider, shape.position, shape.rotation,
                        obstacle, obstacle.transform.position, obstacle.transform.rotation, out var separation, out var depth) &&
                        depth > .001f && Vector3.Dot(direction, separation) < -.01f) return;
                }
                count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, hits,
                    length + collisionPadding, obstacleLayers, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) return;
                for (int i = 0; i < count; i++)
                {
                    if (Ignore(hits[i].collider, origin.y) || Mathf.Abs(hits[i].normal.y) > .8f) continue;
                    length = Mathf.Min(length, Mathf.Max(0f, hits[i].distance - collisionPadding));
                }
            }
            var destination = origin + direction * length;
            if (arenaBody != null) arenaBody.MoveTo(destination);
            else transform.position = destination;
        }
    }
}
