using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class BattleParryEffect : MonoBehaviour
    {
        public GameObject effectPrefab;
        public GameObject secondaryEffectPrefab;
        public Transform shield;
        public bool TryParry(float damage)
        {
            var health = GetComponent<BattlePlayerHealth>();
            var player = GetComponent<BattlePlayerController>();
            if (damage <= 0 || health == null || !health.IsAlive || health.ignoreDamage ||
                player == null || player.IsDashing || !player.IsBlocking) return false;
            if (effectPrefab != null)
            {
                Vector3 point = shield != null ? shield.position : transform.position + Vector3.up;
                var mesh = shield != null ? shield.GetComponentInChildren<Renderer>() : null;
                if (mesh != null) point = mesh.bounds.center;
                point += transform.forward * .08f;
                Vector3 scale = effectPrefab.transform.localScale * 10f;
                Spawn(effectPrefab, point, scale, "Player Parry Hit Blue");
                Spawn(secondaryEffectPrefab, point, scale, "Player Parry Hit Red");
            }
            return true;
        }
        void Spawn(GameObject prefab, Vector3 point, Vector3 scale, string effectName)
        {
            if (prefab == null) return;
            var effect = Instantiate(prefab, point, Quaternion.LookRotation(transform.forward));
            effect.name = effectName;
            effect.transform.localScale = scale;
            var battle = GetComponentInParent<Battle3DSceneController>();
            if (battle != null && battle.CurrentStage != null) effect.transform.SetParent(battle.CurrentStage.transform, true);
            foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>())
            {
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startLifetimeMultiplier *= 3f;
                particles.Play(false);
            }
            Destroy(effect, 9f);
        }
    }
}
