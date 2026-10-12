using System;
using UnityEngine;

public enum BattlePatternKind
{
    LegacyCase = 0,
    Debris = 1,
    Radial = 2,
    Rain = 3,
    Aimed = 4,
    B2 = 5,
    BD = 6
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

    [Header("BD 패턴")]
    [Min(1)] public int bdPatternId = 1;

    [Header("BD 할퀴기")]
    [Min(0.02f)] public float bdClawWidth = 0.22f;
    [Min(0f)] public float bdClawSpacing = 1.5f;
    [Min(0.1f)] public float bdClawLength = 13.5f;
    [Min(0.01f)] public float bdClawGrowSeconds = 1f;
    [Min(0f)] public float bdClawHoldSeconds = 0.25f;
    [Min(0f)] public float bdClawFadeSeconds = 0.2f;
    [Min(0f)] public float bdClawDamage = 1f;

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

    [Header("B2 falling swords")]
    [Min(0f)] public float fallingSwordDriftSpeed = 0.5f;

    [Header("B2 bursting swords")]
    [Min(0.1f)] public float burstSwordSpacing = 2f;
    [Range(0.05f, 0.95f)] public float burstViewportX = 0.25f;
    [Range(3, 40)] public int burstFragmentCount = 12;
    [Min(0.02f)] public float burstFragmentSize = 0.18f;
    [Min(0.1f)] public float burstFragmentSpeed = 4f;
    [Min(0.1f)] public float burstFragmentLifetime = 4f;

    [Header("플레이어 시작 준비")]
    public BattlePlayerPreparation playerPreparation =
    new BattlePlayerPreparation();

    [Header("BD 사인 곡선 통로")]
    [Min(4)] public int sineColumnCount = 49;
    [Min(1f)] public float sinePathLength = 16f;
    [Min(0.02f)] public float sineBarWidth = 0.22f;
    [Min(0.1f)] public float sineGapHeight = 1.8f;
    [Min(0f)] public float sineAmplitude = 1.5f;
    [Min(0.1f)] public float sineMoveSpeed = 2f;
    [Min(0f)] public float sineDamage = 1f;
    [Header("BD 삼각형 레이저")]
    [Min(0.1f)] public float bdLaserWaveInterval = 1.5f;
    [Min(0.01f)] public float bdLaserEnterSeconds = 0.4f;
    [Min(0f)] public float bdLaserAimSeconds = 0.3f;
    [Min(0.01f)] public float bdLaserHoldSeconds = 0.5f;
    [Min(0f)] public float bdLaserFadeSeconds = 0.2f;
    [Min(0.01f)] public float bdLaserExitSeconds = 0.4f;
    [Min(0.1f)] public float bdLaserEmitterSize = 1.4f;
    [Min(0f)] public float bdLaserBoxGap = 0.45f;
    [Range(1f, 60f)] public float bdLaserSpreadAngle = 10f;
    [Min(1f)] public float bdLaserFinalScale = 2.2f;
    [Min(0f)] public float bdLaserDamage = 1f;

    [Header("BD 가시와 이동 발판")]
    [Min(0.1f)] public float bdSpikeSpeed = 3f;
    [Min(0.1f)] public float bdSpikeLength = 8f;
    [Min(0.05f)] public float bdSpikeHeight = 0.8f;
    [Min(0.02f)] public float bdSpikeWidth = 0.2f;
    [Min(0.03f)] public float bdSpikeSpacing = 0.4f;
    [Min(0f)] public float bdSpikeDamage = 1f;

    [Min(0.01f)] public float bdPlatformDelay = 0.6f;
    [Min(0.1f)] public float bdPlatformSpeed = 4f;
    public Vector2 bdPlatformSize = new Vector2(2.2f, 0.25f);

    [Min(0.1f)] public float bdPlatformLowHeight = 1.6f;
    [Min(0.1f)] public float bdPlatformHeightStep = 1.4f;

    [Min(0.1f)] public float bdPlatformSpacing = 3.5f;
    [Min(0.1f)] public float bdPlatformLastSpacing = 5.5f;

    [Header("BD 양방향 발판")]
    public Vector2 bdCrossPlatformSize = new Vector2(1.8f, 0.25f);

    [Min(0.1f)]
    public float bdCrossPlatformSpacing = 3.5f;

    [Min(0.1f)]
    public float bdCrossPlatformSpeed = 2f;

    // 바닥에서 발판 윗면까지의 높이.
    [Min(0.1f)]
    public float bdCrossLowerHeight = 2f;

    [Min(0.1f)]
    public float bdCrossUpperHeight = 4.4f;

