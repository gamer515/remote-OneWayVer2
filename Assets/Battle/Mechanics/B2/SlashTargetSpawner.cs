using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class SlashTargetSpawner
{
    public IEnumerator Run(BattleContext context, BattleStepScope scope)
    {
        var targets = new List<SlashTarget3D>();
        var input = new SlashInputController();
        scope.OnDispose(() => input.Cancel(context));
        int spawned = 0;
        float next = .7f;
        while (context.Owner.Result == BattleResult.Running)
        {
            float dt = context.Clock.Delta;
            next -= dt;
            if (spawned < context.Stage.slashTargetCount && next <= 0)
            {
                var position = new Vector3(Mathf.Sin(spawned * 2.4f) * 2.7f, Mathf.Cos(spawned * 1.7f) * 1.6f, context.Stage.slashStartZ);
                targets.Add(new SlashTarget3D(context, scope, position, spawned));
                spawned++; next = Mathf.Max(.1f, context.Stage.slashSpawnInterval);
            }
            // Arrival is resolved before input on the same frame, so an already-arrived target cannot be saved.
            foreach (var target in targets)
            {
                target.Tick(dt);
                if (context.Owner.Result != BattleResult.Running) yield break;
            }
            if (input.Tick(context, out var a, out var b)) foreach (var target in targets) target.TrySlash(a, b);
            bool allDone = spawned == context.Stage.slashTargetCount;
            foreach (var target in targets) allDone &= target.Finished;
            if (allDone) break;
            yield return null;
        }
    }
}
