using System;

public sealed class BattleStartData
{
    public int StageIndex { get; }

    public int Trait1 { get; }
    public int Trait2 { get; }
    public int Trait3 { get; }
    public int Trait4 { get; }

    public BattleStartData(
        int stageIndex,
        int trait1,
        int trait2,
        int trait3,
        int trait4)
    {
        if (stageIndex < 1 || stageIndex > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stageIndex),
                "전투 번호는 1~3이어야 합니다.");
        }

        StageIndex = stageIndex;
        Trait1 = trait1;
        Trait2 = trait2;
        Trait3 = trait3;
        Trait4 = trait4;
    }

    // 외부에서 전달하지 않았을 때 사용할 값
    public static BattleStartData CreateDefault()
    {
        return new BattleStartData(1, 1, 1, 1, 1);
    }

    // 디버그 버튼에서 성향은 유지하고 전투 번호만 변경
    public BattleStartData WithStage(int stageIndex)
    {
        return new BattleStartData(
            stageIndex,
            Trait1,
            Trait2,
            Trait3,
            Trait4);
    }
}