using System;
using UnityEngine;

public enum BattlePatternKind
{
    LegacyCase = 0,
    Debris = 1,
    Radial = 2,
    Rain = 3,
    Aimed = 4,
    B2 = 5
}

// Embedded values are copied into runtime patterns; never store runtime state here.
[Serializable]
public class BattlePatternData
{
    public BattlePatternKind kind;
    [Range(1, 9)] public int legacyCase = 1;
    [Min(0.1f)] public float duration = 10f;
    [Min(0.05f)] public float interval = 0.5f;
    [Min(0.1f)] public float speed = 5f;
    [Range(1, 40)] public int count = 8;
    public float angleStep = 17f;

    [Min(1)]
    public int b2PatternId = 1;

    [Header("B2 circle and charge")]
    [Min(0.1f)] public float formationSeconds = 1f;
    [Min(0.1f)] public float orbitSecondsPerTurn = 3f;
    [Min(0f)] public float orbitPauseSeconds = 1f;
    [Min(0f)] public float orbitPadding = 0.5f;

    [Header("B2 rotating swords")]
    public Sprite swordSprite;
    public Vector2 swordSize = new Vector2(0.35f, 2.4f);
    [Min(0.1f)] public float swordFlightSeconds = 3f;
    public float swordRotationSpeed = 360f;
    [Min(0.1f)] public float swordPathWidth = 5f;
    [Min(0.01f)] public float swordFlashSeconds = 0.08f;

    [Header("B2 wall swords")]
    [Min(0.1f)] public float wallSwordLength = 3f;
    [Min(0.02f)] public float wallSwordThickness = 0.15f;
}
