using System.Collections;

public sealed class BattleSequenceRunner
{
    public IEnumerator Run(BattleContext context)
    {
        foreach (var definition in context.Stage.flow.steps)
        {
            if (context.Owner.Result != BattleResult.Running) yield break;
            context.Owner.CurrentStep = definition.label;
            var scope = context.Cleanup.Create(definition.label, context.RuntimeRoot);
            try { yield return BattleSteps.Create(definition.kind).Run(context, definition, scope); }
            finally { context.Cleanup.Release(scope); }
        }
    }
}
