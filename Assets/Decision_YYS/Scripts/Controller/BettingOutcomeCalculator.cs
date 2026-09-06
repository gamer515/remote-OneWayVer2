/// <summary>
/// 현재 지문의 가중치와 플레이어가 배치한 코인 분포를 결합합니다.
/// </summary>
public sealed class BettingOutcomeCalculator
{
    public int[] CalculateStatChanges(Dialogue dialogue, int[] coinCounts)
    {
        if (dialogue == null || !dialogue.IsChoice ||
            dialogue.statWeights == null || dialogue.statWeights.Length != 4 ||
            coinCounts == null || coinCounts.Length != 4)
        {
            return null;
        }

        int[] changes = new int[4];
        for (int i = 0; i < changes.Length; i++)
            changes[i] = coinCounts[i] * dialogue.statWeights[i];

        return changes;
    }
}
