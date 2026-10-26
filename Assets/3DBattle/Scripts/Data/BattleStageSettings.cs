using UnityEngine;

namespace OneWay.Battle3D
{
    [CreateAssetMenu(menuName = "3D Battle/Stage Settings")]
    public sealed class BattleStageSettings : ScriptableObject
    {
        [Min(1)] public int stageIndex = 1;
        public string displayName = "B1";
        [Min(1f)] public float bossHealth = 1000f;
        public BossAttackPattern[] patterns = new BossAttackPattern[0];
    }
}
