using System;
using System.Linq;
using UnityEngine;
using static Constants;

public partial class DecisionManager
{
    private static readonly string[] TaxAbilities = { "힘", "속도", "지능", "매력" };
    private ChapterTaxSettings taxRules;
    private Action commitBoardPayment, cancelBoardPayment;

    private bool TryBeginBoardPayment(int[] cost, Action commit, Action cancel)
    {
        if (journeyCoinSupply?.BeginBoardPayment(cost, backpackInventory?.StudyGrid) != true) return false;
        commitBoardPayment = commit;
        cancelBoardPayment = cancel;
        SetYellowInputInteractable(false);
        journeyBoardInput?.SetChoiceCount(0);
        presentationController.ExitChoice();
        presentationController.ShowInteraction($"필요 코인 {DescribeTaxCost(cost)}\n코인통에서 꺼내 초록 보드에 올리세요.\nEsc 취소", "납부");
        return true;
    }

    private void FinishBoardPayment() => StartCoroutine(CompleteBoardPayment());

    private System.Collections.IEnumerator CompleteBoardPayment()
    {
        yield return journeyCoinSupply.AbsorbBoardPayment(uiController.StoryScreenCenter);
        uiController.FlashCoinPayment();
        var commit = commitBoardPayment;
        commitBoardPayment = cancelBoardPayment = null;
        SetYellowInputInteractable(true);
        commit?.Invoke();
    }

