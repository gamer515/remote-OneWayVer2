using System;
using System.Linq;
using UnityEngine;
using static Constants;

public partial class DecisionManager
{
    [SerializeField] private TaxScaleView taxScaleView;
    private static readonly string[] TaxAbilities = { "힘", "속도", "지능", "매력" };
    private ChapterTaxSettings taxRules;
    private Action commitBoardPayment, cancelBoardPayment;
    private bool collectorBoardPayment;
    private int[] npcBoardPaymentCost;
    private Action resumeNpcPaymentAfterTax;

    private bool TryBeginBoardPayment(int[] cost, Action commit, Action cancel, bool useNpcBowl = false,
        bool collectorPayment = false, bool useTaxPan = false)
    {
        if (journeyCoinSupply == null || journeyCoinSupply.HasBoardPayment || (useNpcBowl && !CanShowNpcPaymentBowl))
            return false;
        if (!journeyCoinSupply.BeginBoardPayment(cost, StudyGrid, useNpcBowl ? NpcPaymentBowlOpening : null,
            useNpcBowl ? QueueNpcPaymentCoin : null, useTaxPan))
            return false;
        StopNpcPaymentPresentation();
        npcBoardPaymentCost = useNpcBowl ? (int[])cost.Clone() : null;
        if (useNpcBowl) BeginNpcPaymentPresentation();
        commitBoardPayment = commit;
        cancelBoardPayment = cancel;
        collectorBoardPayment = collectorPayment;
        SetYellowInputInteractable(false);
        journeyBoardInput?.SetChoiceCount(0);
        presentationController.ExitChoice();
        string placement = useNpcBowl ? "코인을 꺼내 나무 통 안에 넣으세요." : "코인통에서 꺼내 초록 보드에 올리세요.";
        if (useTaxPan) placement = "오른쪽 접시에 올린 코인을 먼저 사용합니다. 부족한 코인은 초록 보드에 올리세요.";
        presentationController.ShowInteraction($"필요 코인 {DescribeTaxCost(cost)}\n{placement}\nEsc 취소", "납부");
        return true;
    }

    private void SuspendNpcPaymentForCollector()
    {
        var cost = npcBoardPaymentCost;
        var commit = commitBoardPayment;
        var cancel = cancelBoardPayment;
        resumeNpcPaymentAfterTax = () =>
        {
            if (AbilityPayment.CanPay(statContainer.stats, cost) &&
                TryBeginBoardPayment(cost, commit, cancel, useNpcBowl: true)) return;
            cancel?.Invoke();
            uiController.SetStoryNotice("징수 후 납부에 필요한 코인을 다시 확인해 주세요.");
        };
        commitBoardPayment = cancelBoardPayment = null;
        npcBoardPaymentCost = null;
        StopNpcPaymentPresentation();
        journeyCoinSupply.ClearBoardPayment();
    }

    private void FinishBoardPayment() => StartCoroutine(CompleteBoardPayment());

    private System.Collections.IEnumerator CompleteBoardPayment()
    {
        var commit = commitBoardPayment;
        if (commit == null) yield break;
        commitBoardPayment = cancelBoardPayment = null;
        npcBoardPaymentCost = null;
        if (collectorBoardPayment)
        {
            collectorBoardPayment = false;
            CollectBoardTaxPayment(commit);
            yield break;
        }
        if (npcPaymentRoot != null)
        {
            journeyCoinSupply.ClearBoardPayment(keepBowlCoins: true);
            npcPaymentConfirmed = true;
        }
        else
        {
            yield return journeyCoinSupply.AbsorbBoardPayment(uiController.StoryScreenCenter);
            uiController.FlashCoinPayment();
        }
        SetYellowInputInteractable(true);
        commit.Invoke();
    }

    private void CancelBoardPayment()
    {
        StopNpcPaymentPresentation();
        journeyCoinSupply.ClearBoardPayment();
        var cancel = cancelBoardPayment;
        commitBoardPayment = cancelBoardPayment = null;
        collectorBoardPayment = false;
        npcBoardPaymentCost = null;
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
        if (mine.taxInvoice == null)
            mine.taxInvoice = taxRules.Announce(session.RunSeed, TaxDay, mine.taxScheduleTier, statContainer.stats,
                ChapterContentCatalog.ChapterFolder(mine.taxChapterId) == "Chapter_01");
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
        CloseTaxScalePreparation();
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
        if (!boardPaid && TryBeginBoardPayment(cost, () => CommitCitizenTax(ability, true), () => ShowCitizenInspection(),
            collectorPayment: true))
            return;
        statContainer.SetStats(after);
        guideProgress.committedStats = after;
        SyncAbilityWallet();
        mineCollectors.CompleteInspection();
        PresentSettledTaxPayment(() =>
        {
            FinishCollectorDialogue();
            if (after[0] <= 0) FinishRunAndReturnToMainMenu("기준세 납부 후 체력 0");
        });
    }

