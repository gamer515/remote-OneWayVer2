using UnityEngine;

namespace OneWay.Battle3D
{
    // Query after animation evaluation, without requiring a Rigidbody on the animated weapon.
    public sealed class BossWeaponHit : MonoBehaviour
    {
        public Transform weapon;
        [Header("Damage detection")]
        public Vector3 localCenter;
        public Vector3 localSize = Vector3.one;
        [Header("Parry detection")]
        [Min(1f), Tooltip("Scale the parry-only box and downward reach from the weapon center. Damage size is unchanged.")]
        public float parrySizeMultiplier = 1f;
        public bool WindowOpen { get; private set; }
        public bool HasHit { get; private set; }
        public int HitCount { get; private set; }
        public int ParryCount { get; private set; }
        public int CurrentStrikeIndex { get; private set; } = -1;
        public float CurrentDownwardExtension => pattern != null && CurrentStrikeIndex >= 0
            ? pattern.GetDownwardExtension(CurrentStrikeIndex) : 0;
        public float CurrentParryDownwardExtension => CurrentDownwardExtension * Mathf.Max(1f, parrySizeMultiplier);
        public BossWeaponAttackPattern CurrentPattern => pattern;
        public float CurrentFrame => pattern != null && animator != null
            ? animator.GetCurrentAnimatorStateInfo(0).normalizedTime * pattern.clip.length * pattern.clip.frameRate : 0;
        BossWeaponAttackPattern pattern;
        BattlePlayerHealth target;
        Animator animator;
        Vector3 previousCenter;
        Quaternion previousRotation;
        bool previousValid;
        bool[] struck;
        uint attackSequence;
        public void Begin(BossWeaponAttackPattern attack, BattlePlayerHealth player)
        {
            End(); pattern = attack; target = player; HasHit = false; HitCount = ParryCount = 0;
            animator = GetComponent<BattleBoss>().animator;
            struck = new bool[attack.StrikeCount];
            attackSequence++;
        }
        public void End() { pattern = null; target = null; WindowOpen = false; previousValid = false; CurrentStrikeIndex = -1; struck = null; }
        void OnDisable() => End();
        void LateUpdate() => SampleContact();
        public void SampleContact()
        {
            WindowOpen = false;
            // Dedicated phase-entry rhythm resolves its seven strikes separately.
            if (GetComponent<BossPhaseTwoGimmick>()?.OwnsAttack == true) { previousValid = false; return; }
            if (pattern == null || weapon == null || target == null || !target.IsAlive)
            { previousValid = false; CurrentStrikeIndex = -1; return; }
            var state = animator.GetCurrentAnimatorStateInfo(0);
            float time = state.normalizedTime * pattern.clip.length;
            int strike = state.fullPathHash == Animator.StringToHash(pattern.animationState) ? pattern.GetStrikeIndex(time) : -1;
            if (strike != CurrentStrikeIndex) previousValid = false;
            CurrentStrikeIndex = strike;
            if (strike < 0) { previousValid = false; return; }
            WindowOpen = true;
            // Each swing can damage the target once, even if the invulnerability expires mid-swing.
            if (struck[strike]) { previousValid = false; return; }
            GetWorldBox(out Vector3 center, out Quaternion rotation, out Vector3 half);
            var parry = target.GetComponent<BattleParryEffect>();
            bool canParry = parry != null && parry.CanParry(pattern.damage);
            Vector3 parryHalf = half * Mathf.Max(1f, parrySizeMultiplier);
            int steps = previousValid ? Mathf.Clamp(Mathf.CeilToInt(
                Vector3.Distance(center, previousCenter) / .1f + Quaternion.Angle(rotation, previousRotation) / 5f), 1, 128) : 1;
            bool hitThisSample = false;
            for (int i = 1; i <= steps && !hitThisSample; i++)
            {
                float t = i / (float)steps;
                Vector3 sample = previousValid ? Vector3.Lerp(previousCenter, center, t) : center;
                Quaternion turn = previousValid ? Quaternion.Slerp(previousRotation, rotation, t) : rotation;
                // Check the larger parry volume first. Only the original volume can deal damage.
                if (canParry)
                    hitThisSample = QueryContact(sample, turn, parryHalf, CurrentParryDownwardExtension, strike, parry);
                if (!hitThisSample)
                    hitThisSample = QueryContact(sample, turn, half, CurrentDownwardExtension, strike, null);
            }
            previousCenter = center; previousRotation = rotation; previousValid = true;
        }
        bool QueryContact(Vector3 center, Quaternion rotation, Vector3 half, float down, int strike, BattleParryEffect parry)
        {
            foreach (var collider in Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Ignore))
                if (TryContact(collider, strike, parry)) return true;
            // Preserve the world-down sweep even when the weapon itself is tilted.
            if (down > 0)
                foreach (var contact in Physics.BoxCastAll(center, half, Vector3.down, rotation, down, ~0, QueryTriggerInteraction.Ignore))
                    if (TryContact(contact.collider, strike, parry)) return true;
            return false;
        }
        bool TryContact(Collider collider, int strike, BattleParryEffect parry)
        {
            if (collider == null || collider.GetComponentInParent<BattlePlayerHealth>() != target) return false;
            if (struck[strike]) return false;
            if (parry != null)
            {
                if (!parry.TryParry(pattern.damage)) return false;
                // A deflected swing is consumed even when the shield animation ends before the hit window.
                struck[strike] = true; ParryCount++;
                return true;
            }
            if (!target.TakeAttackDamage(pattern.damage, this, attackSequence, strike, pattern.independentStrikes)) return false;
            struck[strike] = true; HasHit = true; HitCount++;
            return true;
        }
        public void GetWorldBox(out Vector3 center, out Quaternion rotation, out Vector3 half)
        {
            center = weapon.TransformPoint(localCenter);
            rotation = weapon.rotation;
            Vector3 scale = weapon.lossyScale;
            half = Vector3.Scale(localSize * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }
        public void GetParryWorldBox(out Vector3 center, out Quaternion rotation, out Vector3 half)
        {
            GetWorldBox(out center, out rotation, out half);
            half *= Mathf.Max(1f, parrySizeMultiplier);
        }
        void OnDrawGizmosSelected()
        {
            if (weapon == null) return;
            var matrix = Gizmos.matrix;
            var color = Gizmos.color;
            GetWorldBox(out var center, out var rotation, out var half);
            DrawBox(center, rotation, half, CurrentDownwardExtension, WindowOpen ? Color.red : Color.yellow);
            GetParryWorldBox(out center, out rotation, out half);
            DrawBox(center, rotation, half, CurrentParryDownwardExtension, Color.cyan);
            Gizmos.matrix = matrix;
            Gizmos.color = color;
        }
        static void DrawBox(Vector3 center, Quaternion rotation, Vector3 half, float down, Color color)
        {
            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Gizmos.color = color;
            Gizmos.DrawWireCube(Vector3.zero, half * 2);
            if (down > 0)
            {
                Gizmos.matrix = Matrix4x4.TRS(center + Vector3.down * down, rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, half * 2);
                Gizmos.matrix = Matrix4x4.identity;
                for (int i = 0; i < 8; i++)
                {
                    var corner = center + rotation * new Vector3((i & 1) == 0 ? -half.x : half.x,
                        (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                    Gizmos.DrawLine(corner, corner + Vector3.down * down);
                }
            }
        }
    }
}
