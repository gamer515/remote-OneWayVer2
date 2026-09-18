using System.Collections;
public sealed class B3BattleFlow : IBattleFlow
{
    public IEnumerator Run(BattleContext context)
    {
        context.Box.SetWalls(false, false);
        context.Arena.Mode = BattleArena.Boundary.Screen;
        yield return new BattleSequenceRunner().Run(context);
    }
}