    private void ShowMineTax(string feedback = null)
    {
        var quote = new MineTaxBill(mine.taxInvoice, mine.taxArrears);
        OpenTaxScalePreparation(quote.Current);
        string notice = DescribeTaxNotice(quote);
        var fixedCost = quote.FixedCost();
        taxScaleView?.ShowNotice(fixedCost, quote.FlexibleTotal, notice);
        string bill = $"따라잡았다. 이번 순찰의 고지서를 확인하고 세금을 내라.\n{AnnounceCollectorTaxes(quote.Current)}\n{notice}";
        if (taxScaleView != null) bill += "\n오른쪽 접시에 코인을 올려 납부를 준비할 수 있습니다.";
        var wallet = statContainer.stats;
        bool normalPayment = quote.CanAllocate(wallet, fixedCost, 0, quote.Mixed);
        bool alternatePayment = false;
        if (!normalPayment)
        {
            alternatePayment = AlternateTaxAbilities(quote, wallet, out int amount).Length > 0;
            bill += alternatePayment
                ? $"\n대납 / 합계의 {mineRules.alternatePaymentMultiplier}배, 한 종류로 {amount}개"
                : "\n납부에 필요한 코인이 부족합니다.";
        }
        if (!string.IsNullOrEmpty(feedback)) bill += "\n" + feedback;
        string[] responses = { "매력으로 설득", "결투" };
        string[] options = normalPayment || alternatePayment
            ? new[] { normalPayment ? "납부하기" : "다른 능력으로 대납" }.Concat(responses).ToArray()
            : responses;
        ShowTaxChoices(bill, options, slot =>
        {
            if (slot == options.Length - 2) TryPersuadeTaxCollector();
            else if (slot == options.Length - 1) BeginTaxDuel();
            else if (normalPayment) BeginTaxPayment(quote);
            else ChooseAlternateTaxCoin(quote);
        });
    }

    private static string DescribeTaxLines(MineTaxLine[] lines) =>
        string.Join("\n", lines.Select(line => line.label.Replace('·', '/') + " / " +
            (!line.IsFlexible ? DescribeTaxCost(line.cost) :
                line.kind == "single" ? $"한 종류 {line.coins}" : $"혼합 가능 {line.coins}")));

    private static string DescribeTaxNotice(MineTaxBill bill)
    {
        string notice = $"고지 / {bill.Current.authority}\n";
        if (bill.Arrears.Length == 0)
            return notice + $"{DescribeTaxLines(bill.Current.lines)}\n합계 {bill.Total}개";

        notice += $"새 일반세 {bill.Current.Total}개\n{DescribeTaxLines(bill.Current.lines)}";
        notice += $"\n미납 원금 {bill.Principal}개\n" +
            string.Join("\n", bill.Arrears.Select(entry => DescribeTaxLines(entry.invoice.lines)));
        return notice + $"\n벌금 {bill.Penalty}개 / 한 종류 선택\n합계 {bill.Total}개";
    }

    private void ShowAllocatedTaxNotice(MineTaxBill bill, int[] cost, int remaining)
    {
        if (taxScaleView == null) return;
        string text = DescribeTaxNotice(bill);
        if (remaining < bill.FlexibleTotal)
            text += $"\n현재 배정 / {DescribeTaxCost(cost)}";
        taxScaleView.ShowNotice(cost, remaining, text);
    }

    private int[] AlternateTaxAbilities(MineTaxBill bill, int[] wallet, out int amount)
    {
        long total = (long)bill.Total * mineRules.alternatePaymentMultiplier;
        amount = total > 0 && total <= int.MaxValue ? (int)total : 0;
        var fixedCost = bill.FixedCost();
        if (amount == 0 || bill.CanAllocate(wallet, fixedCost, 0, bill.Mixed))
            return Array.Empty<int>();

        int required = amount;
        return Enumerable.Range(0, TaxAbilities.Length)
            .Where(ability => wallet[ability] >= required && wallet[ability] >= fixedCost[ability]).ToArray();
    }

    private void ChooseAlternateTaxCoin(MineTaxBill bill)
    {
        if (mine.collectorChaseClosed || !ReferenceEquals(mine.taxInvoice, bill.Current)) return;
        var candidates = AlternateTaxAbilities(bill, statContainer.stats, out int amount);
        if (candidates.Length == 0)
        {
            ShowMineTax("대납 가능한 코인 종류가 없습니다.");
            return;
        }
        var choices = candidates.Select(ability => $"{TaxAbilities[ability]} {amount}개 대납")
            .Concat(new[] { "고지서로 돌아가기" }).ToArray();
        ShowTaxChoices($"대납 / 고지서 합계 {bill.Total}개 × {mineRules.alternatePaymentMultiplier}배\n한 종류로 {amount}개를 납부합니다.", choices, slot =>
        {
            if (slot == candidates.Length) { ShowMineTax(); return; }
            int ability = candidates[slot];
            var cost = new int[TaxAbilities.Length];
            cost[ability] = amount;
            CommitTaxPayment(cost, alternateAbility: ability);
        });
    }

