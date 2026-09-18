using System.Collections;
public interface IBattleStep
{
    IEnumerator Run(BattleContext context, BattleStepDefinition definition, BattleStepScope scope);
}
