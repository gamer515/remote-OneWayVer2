using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class MineTaxLevy
{
    public string id, label, kind, condition;
    public bool enabled, mine;
    public int[] cost;
    public int coins, divisor, minimum;
}

[Serializable]
public sealed class MineTaxLine
{
    public string label, kind;
    public int[] cost;
    public int coins;
    public bool IsFlexible => kind == "mixed" || kind == "single";
    public int Total => IsFlexible ? coins : cost.Sum();
}

[Serializable]
public sealed class MineTaxInvoice
{
    public int day, tier;
    public string authority;
    public MineTaxLine[] lines;
    public int Total => lines.Sum(line => line.Total);
    public int Single => lines.Where(line => line.kind == "single").Sum(line => line.coins);
    public int Mixed => lines.Where(line => line.kind == "mixed").Sum(line => line.coins);

    public bool CanPass(int[] wallet) => CanPass(wallet, out _, out _);

    public bool CanPass(int[] wallet, out int available, out int largest)
    {
        int[] cost = FixedCost();
        int single = Single, mixed = Mixed;
        available = 0;
        largest = 0;
        for (int i = 0; i < cost.Length; i++)
        {
            int surplus = wallet[i] - cost[i] - 1;
            if (cost[i] > 0 && surplus < 0) return false;
            available += Mathf.Max(0, surplus);
            largest = Mathf.Max(largest, surplus);
        }
        return largest >= single && available >= single + mixed;
    }

    public int[] FixedCost()
    {
        var result = new int[4];
        foreach (var line in lines)
            if (!line.IsFlexible)
                for (int i = 0; i < result.Length; i++) result[i] += line.cost[i];
        return result;
    }
}

[Serializable]
public sealed class ChapterTaxSettings
{
    public string authority;
    public int minimumLevies, maximumLevies;
    public int[] intervalDays;
    public int maximumBurdenTier = 7;
    public float increasePerTier;
    public MineTaxLevy[] levies;

    public static ChapterTaxSettings Load(string folder)
    {
        string document = folder + "/Taxes";
        var data = ChapterWorkbookContent.Get(document);
        if (data != null) return data.ToObject<ChapterTaxSettings>();
        var asset = Resources.Load<TextAsset>("Chapters/" + document);
        return asset != null ? JsonUtility.FromJson<ChapterTaxSettings>(asset.text) : null;
    }

    public bool IsValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(authority) || intervalDays == null || intervalDays.Length == 0 || intervalDays[0] <= 0 ||
                intervalDays.Where((days, index) => index > 0 && days <= intervalDays[index - 1]).Any() ||
                !float.IsFinite(increasePerTier) || increasePerTier < 0 || increasePerTier > 2 ||
                maximumBurdenTier < 0 || maximumBurdenTier > 20 ||
                minimumLevies < 2 || maximumLevies < minimumLevies || maximumLevies > 3 ||
                levies == null || levies.Any(levy => levy == null || string.IsNullOrWhiteSpace(levy.id) ||
                    string.IsNullOrWhiteSpace(levy.label)) ||
                levies.Select(levy => levy.id).Distinct(StringComparer.Ordinal).Count() != levies.Length) return false;
            var enabled = levies.Where(levy => levy.enabled).ToArray();
            return enabled.Count(levy => levy.kind != "largest") >= maximumLevies &&
                enabled.Count(levy => levy.kind == "single") <= 1 && enabled.All(ValidLevy);
        }
    }

    private static bool ValidLevy(MineTaxLevy levy) => levy.kind switch
    {
        "fixed" => levy.cost?.Length == 4 && levy.cost.All(amount => amount >= 0 && amount <= 1000) && levy.cost.Sum() > 0,
        "mixed" or "single" or "notice" => levy.coins > 0 && levy.coins <= 1000,
        "largest" => levy.divisor > 0 && levy.minimum > 0 && levy.minimum <= 1000,
        _ => false
    };

    public int Tier(int defeatedBosses) => Mathf.Clamp(defeatedBosses, 0, maximumBurdenTier);
    public int Interval(int defeatedBosses) => intervalDays[Mathf.Clamp(defeatedBosses, 0, intervalDays.Length - 1)];

    public MineTaxInvoice Announce(int seed, int day, int tier, int[] wallet)
    {
        var random = new System.Random(unchecked(seed * 397 ^ day));
        var available = levies.Where(levy => levy.enabled && (levy.kind != "largest" || wallet.Any(value => value > 0))).ToList();
        var selected = new List<MineTaxLevy>();
        int count = random.Next(minimumLevies, maximumLevies + 1);
        var minePool = available.Where(levy => levy.mine).ToArray();
        if (minePool.Length > 0)
        {
            var local = minePool[random.Next(minePool.Length)];
            selected.Add(local);
            available.Remove(local);
        }
        while (selected.Count < count)
        {
            int index = random.Next(available.Count);
            selected.Add(available[index]);
            available.RemoveAt(index);
        }
        decimal multiplier = 1m + tier * (decimal)increasePerTier;
        return new MineTaxInvoice
        {
            day = day, tier = tier, authority = authority,
            lines = selected.Select(levy => Quote(levy, random, wallet, multiplier)).ToArray()
        };
    }

    private static MineTaxLine Quote(MineTaxLevy levy, System.Random random, int[] wallet, decimal multiplier)
    {
        var line = new MineTaxLine { label = levy.label, kind = levy.kind };
        if (levy.kind == "mixed" || levy.kind == "single")
        {
            line.coins = (int)Math.Ceiling(levy.coins * multiplier);
            return line;
        }
        int[] cost = levy.cost;
        if (levy.kind == "notice" || levy.kind == "largest")
        {
            cost = new int[4];
            int ability = levy.kind == "notice" ? random.Next(4) : Array.IndexOf(wallet, wallet.Max());
            cost[ability] = levy.kind == "notice" ? levy.coins : Mathf.Max(levy.minimum, wallet[ability] / levy.divisor);
        }

        line.cost = cost.Select(amount => (int)Math.Floor(amount * multiplier)).ToArray();
        int remainder = (int)Math.Ceiling(cost.Sum() * multiplier) - line.cost.Sum();
        foreach (int i in Enumerable.Range(0, cost.Length).Where(i => cost[i] > 0)
                     .OrderByDescending(i => cost[i] * multiplier - line.cost[i]).Take(remainder).ToArray())
            line.cost[i]++;
        return line;
    }
}
