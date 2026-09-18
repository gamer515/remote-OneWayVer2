using System;
using System.Collections.Generic;
using UnityEngine;

public enum BattleStepKind { Dialogue, Corridor, Mist, EnemyEntrance, BuildBox, Pattern, Slash, ScreenBreak, Exit }

[Serializable]
public class BattleStepDefinition
{
    public string label;
    public BattleStepKind kind;
    [TextArea] public string text;
    [Min(0)] public float duration = 1f;
    public BattlePatternData pattern = new BattlePatternData();
}

[Serializable]
public class BattleFlowDefinition
{
    public List<BattleStepDefinition> steps = new List<BattleStepDefinition>();
}
