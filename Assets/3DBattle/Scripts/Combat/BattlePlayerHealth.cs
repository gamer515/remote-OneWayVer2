using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class BattlePlayerHealth : MonoBehaviour
    {
        [Min(1f)] public float maxHealth = 100f;
        public bool ignoreDamage;
        [Min(0f)] public float hitInvulnerabilitySeconds = 3f;
        public float Health { get; private set; }
        public bool IsAlive => Health > 0f;
        public float InvulnerabilityRemaining => Mathf.Max(0f, invulnerableUntil - Time.time);
        public bool IsHitInvulnerable => InvulnerabilityRemaining > 0f;
        float invulnerableUntil;
        Object lastAttackSource;
        uint lastAttackSequence;
        int lastStrike = -1;
        void Awake() => ResetHealth();
        public void ResetHealth()
        {
            Health = maxHealth;
            invulnerableUntil = 0f;
            lastAttackSource = null; lastStrike = -1;
        }
        public void TakeDamage(float damage)
        {
            TryTakeDamage(damage, false, null, 0, -1);
        }
        public bool TakeAttackDamage(float damage, Object source, uint sequence, int strike, bool independent)
        {
            bool sameCombo = independent && source != null && source == lastAttackSource &&
                sequence == lastAttackSequence && strike > lastStrike;
            return TryTakeDamage(damage, sameCombo, source, sequence, strike);
        }
        bool TryTakeDamage(float damage, bool bypassHitInvulnerability, Object source, uint sequence, int strike)
        {
            var player = GetComponent<BattlePlayerController>();
            if (damage <= 0f || ignoreDamage || !IsAlive || (IsHitInvulnerable && !bypassHitInvulnerability) ||
                (player != null && (player.IsDashing || player.IsBlocking))) return false;
            Health = Mathf.Max(0f, Health - Mathf.Max(0f, damage));
            invulnerableUntil = Time.time + Mathf.Max(0f, hitInvulnerabilitySeconds);
            lastAttackSource = source; lastAttackSequence = sequence; lastStrike = strike;
            if (!IsAlive && player != null) player.PlayDefeat();
            return true;
        }
    }
}
