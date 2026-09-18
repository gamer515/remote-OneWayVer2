using System.Collections;
public sealed class B2BattleFlow : IBattleFlow
{
    public IEnumerator Run(BattleContext context)
    {
        context.Box.SetWalls(false, false);
        yield return new BattleSequenceRunner().Run(context);
    }
}
