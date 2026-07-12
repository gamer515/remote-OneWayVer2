using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyPattern", menuName = "One-Way/Enemy Pattern")]
public class EnemyPatternData : ScriptableObject
{
    [Header("Pattern Settings")]
    public string patternName;
    public float patternDuration = 5f;

    [Header("Box Configurations")]
    public Vector2 targetBoxSize = new Vector2(10f, 6f);
    public Vector2 targetBoxPos = new Vector2(0f, 0f);

    [Header("Player Constraints")]
    public PlayerController.MovementMode playerMovementMode; // Free 또는 Gravity

    [Header("Attack Logic")]
    public int patternRoutineID; // AttackPatternManager에서 실행할 패턴 번호 (1~4)
}