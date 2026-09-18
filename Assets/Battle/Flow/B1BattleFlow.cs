using System.Collections;
public sealed class B1BattleFlow : IBattleFlow
{
    public IEnumerator Run(BattleContext context)
    {
        context.Arena.CorridorWidth = context.Stage.corridorWidth;
        yield return new BattleSequenceRunner().Run(context);
    }
}
