using UnityEngine;

namespace OneWay.Battle3D
{
    [DefaultExecutionOrder(-50)]
    public sealed class BattleBoss : MonoBehaviour
    {
        [Min(1f)] public float maxHealth = 1000f;
        public bool patrolEnabled;
        [Min(0f)] public float patrolDistance = 4f;
        [Min(0f)] public float patrolSpeed = 1.5f;
        public Renderer body;
        public Renderer[] bodyParts;
        public Animator animator;
        public Collider hitbox;
        public float Health { get; private set; }
        public bool IsAlive => Health > 0f;
        Vector3 origin;
        float phase, flashRemaining;
        MaterialPropertyBlock properties;
        int animationState;

        void Awake()
        {
            Health = maxHealth;
            origin = transform.position;
            properties = new MaterialPropertyBlock();
            if (body == null) body = GetComponentInChildren<Renderer>();
            if (hitbox == null) hitbox = GetComponentInChildren<Collider>();
            if (bodyParts == null || bodyParts.Length == 0) bodyParts = GetComponentsInChildren<Renderer>();
        }

        void Update()
        {
            if (patrolEnabled && IsAlive)
            {
                phase += Time.deltaTime * patrolSpeed / Mathf.Max(0.01f, patrolDistance);
                transform.position = origin + Vector3.forward * (Mathf.Sin(phase) * patrolDistance);
            }
            flashRemaining = Mathf.Max(0f, flashRemaining - Time.deltaTime);
            foreach (Renderer part in bodyParts)
            {
                if (part == null) continue;
                if (IsAlive && flashRemaining <= 0f)
                {
                    // Clear the flash to restore each material's original texture and colour.
                    part.SetPropertyBlock(null);
                    continue;
                }
                Color tint = !IsAlive ? new Color(0.15f, 0.22f, 0.3f) : Color.white;
                properties.SetColor("_BaseColor", tint);
                properties.SetColor("_Color", tint);
                part.SetPropertyBlock(properties);
            }
            UpdateAnimation();
        }

        public float PlanarDistance(Vector3 playerPosition)
        {
            if (hitbox == null) return float.PositiveInfinity;
            // Ground-plane distance to the boss's surface, not its tall model's centre.
            Vector3 sample = new Vector3(playerPosition.x, hitbox.bounds.center.y, playerPosition.z);
            Vector3 nearest = hitbox.ClosestPoint(sample);
            nearest.y = sample.y;
            return Vector3.Distance(sample, nearest);
        }

        public void TakeDamage(float damage)
        {
            if (!IsAlive) return;
            Health = Mathf.Max(0f, Health - Mathf.Max(0f, damage));
            flashRemaining = 0.12f;
        }

        public void ResetBoss()
        {
            Health = maxHealth;
            phase = flashRemaining = 0f;
            transform.position = origin;
            animationState = 0;
            UpdateAnimation();
        }

        void UpdateAnimation()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            int desired = Animator.StringToHash(!IsAlive ? "Base Layer.Dead" : patrolEnabled ? "Base Layer.Walk" : "Base Layer.Idle");
            if (animationState == desired || !animator.HasState(0, desired)) return;
            animator.CrossFadeInFixedTime(desired, 0.15f, 0, 0f);
            animationState = desired;
        }
    }
}
