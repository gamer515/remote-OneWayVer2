using System.Collections;
public interface IBattleFlow
{
    IEnumerator Run(BattleContext context);
}
