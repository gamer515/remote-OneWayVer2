using System;
using System.Linq;
using UnityEngine;
using static Constants;

public partial class DecisionManager
{
    private static readonly string[] TaxAbilities = { "힘", "속도", "지능", "매력" };
    private ChapterTaxSettings taxRules;
    private int TaxDay => mineRules?.dayCycle is ChapterDayCycle cycle
        ? Mathf.FloorToInt(mine.elapsedSeconds / cycle.DayLength) : 0;

    private void UpdateTaxSchedule()
    {
        if (taxRules == null || mineRules.dayCycle == null) return;
        int defeated = saveService.DefeatedTaxBosses;
        int interval = taxRules.Interval(defeated);
        mine.taxScheduleTier = taxRules.Tier(defeated);
        if (mine.nextTaxDay > 0 && mine.taxScheduleInterval == interval) return;
        int nextDay = TaxDay + interval;
        if (mine.nextTaxDay > 0 && interval < mine.taxScheduleInterval)
            nextDay = Mathf.Min(nextDay, mine.nextTaxDay);
        mine.taxScheduleInterval = interval;
        mine.nextTaxDay = nextDay;
    }

    private void TickTaxSchedule(float daylight)
    {
        if (taxRules == null || mineCollectors == null || mineRules.dayCycle == null) return;
        UpdateTaxSchedule();
        if (mine.taxPatrolActive)
        {
            if (mineCollectors.Finished)
            {
                mine.taxPatrolActive = false;
                SaveGuideCheckpoint();
            }
            return;
        }
        if (daylight < 1f || TaxDay < mine.nextTaxDay) return;

        if (!mine.HasTaxInvoice)
            mine.taxInvoice = taxRules.Announce(session.RunSeed, TaxDay, mine.taxScheduleTier, statContainer.stats);
        mine.nextTaxDay = TaxDay + mine.taxScheduleInterval;
        mine.taxPatrolActive = true;
        mineCollectors.BeginRoute();
        SaveGuideCheckpoint();
    }

    private static string DescribeTaxCost(int[] cost) =>
        string.Join(" + ", cost.Select((amount, index) => (amount, index))
            .Where(item => item.amount > 0).Select(item => $"{TaxAbilities[item.index]} {item.amount}"));

    private void ShowMineTax(string feedback = null)
    {
        var invoice = mine.taxInvoice;
        var lines = invoice.lines.Select(line => line.label.Replace('·', '/') + " / " +
            (!line.IsFlexible ? DescribeTaxCost(line.cost) :
                line.kind == "single" ? $"한 종류 {line.coins}" : $"혼합 가능 {line.coins}"));
        string bill = $"세금 걷을 시간이다.\n고지 / {invoice.authority}\n{string.Join("\n", lines)}\n합계 {invoice.Total}개";
        if (!string.IsNullOrEmpty(feedback)) bill += "\n" + feedback;
        ShowTaxChoices(bill, new[] { "납부하기", "매력으로 설득", "결투" }, slot =>
        {
            if (slot == 1) TryPersuadeTaxCollector();
            else if (slot == 2) BeginTaxDuel();
            else BeginTaxPayment(invoice);
        });
    }

    private void BeginTaxPayment(MineTaxInvoice invoice)
    {
        var cost = invoice.FixedCost();
        var wallet = statContainer.stats;
        int single = invoice.Single, mixed = invoice.Mixed;
        if (!AbilityPayment.CanPay(wallet, cost) ||
            wallet.Select((amount, index) => amount - cost[index]).Sum() < single + mixed ||
            !wallet.Where((amount, index) => amount - cost[index] >= single).Any())
        {
            ShowMineTax("납부에 필요한 코인이 부족합니다.");
            return;
        }
        if (single > 0 || mixed > 0) ChooseTaxCoin(cost, single, mixed);
        else CommitTaxPayment(cost);
    }

    private void ChooseTaxCoin(int[] cost, int single, int mixed, string feedback = null)
    {
        int amount = single > 0 ? single : 1;
        string prompt = single > 0
            ? $"자유세 / 한 종류를 골라 {single}개 배정하세요."
            : $"인두세 / 종류를 섞을 수 있습니다. 남은 코인 {mixed}개.";
        if (feedback != null) prompt += "\n" + feedback;
        var choices = TaxAbilities.Select((name, index) =>
            $"{name} {amount}개 배정 (사용 가능 {statContainer.stats[index] - cost[index]}개)")
            .Concat(new[] { "고지서로 돌아가기" }).ToArray();
        ShowTaxChoices(prompt, choices, slot =>
        {
            if (slot == TaxAbilities.Length) { ShowMineTax(); return; }
            if (statContainer.stats[slot] - cost[slot] < amount)
            {
                ChooseTaxCoin(cost, single, mixed, "이 종류의 코인이 부족합니다.");
                return;
            }
            cost[slot] += amount;
            int remainingMixed = single > 0 ? mixed : mixed - 1;
            if (remainingMixed > 0) ChooseTaxCoin(cost, 0, remainingMixed);
            else ShowTaxChoices($"총 {cost.Sum()}개 / {DescribeTaxCost(cost)}",
                new[] { "이대로 납부", "고지서로 돌아가기" },
                confirm => { if (confirm == 0) CommitTaxPayment(cost); else ShowMineTax(); });
        });
    }

    private void CommitTaxPayment(int[] cost)
    {
        if (!mine.HasTaxInvoice || !AbilityPayment.TryPay(statContainer.stats, cost, out var after))
        {
            if (mine.HasTaxInvoice) ShowMineTax("납부에 필요한 코인이 부족합니다.");
            return;
        }
        statContainer.SetStats(after);
        guideProgress.committedStats = after;
        mine.collected += cost.Sum();
        SyncAbilityWallet();
        FinishMineTax();
        if (after[0] <= 0) FinishRunAndReturnToMainMenu("징수 후 체력 0");
    }

    private void TryPersuadeTaxCollector()
    {
        bool success = MineChapterSettings.Roll(session.RunSeed, mine.persuasionAttempts, "persuade") <
            mineRules.PersuasionChance(statContainer.stats[3], mine.persuasionAttempts++);
        if (success) FinishMineTax();
        else
        {
            SaveGuideCheckpoint();
            ShowMineTax("설득에 실패했습니다.");
        }
    }

    private void BeginTaxDuel()
    {
        if (duelMiniGame == null) { ShowMineTax("결투를 시작할 수 없습니다."); return; }
        presentationController.ExitChoice();
        taxSelected = null;
        currentState = StoryState.Duel;
        playerController.FocusNpc(mineCollectors.NearestMember(playerController.CurrentPosition), true);
        duelMiniGame.BeginDuel(won =>
        {
            duelMiniGame.CleanupDuel();
            if (won) FinishMineTax();
            else FinishRunAndReturnToMainMenu("징수원 결투 패배");
        }, mineRules.duelHitTarget, statContainer.stats[0], ConsumeMineDuelHealth);
    }
}
