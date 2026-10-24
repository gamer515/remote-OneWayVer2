using System.Collections;
using UnityEngine;

namespace OneWay.Battle3D
{
    // Assets hold settings only. Per-execution state belongs to the coroutine/runner.
    public abstract class BossAttackPattern : ScriptableObject
    {
        public string displayName = "Attack";
        [Min(0f)] public float damage = 20f;
        [Min(0f)] public float windupSeconds = 0.7f;
        [Min(0f)] public float recoverySeconds = 0.5f;
        public string animationState = "Base Layer.Attack";
        public abstract IEnumerator Execute(BossPatternRunner runner);
    }
}
