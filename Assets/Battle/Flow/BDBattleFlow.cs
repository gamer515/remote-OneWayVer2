using System.Collections;
using UnityEngine;
using System.Collections.Generic;

public sealed class BDBattleFlow : IBattleFlow
{
    public IEnumerator Run(BattleContext context)
    {
        context.Box.SetWalls(false, false);
        context.HideMessage();

        // 패턴 버튼으로 시작했다면 선택한 범위만 실행한다.
        if (context.Owner.IsPatternPreview)
        {
            var steps = context.Stage.flow.steps;
            int startIndex = context.Owner.DebugPatternStepIndex;
            var runner = new BattleSequenceRunner();

            if (!context.Owner.DebugContinueFromHere)
            {
                // 선택한 패턴 하나만 실행한다.
                yield return runner.Run(
                    context,
                    new[] { steps[startIndex] });

                yield break;
            }

            // 선택한 단계부터 마지막 단계까지 실행한다.
            var selectedSteps = new List<BattleStepDefinition>();

            for (int i = startIndex; i < steps.Count; i++)
            {
                selectedSteps.Add(steps[i]);
            }

            yield return runner.Run(context, selectedSteps);
            yield break;
        }

        // 실제 단계가 등록되면 공통 실행기로 순서대로 진행한다.
        if (context.Stage.flow.steps.Count > 0)
        {
            yield return new BattleSequenceRunner().Run(context);
            yield break;
        }

        context.Owner.CurrentStep = "BD 준비";
        var scope = context.Cleanup.Create("BD 준비", context.RuntimeRoot);
        scope.OnDispose(() => context.Player.Lock(true));
        try
        {
            yield return BattleBoxTransitionRunner.Run(context, new BattleBoxPreparation
            {
                transition = BattleBoxTransition.Morph,
                size = new Vector2(8f, 8f),
                useScreenCenter = true,
                seconds = 0.5f
            });
            if (context.Owner.Result != BattleResult.Running)
                yield break;

            BattlePlayerPreparationRunner.Apply(context, new BattlePlayerPreparation
            {
                movement = BattleMouseMovement.Free,
                boundary = BattlePlayerBoundary.Box,
                visible = true,
                startPosition = BattlePlayerStartPosition.Normalized,
                normalizedPosition = new Vector2(0.5f, 0.5f)
            });
            context.ShowMessage("BD 패턴 준비 중\n마우스로 이동할 수 있습니다.", false);

            // 패턴이 없는 준비 상태에서는 전투 완료나 씬 이동을 하지 않는다.
            while (context.Owner.Result == BattleResult.Running)
                yield return null;
        }
        finally
        {
            context.Cleanup.Release(scope);
        }
    }
}
