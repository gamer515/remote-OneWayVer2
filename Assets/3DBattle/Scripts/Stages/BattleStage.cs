using UnityEngine;

namespace OneWay.Battle3D
{
    // Keep environment and boss below this object so stages remain editable in Scene view.
    public sealed class BattleStage : MonoBehaviour
    {
        public BattleStageSettings settings;
        public BattleBoss boss;
        public BossPatternRunner patternRunner;
        public Transform playerSpawn;
        public Transform bossSpawn;
        public BattleArenaBoundary boundary;
        public int StageIndex => settings != null ? settings.stageIndex : 0;
        public string DisplayName => settings != null ? settings.displayName : name;
    }
}
