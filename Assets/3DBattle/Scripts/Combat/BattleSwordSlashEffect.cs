using UnityEngine;
using UnityEngine.VFX;

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
        GameObject active;
        Vector3 anchorOffset;
        void Awake() => player = GetComponent<BattlePlayerController>();
        public void PlayHitFlash(BattleBoss boss)
        {
            if (hitFlashPrefab == null || boss == null) return;
            Vector3 sample = sword != null ? sword.position : transform.position + Vector3.up;
            Vector3 point = boss.hitbox != null ? boss.hitbox.ClosestPoint(sample) : boss.transform.position + Vector3.up;
            var camera = player != null && player.viewCamera != null ? player.viewCamera : Camera.main;
            Quaternion rotation = camera != null ? camera.transform.rotation : transform.rotation;
            var effect = Instantiate(hitFlashPrefab, point, rotation);
            effect.name = "Player Sword Hit Flash";
            effect.transform.localScale = hitFlashPrefab.transform.localScale * effectScale;
            foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>()) ps.Play(false);
            Destroy(effect, .65f);
        }
        public void PlayAuthoredSwing(float seconds, float tailSeconds = .1f, float scaleMultiplier = 1f)
        {
            if (slashPrefab == null) return;
            Vector3 center = sword != null ? sword.position : transform.position + Vector3.up;
            Quaternion rotation = sword != null ? sword.rotation : transform.rotation;
            var effect = Instantiate(slashPrefab, center, rotation);
            effect.name = "NewPlayer Sword Slash";
            effect.transform.localScale = slashPrefab.transform.localScale * effectScale * Mathf.Max(.01f, scaleMultiplier);
            float lifetime = Mathf.Max(.01f, seconds) + Mathf.Max(0, tailSeconds);
            effect.GetComponent<BattleRefractiveSlash>()?.Initialize(lifetime);
            ConfigureParticles(effect, lifetime);
            foreach (var vfx in effect.GetComponentsInChildren<VisualEffect>()) vfx.playRate = 1f / lifetime;
            Destroy(effect, lifetime);
        }
        public void PlaySwing(int step, float speed)
        {
            // Previous swing keeps its independently scheduled tail when a new swing starts.
            active = null;
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
                active = null;
                return;
            }
            if (active == null && slashPrefab != null)
            {
                var rotation = localSwingRotations[pending];
                if (rotation == new Quaternion(0,0,0,0)) rotation = Quaternion.identity;
                Vector3 center = sword != null ? sword.position : transform.position + Vector3.up;
                active = Instantiate(slashPrefab, center, transform.rotation * rotation);
                active.name = "Player Sword Slash " + (pending + 1);
                active.transform.localScale = slashPrefab.transform.localScale * effectScale;
                anchorOffset = center - transform.position;
                float swingSeconds = (endFrames[pending] - startFrames[pending]) /
                    (clip.frameRate * Mathf.Max(.1f, player.normalAttackSpeed));
                float lifetime = swingSeconds + .1f;
                active.GetComponent<BattleRefractiveSlash>()?.Initialize(lifetime);
                ConfigureParticles(active, lifetime);
                foreach (var vfx in active.GetComponentsInChildren<VisualEffect>())
                    vfx.playRate = 1f / lifetime;
                // Account for a render frame arriving slightly after the authored start frame.
                float remaining = (endFrames[pending] - frame) /
                    (clip.frameRate * Mathf.Max(.1f, player.normalAttackSpeed)) + .1f;
                Destroy(active, Mathf.Max(.1f, remaining));
            }
            if (active != null) active.transform.position = transform.position + anchorOffset;
        }
        static void ConfigureParticles(GameObject effect, float lifetime)
        {
            // The package's principal slash lasts half a second; fit it to the swing window.
            foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>())
            {
                var main = particles.main;
                main.simulationSpeed = .5f / Mathf.Max(.01f, lifetime);
                particles.Play(false);
            }
        }
        public void Cancel()
        {
            pending = -1;
            if (active != null) Destroy(active);
            active = null;
        }
        void OnDisable() => Cancel();
    }
}

