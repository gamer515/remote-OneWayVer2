using System;

/// <summary>Data-owned costs use the same four balances as the player's abilities.</summary>
public static class AbilityPayment
{
    public static bool IsValid(int[] cost) => cost == null ||
        (cost.Length == 4 && Array.TrueForAll(cost, value => value >= 0));

    public static bool CanPay(int[] balances, int[] cost)
    {
        if (!IsValid(cost)) return false;
        if (cost == null) return true;
        if (balances == null || balances.Length != 4) return false;
        for (int i = 0; i < 4; i++) if (balances[i] < cost[i]) return false;
        return true;
    }

    public static bool TryPay(int[] balances, int[] cost, out int[] result)
    {
        result = null;
        if (!CanPay(balances, cost)) return false;
        result = (int[])balances.Clone();
        if (cost != null) for (int i = 0; i < 4; i++) result[i] -= cost[i];
        return true;
    }
}