    private void CancelBoardPayment()
    {
        journeyCoinSupply.ClearBoardPayment();
        var cancel = cancelBoardPayment;
        commitBoardPayment = cancelBoardPayment = null;
        SetYellowInputInteractable(true);
        cancel?.Invoke();
    }
    private int TaxDay => mineRules?.dayCycle is ChapterDayCycle cycle
        ? cycle.DayIndex(mine.elapsedSeconds) : 0;

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
        if (mine.taxPatrolActive && mineCollectors.Finished)
        {
            mine.taxPatrolActive = false;
            mine.collectorOpeningCompleted = true;
            mine.taxInvoice = null;
            mineCollectors.SetVisible(false);
            SaveGuideCheckpoint();
        }
        bool due = TaxDay >= mine.nextTaxDay;
        if (daylight < 1f || mine.taxPatrolActive || (!due && mine.collectorOpeningCompleted)) return;
        if (envController.LoadedChunkEndZ == null) return;
        var registry = envController.PlaceRegistry;
        float firstChunkZ = registry.GetChunkWorldPosition(0).z;
        float length = registry.GetChunkWorldPosition(1).z - firstChunkZ;
        int lastChunk = Mathf.Max(0, Mathf.RoundToInt((envController.CurrentTerrainEndZ - firstChunkZ) / length) - 1);
        int chunk = Mathf.Clamp(Mathf.FloorToInt((playerController.CurrentPosition.z - firstChunkZ) / length), 0, lastChunk);
        if (!mine.HasTaxInvoice)
            mine.taxInvoice = taxRules.Announce(session.RunSeed, TaxDay, mine.taxScheduleTier, statContainer.stats);
        mine.nextTaxDay = TaxDay + mine.taxScheduleInterval;
        mine.taxPatrolActive = true;
        mineCollectors.BeginRoute(registry.GetChunkWorldPosition(chunk).z, registry.GetChunkWorldPosition(chunk + 1).z, chunk);
        SaveGuideCheckpoint();
    }

    private static string DescribeTaxCost(int[] cost) =>
        string.Join(" + ", cost.Select((amount, index) => (amount, index))
            .Where(item => item.amount > 0).Select(item => $"{TaxAbilities[item.index]} {item.amount}"));

    private static string AnnounceCollectorTaxes(MineTaxInvoice invoice)
    {
        string names = string.Join(", ", invoice.lines.Select(line => line.label.Replace('·', '/')));
        return $"오늘 걷는 세금은 {names}다.";
    }

    private void ShowCitizenInspection(string feedback = null)
    {
        int minimum = mineCollectors.InspectionMinimum;
        var wallet = statContainer.stats;
        var missing = wallet.Select((amount, index) => (index, count: minimum - amount))
            .Where(item => item.count > 0).ToArray();
        string criteria = $"{mineCollectors.ChunkNumber}번 청크 / 힘·속도·지능·매력 각 {minimum}개";
        if (missing.Length == 0)
        {
            presentationController.ShowInteraction("이 구간의 기준을 충족했군. 기준세 없이 지나가라.", "징수원");
            uiController.SetTaxCriteria(criteria);
            mineCollectors.CompleteInspection();
            SaveGuideCheckpoint();
            collectorPass = StartCoroutine(FinishCollectorPass());
            SetYellowInputInteractable(true);
            return;
        }

        string shortage = string.Join(", ", missing.Select(item => $"{TaxAbilities[item.index]} {item.count}개"));
        var first = missing[0];
        string line = missing.Length == 1
            ? $"{TaxAbilities[first.index]} 코인이 {(first.count == 1 ? "한 닢" : $"{first.count}개")} 모자라군."
            : $"{shortage} 모자라군.";
        line += " 여기서 기준세 한 닢을 내라.";
        if (!string.IsNullOrEmpty(feedback)) line += "\n" + feedback;
        if (wallet.Any(amount => amount > 0))
        {
            var choices = TaxAbilities.Select((name, index) => $"{name} 1개 납부 (보유 {wallet[index]}개)").ToArray();
            ShowTaxChoices(line, choices, slot => CommitCitizenTax(slot));
        }
        else
        {
            ShowTaxChoices(line + "\n납부할 코인이 없으면 결투로 결정한다.",
                new[] { "결투" }, _ => BeginTaxDuel(inspection: true));
        }
        uiController.SetTaxCriteria(criteria + "\n부족 / " + shortage);
    }

    private void CommitCitizenTax(int ability, bool boardPaid = false)
    {
        if (!collectingTax || mine.collectorReturning || mine.collectorPassedPlayer) return;
        var cost = new int[4];
        cost[ability] = 1;
        if (!AbilityPayment.TryPay(statContainer.stats, cost, out var after))
        {
            ShowCitizenInspection("이 종류의 코인이 부족합니다.");
            return;
        }
        if (!boardPaid && TryBeginBoardPayment(cost, () => CommitCitizenTax(ability, true), () => ShowCitizenInspection()))
            return;
        statContainer.SetStats(after);
        guideProgress.committedStats = after;
        SyncAbilityWallet();
        mineCollectors.CompleteInspection();
        FinishCollectorDialogue();
        if (after[0] <= 0) FinishRunAndReturnToMainMenu("기준세 납부 후 체력 0");
    }

    private void ShowMineTax(string feedback = null)
    {
        var invoice = mine.taxInvoice;
        var lines = invoice.lines.Select(line => line.label.Replace('·', '/') + " / " +
            (!line.IsFlexible ? DescribeTaxCost(line.cost) :
                line.kind == "single" ? $"한 종류 {line.coins}" : $"혼합 가능 {line.coins}"));
        string bill = $"따라잡았다. 이번 순찰의 고지서를 확인하고 세금을 내라.\n{AnnounceCollectorTaxes(invoice)}\n고지 / {invoice.authority}\n{string.Join("\n", lines)}\n합계 {invoice.Total}개";
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

    private void CommitTaxPayment(int[] cost, bool boardPaid = false)
    {
        if (mine.collectorChaseClosed) return;
        if (!mine.HasTaxInvoice || !AbilityPayment.TryPay(statContainer.stats, cost, out var after))
        {
            if (mine.HasTaxInvoice) ShowMineTax("납부에 필요한 코인이 부족합니다.");
            return;
        }
        if (!boardPaid && TryBeginBoardPayment(cost, () => CommitTaxPayment(cost, true), () => ShowMineTax())) return;
        statContainer.SetStats(after);
        guideProgress.committedStats = after;
        mine.collected += cost.Sum();
        SyncAbilityWallet();
        FinishMineTax();
        if (after[0] <= 0) FinishRunAndReturnToMainMenu("징수 후 체력 0");
    }

    private void TryPersuadeTaxCollector()
    {
        int attempts = mine.persuasionAttempts++;
        bool success = MineChapterSettings.Roll(session.RunSeed, attempts, "persuade") <
            mineRules.PersuasionChance(statContainer.stats[3], attempts, GetStudySkillLevel("persuasion"));
        if (success) FinishMineTax();
        else
        {
            SaveGuideCheckpoint();
            ShowMineTax("설득에 실패했습니다.");
        }
    }

    private void BeginTaxDuel(bool inspection = false)
    {
        if (duelMiniGame == null)
        {
            if (inspection) ShowCitizenInspection("결투를 시작할 수 없습니다.");
            else ShowMineTax("결투를 시작할 수 없습니다.");
            return;
        }
        presentationController.ExitChoice();
        taxSelected = null;
        currentState = StoryState.Duel;
        playerController.FocusNpc(mineCollectors.NearestMember(playerController.CurrentPosition), true);
        duelMiniGame.BeginDuel(won =>
        {
            duelMiniGame.CleanupDuel();
            if (!won) FinishRunAndReturnToMainMenu("징수원 결투 패배");
            else if (inspection)
            {
                mineCollectors.CompleteInspection();
                FinishCollectorDialogue();
            }
            else FinishMineTax();
        }, mineRules.duelHitTarget, statContainer.stats[0], ConsumeMineDuelHealth);
    }
}
