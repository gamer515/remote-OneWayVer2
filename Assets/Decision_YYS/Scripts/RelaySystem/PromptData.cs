using UnityEngine;

[CreateAssetMenu(fileName = "PromptData", menuName = "ScriptableObjects/PromptData", order = 1)]
public class PromptData : ScriptableObject
{
    [Header("Story Change Intensity")]
    [Tooltip("각 능력치가 시작하는 중립값입니다.")]
    public int neutralStatValue = 5;
    [Tooltip("중립값에서 벗어난 변화량 합계가 이 값 이상이면 Moderate입니다.")]
    [Min(0)] public int moderateChangeMinStat = 4;
    [Tooltip("중립값에서 벗어난 변화량 합계가 이 값 이상이면 Strong입니다.")]
    [Min(0)] public int strongChangeMinStat = 8;
}
