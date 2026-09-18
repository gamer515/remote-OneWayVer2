using System.Collections;
public interface IBattlePattern
{
    IEnumerator Run(BattleContext context, BattlePatternData data, BattleStepScope scope);
}