    private void BeginTaxPayment(MineTaxBill bill)
    {
        if (mine.collectorChaseClosed || !ReferenceEquals(mine.taxInvoice, bill.Current)) return;
        var cost = bill.FixedCost();
        var wallet = statContainer.stats;
        if (!bill.CanAllocate(wallet, cost, 0, bill.Mixed))
        {
            ShowMineTax("납부에 필요한 코인이 부족합니다.");
            return;
        }
        if (bill.FlexibleTotal > 0) ChooseTaxCoin(bill, cost, 0, bill.Mixed);
        else CommitTaxPayment(cost);
    }

    private void ChooseTaxCoin(MineTaxBill bill, int[] cost, int singleIndex, int mixed, string feedback = null)
    {
        if (!ReferenceEquals(bill.Current, mine.taxInvoice)) return;
        ShowAllocatedTaxNotice(bill, cost, bill.RemainingFlexible(singleIndex, mixed));
        var single = bill.SingleLines.ElementAtOrDefault(singleIndex);
        int amount = single?.coins ?? 1;
        string mixedLabel = bill.Arrears.Length == 0
            ? bill.Current.lines.FirstOrDefault(line => line.kind == "mixed")?.label ?? "혼합 가능 세목"
            : "혼합 가능 세목";
        string prompt = single != null
            ? $"{single.label} / 한 종류를 골라 {amount}개 배정하세요."
            : $"{mixedLabel} / 종류를 섞을 수 있습니다. 남은 코인 {mixed}개.";
        if (feedback != null) prompt += "\n" + feedback;
        var choices = TaxAbilities.Select((name, index) =>
            $"{name} {amount}개 배정 (사용 가능 {statContainer.stats[index] - cost[index]}개)")
            .Concat(new[] { "고지서로 돌아가기" }).ToArray();
        ShowTaxChoices(prompt, choices, slot =>
        {
            if (slot == TaxAbilities.Length) { ShowMineTax(); return; }
            if (statContainer.stats[slot] - cost[slot] < amount)
            {
                ChooseTaxCoin(bill, cost, singleIndex, mixed, "이 종류의 코인이 부족합니다.");
                return;
            }
            cost[slot] += amount;
            int nextSingle = single != null ? singleIndex + 1 : singleIndex;
            int remainingMixed = single != null ? mixed : mixed - 1;
            if (!bill.CanAllocate(statContainer.stats, cost, nextSingle, remainingMixed))
            {
                cost[slot] -= amount;
                ChooseTaxCoin(bill, cost, singleIndex, mixed, "다른 세목에 필요한 코인을 남겨 주세요.");
                return;
            }
            if (bill.RemainingFlexible(nextSingle, remainingMixed) > 0)
                ChooseTaxCoin(bill, cost, nextSingle, remainingMixed);
            else
            {
                ShowAllocatedTaxNotice(bill, cost, 0);
                ShowTaxChoices($"총 {cost.Sum()}개 / {DescribeTaxCost(cost)}",
                    new[] { "이대로 납부", "고지서로 돌아가기" },
                    confirm => { if (confirm == 0) CommitTaxPayment(cost); else ShowMineTax(); });
            }
        });
    }

    private void CommitTaxPayment(int[] cost, bool boardPaid = false, int alternateAbility = -1)
    {
        if (mine.collectorChaseClosed || !mine.HasTaxInvoice) return;
        var invoice = mine.taxInvoice;
        var wallet = statContainer.stats;
        if (alternateAbility >= 0 &&
            (!AlternateTaxAbilities(new MineTaxBill(invoice, mine.taxArrears), wallet, out int amount).Contains(alternateAbility) ||
             cost[alternateAbility] != amount))
        {
            ShowMineTax("현재 잔액으로 대납 조건을 다시 확인하세요.");
            return;
        }
        if (!AbilityPayment.TryPay(wallet, cost, out var after))
        {
            ShowMineTax("납부에 필요한 코인이 부족합니다.");
            return;
        }
        if (!boardPaid && TryBeginBoardPayment(cost, () =>
        {
            if (ReferenceEquals(mine.taxInvoice, invoice)) CommitTaxPayment(cost, true, alternateAbility);
        }, () => ShowMineTax(), collectorPayment: true, useTaxPan: taxScaleView != null)) return;
        statContainer.SetStats(after);
        guideProgress.committedStats = after;
        mine.collected += cost.Sum();
        SyncAbilityWallet();
        CloseSettledMineTax();
        PresentSettledTaxPayment(() =>
        {
            FinishCollectorDialogue();
            if (after[0] <= 0) FinishRunAndReturnToMainMenu("징수 후 체력 0");
        });
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
        CloseTaxScalePreparation();
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
