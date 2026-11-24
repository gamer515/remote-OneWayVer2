using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Quoted general tax and carried amounts, without changing the issued invoice.</summary>
internal sealed class MineTaxBill
{
    internal readonly MineTaxInvoice Current;
    internal readonly MineTaxArrear[] Arrears;
    internal readonly MineTaxLine[] SingleLines;
    internal readonly int Mixed, Penalty, Principal, Total, FlexibleTotal;
    private readonly int[] fixedCost = new int[4];

    internal MineTaxBill(MineTaxInvoice invoice, List<MineTaxArrear> arrears)
    {
        Current = invoice;
        Arrears = arrears?.ToArray() ?? Array.Empty<MineTaxArrear>();
        Principal = Arrears.Sum(entry => entry.invoice.Total);
        Penalty = Arrears.Sum(entry => entry.penaltyCoins);
        Total = Current.Total + Principal + Penalty;

        var single = new List<MineTaxLine>();
        foreach (var line in Current.lines.Concat(Arrears.SelectMany(entry => entry.invoice.lines)))
        {
            if (line.kind == "single") single.Add(line);
            else if (line.kind == "mixed") Mixed += line.coins;
            else
                for (int i = 0; i < fixedCost.Length; i++) fixedCost[i] += line.cost[i];
        }
        if (Penalty > 0)
            single.Add(new MineTaxLine { label = "벌금", kind = "single", coins = Penalty });
        SingleLines = single.ToArray();
        FlexibleTotal = RemainingFlexible(0, Mixed);
    }

    internal int[] FixedCost() => (int[])fixedCost.Clone();

    internal int RemainingFlexible(int nextSingle, int mixed) =>
        mixed + SingleLines.Skip(nextSingle).Sum(line => line.coins);

    internal bool CanAllocate(int[] wallet, int[] cost, int nextSingle, int mixed)
    {
        if (!AbilityPayment.CanPay(wallet, cost)) return false;
        var surplus = wallet.Select((amount, index) => amount - cost[index]).ToArray();
        if (surplus.Sum(amount => (long)amount) < RemainingFlexible(nextSingle, mixed)) return false;
        var requirements = SingleLines.Skip(nextSingle).Select(line => line.coins)
            .OrderByDescending(amount => amount).ToArray();
        return FitsSingleTypes(surplus, requirements, 0);
    }

    private static bool FitsSingleTypes(int[] surplus, int[] requirements, int index)
    {
        if (index == requirements.Length) return true;
        int amount = requirements[index];
        for (int ability = 0; ability < surplus.Length; ability++)
        {
            if (surplus[ability] < amount || Array.IndexOf(surplus, surplus[ability]) != ability) continue;
            surplus[ability] -= amount;
            bool fits = FitsSingleTypes(surplus, requirements, index + 1);
            surplus[ability] += amount;
            if (fits) return true;
        }
        return false;
    }
}
