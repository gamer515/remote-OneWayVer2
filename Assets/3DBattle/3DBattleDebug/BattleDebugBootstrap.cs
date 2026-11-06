using UnityEngine;

namespace OneWay.Battle3D
{
    // Reuse combat references without the production intro or scene-exit lifecycle.
    [DefaultExecutionOrder(-10)]
    public sealed class BattleDebugBootstrap : MonoBehaviour
    {
        public Battle3DSceneController battle;
        void Awake()
        {
            if (battle != null) battle.enabled = false;
        }
        void Start()
        {
            if (battle == null || !battle.SelectStage(1))
                Debug.LogError("B1 debug scene references are incomplete.", this);
        }
    }
}
