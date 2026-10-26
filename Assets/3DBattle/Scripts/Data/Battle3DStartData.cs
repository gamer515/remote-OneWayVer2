using System;

namespace OneWay.Battle3D
{
    /// <summary>Decision 씬에서 3DBattleScene으로 전달하는 전투 시작 정보입니다.</summary>
    public sealed class Battle3DStartData
    {
        public int StageIndex { get; }
        public string DisplayName => "B" + StageIndex;

        public int Trait1 { get; }
        public int Trait2 { get; }
        public int Trait3 { get; }
        public int Trait4 { get; }

        public Battle3DStartData(
            int stageIndex,
            int trait1,
            int trait2,
            int trait3,
            int trait4)
        {
            if (stageIndex < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stageIndex),
                    "전투 번호는 1 이상이어야 합니다.");
            }

            StageIndex = stageIndex;
            Trait1 = trait1;
            Trait2 = trait2;
            Trait3 = trait3;
            Trait4 = trait4;
        }

        // 씬을 직접 실행해서 전달받은 정보가 없을 때 사용합니다.
        public static Battle3DStartData CreateDefault()
        {
            return new Battle3DStartData(1, 1, 1, 1, 1);
        }

        // 성향을 유지하면서 전투 번호만 변경한 새 데이터를 만듭니다.
        public Battle3DStartData WithStage(int stageIndex)
        {
            return new Battle3DStartData(stageIndex, Trait1, Trait2, Trait3, Trait4);
        }
    }
}
