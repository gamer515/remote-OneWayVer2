using System.Collections;
using UnityEngine;

public sealed class LegacyCasePattern : IBattlePattern
{
    public IEnumerator Run(BattleContext context, BattlePatternData data, BattleStepScope scope)
    {
        var attacks = context.Owner.attacks;
        context.Arena.Mode = BattleArena.Boundary.Box;
        context.Player.Lock(false);
        context.Player.SetMovementMode(PlayerController.MovementMode.Free);
        context.Player.SetVisible(true);
        attacks.SetPatternRoot(scope.Root);
        scope.OnDispose(() => { attacks.CancelSequence(); context.Box.CancelAnimations(); });
        yield return attacks.ExecutePattern(data.legacyCase);
        context.Player.Lock(true);
        context.Player.SetMovementMode(PlayerController.MovementMode.Free);
    }
}
