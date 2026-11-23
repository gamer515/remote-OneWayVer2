using UnityEngine;

namespace OneWay.Battle3D
{
    // The phase owner advances this clock, so launch, contact and visual expiry agree.
    public sealed class B2PhaseSlashProjectile : MonoBehaviour
    {
        B2PhaseSmokeAttack owner;
        BattlePlayerHealth target;
        ParticleSystem[] particles;
        Vector3 direction, center, half;
        Quaternion slashRotation;
        float delay, speed, life, fade, fadeLength;
        bool contacted, finishing;
        public bool Launched { get; private set; }
        public bool Complete { get; private set; }
        public Vector3 Direction => direction;
        public Vector3 AimPosition { get; private set; }
        public int ShotIndex { get; private set; }
        public B2PhaseSmokeAttack.SlashStyle Style { get; private set; }
        public void Initialize(B2PhaseSmokeAttack source, BattlePlayerHealth player, int index,
            B2PhaseSmokeAttack.SlashStyle style = B2PhaseSmokeAttack.SlashStyle.Vertical)
        {
            owner = source; target = player; ShotIndex = index;
            Style = style; slashRotation = source.GetSlashRotation(style);
            delay = source.launchDelay; speed = source.slashSpeed; life = source.flightSeconds;
            center = source.hitLocalCenter; half = source.hitLocalSize * .5f;
            contacted = finishing = Launched = Complete = false;
            fade = fadeLength = 0;
            if (particles == null)
            {
                var pooled = GetComponent<BattlePooledEffect>();
                particles = pooled ? pooled.Particles : GetComponentsInChildren<ParticleSystem>(true);
            }
            foreach (var ps in particles) if (ps.gameObject.activeInHierarchy)
            { ps.Play(false); fadeLength = Mathf.Max(fadeLength, ps.main.startLifetime.constantMax); }
            FaceTarget();
        }
        void FaceTarget()
        {
            AimPosition = target ? target.transform.position : transform.position + transform.forward;
            direction = Vector3.ProjectOnPlane(AimPosition - transform.position, Vector3.up).normalized;
            if (direction.sqrMagnitude < .001f) direction = Vector3.forward;
            transform.rotation = Quaternion.LookRotation(direction) * slashRotation;
        }
        public void Advance(float delta)
        {
            if (Complete) return;
            delta = Mathf.Max(0, delta);
            if (finishing)
            {
                fade += delta;
                if (fade >= fadeLength) { Complete = true; gameObject.SetActive(false); }
                return;
            }
            float waiting = Mathf.Min(delay, delta); delay -= waiting; delta -= waiting;
            if (!Launched)
            {
                if (delay > .00001f) return;
                FaceTarget(); Launched = true; // Lock once, after the 0.2 second preparation.
            }
            Vector3 from = transform.TransformPoint(center);
            float step = Mathf.Min(delta, life);
            transform.position += direction * (speed * step); life -= step;
            CheckContact(from, transform.TransformPoint(center));
            if (life <= .00001f && !finishing) FinishFlight();
        }
        void CheckContact(Vector3 from, Vector3 to)
        {
            if (contacted || !target || !target.IsAlive) return;
            Vector3 extent = Vector3.Scale(half, transform.lossyScale);
            Quaternion rotation = transform.rotation;
            foreach (var hit in Physics.OverlapBox(from, extent, rotation, ~0, QueryTriggerInteraction.Ignore))
                if (Contact(hit)) return;
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .000001f) return;
            foreach (var hit in Physics.BoxCastAll(from, extent, delta.normalized, rotation, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (Contact(hit.collider)) return;
        }
        bool Contact(Collider collider)
        {
            // Arena walls are only movement barriers; smoke projectiles enter from outside them.
            if (!collider || collider.GetComponentInParent<BattlePlayerHealth>() != target) return false;
            contacted = true;
            bool parried = owner.ResolveContact(target);
            if (parried) FinishFlight();
            return true;
        }
        void FinishFlight()
        {
            finishing = true; fade = 0;
            foreach (var ps in particles) if (ps) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