    [Header("BD 중앙 고정 낙하물")]
    public Vector2 bdFallingSquareSize = new Vector2(1.4f, 2.5f);
    [Min(0.1f)] public float bdFallingSquareSpeed = 4.5f;
    [Min(0.05f)] public float bdFallingSquareInterval = 0.65f;
    [Min(0)] public int bdFallingSquareRandomCount = 15;
    [Min(0f)] public float bdFallingSquareGap = 0.2f;
    [Min(0f)] public float bdFallingSquareDamage = 1f;

    [Header("BD 양쪽 할퀴기")]
    [Min(0.1f)] public float bdMirrorClawLength = 6.5f;
    [Min(0.02f)] public float bdMirrorClawWidth = 0.55f;
    [Min(0f)] public float bdMirrorClawSpacing = 1.1f;
    [Min(0f)] public float bdMirrorClawCenterGap = 1.2f;
    [Min(0f)] public float bdMirrorClawBottomOffset = 0.45f;
    [Range(10f, 80f)] public float bdMirrorClawAngle = 45f;
    [Min(0.01f)] public float bdMirrorClawGrowSeconds = 0.4f;
    [Min(0f)] public float bdMirrorClawHoldSeconds = 1.2f;
    [Min(0f)] public float bdMirrorClawDamage = 1f;

    [Header("BD 계단 발판과 순차 할퀴기")]
    public Vector2 bdStairPlatformSize = new Vector2(3.2f, 0.45f);
    public Vector3 bdStairPlatformHeights = new Vector3(3f, 7f, 11f);
    // 발판과 바닥 기준으로 할퀴기의 위치를 조정한다.
    [Range(0f, 1f)] public float bdStairClawPlatformAnchorRatio = 0.75f;
    public float bdStairClawPlatformOffsetX = 0f;
    public float bdStairClawPlatformOffsetY = 0f;
    [Min(0.1f)] public float bdStairJumpForce = 15f;
    [Min(0f)] public float bdStairClawDelay = 1f;
    [Min(0.01f)] public float bdStairClawInterval = 1.2f;
    [Min(0.01f)] public float bdStairClawGrowSeconds = 0.4f;
    [Min(0f)] public float bdStairClawHoldSeconds = 1f;
    [Min(0.1f)] public float bdStairClawLength = 4.5f;
    [Min(0.02f)] public float bdStairClawWidth = 0.45f;
    [Min(0f)] public float bdStairClawSpacing = 0.65f;
    [Min(0f)] public float bdStairClawWallOverlap = 0.45f;
    [Range(10f, 80f)] public float bdStairClawAngle = 40f;
    [Min(0f)] public float bdStairClawDamage = 1f;

    [Header("BD 계단 패턴 바닥 장애물")]
    public Vector2 bdStairFloorSize = new Vector2(0.5f, 0.8f);
    [Min(0f)] public float bdStairFloorGap = 0.3f;
    [Min(0.01f)] public float bdStairFloorGrowSeconds = 0.3f;
    [Min(0f)] public float bdStairFloorDamage = 1f;

    [Header("BD 이동 경로 바닥 장애물")]
    [Min(0f)] public float bdTrailPlayerSpeed = 5f;
    [Min(0f)] public float bdTrailSpawnDelay = 0.3f;
    [Min(0f)] public float bdTrailHoldSeconds = 3f;
    [Min(0.1f)] public float bdTrailDuration = 8f;

    public Vector2 bdTrailSquareSize = new Vector2(0.5f, 1f);
    [Min(0f)] public float bdTrailSquareGap = 0.3f;
    [Min(0.01f)] public float bdTrailGrowSeconds = 0.15f;
    [Min(0.01f)] public float bdTrailRetractSeconds = 0.15f;
    [Min(0f)] public float bdTrailDamage = 1f;
    [Min(0.1f)] public float bdTrailJumpForce = 15f;

    [Header("BD 네 구역 레이저")]
    [Min(0.01f)] public float bdQuadMoveSeconds = 0.7f;
    [Min(0.01f)] public float bdQuadCreateSeconds = 0.3f;

    [Min(1)] public int bdQuadAttackCount = 8;
    [Min(0f)] public float bdQuadAttackGap = 0.3f;

    [Min(0.01f)] public float bdQuadEnterSeconds = 0.4f;
    [Min(0f)] public float bdQuadAimSeconds = 0.3f;
    [Min(0.01f)] public float bdQuadHoldSeconds = 0.5f;
    [Min(0f)] public float bdQuadFadeSeconds = 0.2f;
    [Min(0.01f)] public float bdQuadExitSeconds = 0.4f;

    [Min(0.1f)] public float bdQuadEmitterSize = 1.4f;
    [Min(0f)] public float bdQuadEmitterGap = 0.5f;
    [Min(0f)] public float bdQuadDamage = 1f;
}
