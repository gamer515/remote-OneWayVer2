using System.Collections.Generic;
using UnityEngine;

public sealed class BattleCleanupService
{
    readonly List<BattleStepScope> scopes = new List<BattleStepScope>();
    public BattleStepScope Create(string name, Transform parent)
    {
        var scope = new BattleStepScope(name, parent);
        scopes.Add(scope);
        return scope;
    }
    public void Release(BattleStepScope scope)
    {
        scope.Dispose();
        scopes.Remove(scope);
    }
    public void Clear()
    {
        for (int i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose();
        scopes.Clear();
    }
}
