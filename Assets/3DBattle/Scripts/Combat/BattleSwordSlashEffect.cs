using UnityEngine;

namespace OneWay.Battle3D
{
    [DefaultExecutionOrder(200)]
    public sealed class BattleSwordSlashEffect : MonoBehaviour
    {
        public GameObject slashPrefab;
        public GameObject hitFlashPrefab;
        public Transform sword;
        public int[] startFrames = {52,20,32,16,28};
        public int[] endFrames = {60,28,42,26,38};
        public Quaternion[] localSwingRotations = new Quaternion[5];
        [Min(.01f)] public float effectScale = .2f;
        BattlePlayerController player;
        int pending = -1;
        BattleEffectLease active;
        BattleEffectPool slashPool, flashPool;
        Vector3 anchorOffset;
        void Awake() => player = GetComponent<BattlePlayerController>();
        void Start() => PreparePools();
        void PreparePools()
        {
            if (slashPrefab && (slashPool == null || slashPool.Prefab != slashPrefab))
            { slashPool?.Dispose(); slashPool = new BattleEffectPool(slashPrefab, transform, BattleEffectPool.Kind.Particles, 4, 8); }
            if (hitFlashPrefab && (flashPool == null || flashPool.Prefab != hitFlashPrefab))
            { flashPool?.Dispose(); flashPool = new BattleEffectPool(hitFlashPrefab, transform, BattleEffectPool.Kind.Particles, 4, 8); }
        }
        public void PlayHitFlash(BattleBoss boss)
        {
            if (hitFlashPrefab == null || boss == null) return;
            Vector3 sample = sword != null ? sword.position : transform.position + Vector3.up;
            Vector3 point = boss.hitbox != null ? boss.hitbox.ClosestPoint(sample) : boss.transform.position + Vector3.up;
            var camera = player != null && player.viewCamera != null ? player.viewCamera : Camera.main;
            Quaternion rotation = camera != null ? camera.transform.rotation : transform.rotation;
            PreparePools();
            var effect = flashPool.Rent(point, rotation).Effect;
            effect.name = "Player Sword Hit Flash";
            effect.transform.localScale = hitFlashPrefab.transform.localScale * effectScale;
            effect.Activate(); effect.ReleaseAfter(.65f);
        }
        public void PlayAuthoredSwing(float seconds, float tailSeconds = .1f, float scaleMultiplier = 1f)
        {
            if (slashPrefab == null) return;
            Vector3 center = sword != null ? sword.position : transform.position + Vector3.up;
            Quaternion rotation = sword != null ? sword.rotation : transform.rotation;
            PreparePools();
            var effect = slashPool.Rent(center, rotation).Effect;
            effect.name = "NewPlayer Sword Slash";
            effect.transform.localScale = slashPrefab.transform.localScale * effectScale * Mathf.Max(.01f, scaleMultiplier);
            float lifetime = Mathf.Max(.01f, seconds) + Mathf.Max(0, tailSeconds);
            effect.Activate(); effect.FitSwing(lifetime); effect.ReleaseAfter(lifetime);
        }
        public void PlaySwing(int step, float speed)
        {
            // Previous swing keeps its independently scheduled tail when a new swing starts.
            active = default;
            pending = step >= 1 && step <= 5 ? step - 1 : -1;
        }
        void LateUpdate() => Sample();
        public void Sample()
        {
            if (pending < 0 || player == null || player.animator == null) return;
            var animator = player.animator;
            int hash = Animator.StringToHash("Base Layer.MeshyCombo0" + (pending + 1));
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).fullPathHash == hash)
                state = animator.GetNextAnimatorStateInfo(0);
            if (state.fullPathHash != hash || player.IsDashing || player.IsBlocking || player.IsDefeated)
            { Cancel(); return; }
            AnimationClip clip = null;
            foreach (var c in animator.runtimeAnimatorController.animationClips)
                if (c.name == "MeshyCombo0" + (pending + 1)) { clip = c; break; }
            if (clip == null) { Cancel(); return; }
            float frame = state.normalizedTime * clip.length * clip.frameRate;
            if (frame < startFrames[pending]) return;
            if (frame >= endFrames[pending])
            {
                pending = -1;
                active = default;
                return;
            }
            if (!active.IsAlive && slashPrefab != null)
            {
                var rotation = localSwingRotations[pending];
                if (rotation == new Quaternion(0,0,0,0)) rotation = Quaternion.identity;
                Vector3 center = sword != null ? sword.position : transform.position + Vector3.up;
                PreparePools();
                active = slashPool.Rent(center, transform.rotation * rotation);
                var effect = active.Effect;
                effect.name = "Player Sword Slash " + (pending + 1);
                effect.transform.localScale = slashPrefab.transform.localScale * effectScale;
                anchorOffset = center - transform.position;
                float swingSeconds = (endFrames[pending] - startFrames[pending]) /
                    (clip.frameRate * Mathf.Max(.1f, player.normalAttackSpeed));
                float lifetime = swingSeconds + .1f;
                effect.Activate(); effect.FitSwing(lifetime);
                // Account for a render frame arriving slightly after the authored start frame.
                float remaining = (endFrames[pending] - frame) /
                    (clip.frameRate * Mathf.Max(.1f, player.normalAttackSpeed)) + .1f;
                effect.ReleaseAfter(Mathf.Max(.1f, remaining));
            }
            if (active.IsAlive) active.Effect.transform.position = transform.position + anchorOffset;
        }
        public void Cancel()
        {
            pending = -1;
            active.Release(); active = default;
        }
        void OnDisable() => Cancel();
        void OnDestroy() { slashPool?.Dispose(); flashPool?.Dispose(); }
    }
}

