using UnityEngine;

[CreateAssetMenu(menuName = "One-Way/Battle Stage")]
public class BattleStageData : ScriptableObject
{
    [Range(1, 3)] public int stageIndex = 1;
    public BattleFlowDefinition flow = new BattleFlowDefinition();
    [Header("B1 movement")]
    [Range(0.05f, 0.9f)] public float forwardViewportY = 1f / 3f;
    [Range(0.1f, 0.9f)] public float corridorWidth = 0.48f;
    public float backwardSpeed = 0.65f;
    public float clickAdvance = 0.65f;
    public float mouseFollow = 5f;
    public float recoverySeconds = 5f;
    public float recoveryGrace = 1.5f;
    [Header("B2 slash")]
    [Min(1)] public int slashTargetCount = 5;
    [Min(0.1f)] public float slashSpawnInterval = 1.8f;
    [Min(1)] public float slashStartZ = 22f;
    [Min(0.1f)] public float slashSpeed = 8f;
    [Min(1)] public float slashWidthPixels = 14f;
    [Min(0)] public float slashDamage = 1f;
    [Header("Completion")]
    public bool returnToDecision = true;
}
