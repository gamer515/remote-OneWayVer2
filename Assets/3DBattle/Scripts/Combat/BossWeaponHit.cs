using UnityEngine;

namespace OneWay.Battle3D
{
    // Query after animation evaluation, without requiring a Rigidbody on the animated weapon.
    public sealed class BossWeaponHit : MonoBehaviour
    {
        public Transform weapon;
        public Vector3 localCenter;
        public Vector3 localSize = Vector3.one;
        public bool WindowOpen { get; private set; }
        public bool HasHit { get; private set; }
        public int HitCount { get; private set; }
        public int CurrentStrikeIndex { get; private set; } = -1;
        public float CurrentDownwardExtension => pattern != null && CurrentStrikeIndex >= 0
            ? pattern.GetDownwardExtension(CurrentStrikeIndex) : 0;
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
            End(); pattern = attack; target = player; HasHit = false; HitCount = 0;
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
            int steps = previousValid ? Mathf.Clamp(Mathf.CeilToInt(
                Vector3.Distance(center, previousCenter) / .1f + Quaternion.Angle(rotation, previousRotation) / 5f), 1, 128) : 1;
            bool hitThisSample = false;
            for (int i = 1; i <= steps && !hitThisSample; i++)
            {
                float t = i / (float)steps;
                Vector3 sample = previousValid ? Vector3.Lerp(previousCenter, center, t) : center;
                Quaternion turn = previousValid ? Quaternion.Slerp(previousRotation, rotation, t) : rotation;
                foreach (var collider in Physics.OverlapBox(sample, half, turn, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (TryHit(collider, strike)) { hitThisSample = true; break; }
                }
                // Sweep the same oriented box vertically. Unlike increasing its local Y size,
                // this expands only downward even when the pickaxe is tilted or horizontal.
                float down = CurrentDownwardExtension;
                if (!hitThisSample && down > 0)
                    foreach (var contact in Physics.BoxCastAll(sample, half, Vector3.down, turn, down, ~0, QueryTriggerInteraction.Ignore))
                        if (TryHit(contact.collider, strike)) { hitThisSample = true; break; }
            }
            previousCenter = center; previousRotation = rotation; previousValid = true;
        }
        bool TryHit(Collider collider, int strike)
        {
            if (collider == null || collider.GetComponentInParent<BattlePlayerHealth>() != target) return false;
            if (struck[strike]) return false;
            var parry = target.GetComponent<BattleParryEffect>();
            if (parry != null && parry.TryParry(pattern.damage))
            {
                // A deflected swing is consumed even when the shield animation ends before the hit window.
                struck[strike] = true;
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
        void OnDrawGizmosSelected()
        {
            if (weapon == null) return;
            var matrix = Gizmos.matrix;
            GetWorldBox(out var center, out var rotation, out var half);
            Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
            Gizmos.color = WindowOpen ? Color.red : Color.yellow;
            Gizmos.DrawWireCube(Vector3.zero, half * 2);
            float down = CurrentDownwardExtension;
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
            Gizmos.matrix = matrix;
        }
    }
}
