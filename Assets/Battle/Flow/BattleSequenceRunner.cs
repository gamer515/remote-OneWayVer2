using System.Collections;
using System.Collections.Generic;

public sealed class BattleSequenceRunner
{
    public IEnumerator Run(BattleContext context)
    {
        yield return Run(context, context.Stage.flow.steps);
    }

    public IEnumerator Run(
        BattleContext context,
        IEnumerable<BattleStepDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            if (context.Owner.Result != BattleResult.Running)
                yield break;

            context.Owner.CurrentStep = definition.label;

            var scope = context.Cleanup.Create(
                definition.label,
                context.RuntimeRoot);

            try
            {
                // 1단계에서는 공격 패턴에만 공통 상자 준비 적용.
                if (definition.kind == BattleStepKind.Pattern)
                {
                    yield return BattleBoxTransitionRunner.Run(
                        context,
                        definition.boxPreparation);
                }

                if (context.Owner.Result != BattleResult.Running)
                    yield break;

                yield return BattleSteps.Create(definition.kind)
                    .Run(context, definition, scope);
            }
            finally
            {
                context.Cleanup.Release(scope);
            }
        }
    }
}
