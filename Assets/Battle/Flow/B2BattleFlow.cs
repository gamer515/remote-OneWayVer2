using System.Collections;
using System.Collections.Generic;

public sealed class B2BattleFlow : IBattleFlow
{
    public IEnumerator Run(BattleContext context)
    {
        context.Box.SetWalls(false, false);

        var runner = new BattleSequenceRunner();

        // 전체 실행은 등장 연출부터 원래 순서대로 진행한다.
        if (!context.Owner.IsPatternPreview)
        {
            yield return runner.Run(context);
            yield break;
        }

        var steps = context.Stage.flow.steps;
        int startIndex = context.Owner.DebugPatternStepIndex;

        // 디버그 실행에서는 앞쪽 등장 연출을 재생하지 않는다.
        // 각 패턴의 공통 준비가 상자와 플레이어를 준비한다.
        if (!context.Owner.DebugContinueFromHere)
        {
            yield return runner.Run(
                context,
                new[] { steps[startIndex] });

            yield break;
        }

        // 선택한 단계부터 마지막까지 등록 순서대로 실행한다.
        var selectedSteps = new List<BattleStepDefinition>();

        for (int i = startIndex; i < steps.Count; i++)
        {
            selectedSteps.Add(steps[i]);
        }

        yield return runner.Run(context, selectedSteps);
    }
}