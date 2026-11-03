using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using static Constants;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>First-meeting orchestration. Dialogue, costs, deltas and destinations remain in chapter data.</summary>
public partial class DecisionManager
{
    private const string MineStoryPrefix = "Chapter_1/Chapter_1_01/Encounters/";
    private MineChapterSettings mineRules;
    private MineChapterProgress mine;
    private MineChapterActivityView mineView;
    private string lastMinePlace;
    [SerializeField] private GameObject mineCollectorPrefab;
    private MineCollectorProcession mineCollectors;
    private bool collectingTax;
    private float mineSaveAt;
    private StoryState beforeTax;
    private string[] taxOptions;
    private Action<int> taxSelected;
    private int taxSelection = -1;

    private void InitializeMineChapter()
    {
        if (currentScenarioPath != "Chapter_1/Chapter_1_01") return;
        mineRules ??= MineChapterSettings.Load();
        mine = guideProgress.mineChapter ??= new MineChapterProgress();
        if (!mine.initialized)
        {
            mine.initialized = true;
            Debug.Log("[학습] 학습 대기열과 이동·대화 병행 적용. 전용 성장 미니게임, 나이에 따른 성장·유지, 신분별 통행료 가중치는 후속 작업입니다.");
        }
        if (mineView == null)
        {
            var go = new GameObject("MineChapterActivityView");
            mineView = go.AddComponent<MineChapterActivityView>(); mineView.Initialize(); mineView.Hide();
        }
        SyncAbilityWallet();
        guideProgress.committedStats ??= statContainer.stats;
        mineCollectors ??= FindFirstObjectByType<MineCollectorProcession>();
        mineCollectors?.Initialize(mineCollectorPrefab, mine, envController.PlaceRegistry.GetChunkWorldPosition(0), envController.TerrainData.chunkLength, session.RunSeed);
    }

    private string MineOpeningPath(string place, string fallback)
    {
        if (mine == null) return fallback;
        var saved = mine.visits.FirstOrDefault(v => v.placeId == place);
        if (saved != null) return saved.openingPath;
        var opening = mineRules.openings.FirstOrDefault(o => o.placeId == place);
        if (opening?.variants == null || opening.variants.Length == 0) return fallback;
        int index = (int)(MineChapterSettings.Roll(session.RunSeed, session.RunNumber, place) * opening.variants.Length);
        string path = MineStoryPrefix + opening.variants[index];
        mine.visits.Add(new MinePlaceVisit { placeId = place, openingPath = path });
        SaveGuideCheckpoint();
        if (!string.IsNullOrEmpty(opening.rumor) && MineChapterSettings.Roll(session.RunSeed, session.RunNumber, place + ":rumor") < mineRules.rumorChance)
        {
            mine.pendingOpeningPath = path;
            SaveGuideCheckpoint();
            return MineStoryPrefix + opening.rumor;
        }
        return path;
    }

    private void SyncAbilityWallet()
    {
        if (mine == null || statContainer == null) return;
        coinDropController.InitializeInventory(statContainer.stats);
        journeyCoinSupply?.RefreshDisplay();
    }

    private void ReceiveMinePayment(StoryChoiceAction choice)
    {
        if (mine == null || choice.abilityCost == null || string.IsNullOrWhiteSpace(choice.receiverId)) return;
        var receiver = mine.recipients.FirstOrDefault(n => n.characterId == choice.receiverId);
        if (receiver == null) { receiver = new MineNpcBalance { characterId = choice.receiverId }; mine.recipients.Add(receiver); }
        for (int i = 0; i < 4; i++) receiver.coins[i] += choice.abilityCost[i];
        var visit = mine.visits.FirstOrDefault(v => v.placeId == activePlaceId);
        if (visit != null && choice.abilityCost[3] > 0) visit.paidCharm = true;
        SyncAbilityWallet();
    }

    private bool TryExecuteMineAction(Dialogue card)
    {
        var action = card.chapterAction;
        if (mine == null || string.IsNullOrEmpty(action?.kind)) return false;
        string key = "chapter:" + storyVisit;
        if (action.kind == "access")
        {
            var visit = mine.visits.FirstOrDefault(v => v.placeId == activePlaceId);
            if (visit != null) { visit.granted = action.granted; visit.returnStoryPath = activeEncounterPath; }
            SaveGuideCheckpoint();
            return false;
        }
        if (action.kind == "death") { FinishRunAndReturnToMainMenu("결투 패배"); return true; }
        currentState = StoryState.Transitioning;
        SetYellowInputInteractable(false);
        presentationController.ExitChoice();
        if (action.kind == "labor" || action.kind == "race") { StartCoroutine(RunMineActivity(action, key)); return true; }
        if (action.kind == "duel")
        {
            if (duelMiniGame == null) { ShowMissingMineGame("결투", card); return true; }
            currentState = StoryState.Duel;
            FrameActiveEncounter(true);
            duelMiniGame.BeginDuel(won => {
                duelMiniGame.CleanupDuel();
                RecordMiniGameResult(NewStoryEvent("duel_result:" + storyVisit, "duel_result", card), won);
                OpenStoryBranch(won ? action.winStoryPath : action.loseStoryPath);
            }, mineRules.duelHitTarget, statContainer.stats[0], ConsumeMineDuelHealth);
            return true;
        }
        if (action.kind == "coin_heads" || action.kind == "coin_tails")
        {
            // The verdict is an exact, seeded 50/50 roll. Physics is only the existing visual presentation.
            bool won = MineChapterSettings.Roll(session.RunSeed, storyVisit, key) < 0.5;
            if (tutorialMiniGames == null || !tutorialMiniGames.IsReady) { ShowMissingMineGame("동전 던지기", card); return true; }
            bool choseHeads = action.kind == "coin_heads";
            tutorialMiniGames.StartCoinToss(choseHeads, (_, __) => {
                tutorialMiniGames.Cleanup();
                RecordMiniGameResult(NewStoryEvent("gamble_result:" + storyVisit, "gamble_result", card), won);
                OpenStoryBranch(won ? action.winStoryPath : action.loseStoryPath);
            }, won ? choseHeads : !choseHeads);
            return true;
        }
        ShowMissingMineGame(action.kind, card);
        return true;
    }

    private void ShowMissingMineGame(string name, Dialogue card)
    {
        Debug.LogWarning("[미구현/미연결] " + name + " — 성공/실패를 임의로 처리하지 않습니다.");
        mineView.Show(name + " 준비가 필요합니다.", new[] { "대화로 돌아가기" }, _ => {
            mineView.Hide(); currentState = StoryState.ShowingStory; SetYellowInputInteractable(true);
        });
    }

    private int ConsumeMineDuelHealth()
    {
        var balances = statContainer.stats;
        balances[0] = Mathf.Max(0, balances[0] - 1);
        statContainer.SetStats(balances); guideProgress.committedStats = balances;
        SyncAbilityWallet(); SaveGuideCheckpoint();
        return balances[0];
    }

    private IEnumerator RunMineActivity(MineChapterAction action, string key)
    {
        bool labor = action.kind == "labor";
        if (!labor) tutorialMiniGames?.StartRaceTrack();
        if (mine.activityKey != key)
        {
            bool won = action.guaranteed || MineChapterSettings.Roll(session.RunSeed, storyVisit, key) < mineRules.SuccessChance(statContainer.stats[action.ability]);
            mine.activityKey = key;
            mine.activityRemaining = labor ? mineRules.laborSeconds : mineRules.raceSeconds;
            mine.resultPath = won ? action.winStoryPath : action.loseStoryPath;
            mine.activityDelta = labor ? (won ? action.successDelta : action.failureDelta) : null;
            mine.racePresses = 0;
            SaveGuideCheckpoint();
        }
        while (mine.activityRemaining > 0)
        {
            if (!collectingTax)
            {
                mine.activityRemaining = Mathf.Max(0, mine.activityRemaining - Time.deltaTime);
#if ENABLE_INPUT_SYSTEM
                if (!labor && Keyboard.current?.spaceKey.wasPressedThisFrame == true) mine.racePresses++;
#else
                if (!labor && Input.GetKeyDown(KeyCode.Space)) mine.racePresses++;
#endif
                mineView.Show(labor ? $"요구사항 수행 중 / {mine.activityRemaining:F1}초\n이동과 능력 성장 중지 / 징수원은 접근합니다." :
                    $"경주 / SPACE 연타\n{mine.racePresses}/{mineRules.racePressTarget} / 남은 시간 {mine.activityRemaining:F1}초");
                if (!labor) tutorialMiniGames?.UpdateRaceTrack((float)mine.racePresses / mineRules.racePressTarget, 1 - mine.activityRemaining / mineRules.raceSeconds);
                if (!labor && mine.racePresses >= mineRules.racePressTarget) break;
            }
            yield return null;
        }
        if (!labor) { mine.resultPath = mine.racePresses >= mineRules.racePressTarget ? action.winStoryPath : action.loseStoryPath; tutorialMiniGames?.Cleanup(); }
        string next = mine.resultPath;
        if (!labor) RecordMiniGameResult(NewStoryEvent(key, "race_result", activeCards.MainStory[activeCardIndex]), next == action.winStoryPath);
        if (!mine.completedActions.Contains(key))
        {
            if (labor)
            {
                var record = NewStoryEvent(key, "labor_result", activeCards.MainStory[activeCardIndex]);
                statContainer.SetStats(MineChapterSettings.ApplyDelta(statContainer.stats, mine.activityDelta));
                guideProgress.committedStats = statContainer.stats; SyncAbilityWallet();
                record.reason = next == action.winStoryPath ? "win" : "lose";
                record.statsAfter = statContainer.stats; record.coinsAfter = coinDropController.RemainingCoins;
                storyEvents.Add(record);
            }
            mine.completedActions.Add(key);
        }
        mineView.Hide(); SaveGuideCheckpoint();
        if (statContainer.stats[0] <= 0) { FinishRunAndReturnToMainMenu("노동 후 체력 0"); yield break; }
        OpenStoryBranch(next);
    }

    private void TickMineChapter()
    {
        if (mine == null || playerController == null || finalBattleRequested) return;
        TickLearning();
        if (Time.unscaledTime >= mineSaveAt) { mineSaveAt = Time.unscaledTime + 1; SaveGuideCheckpoint(); }
        if (currentScenarioPath != "Chapter_1/Chapter_1_01") return;
        if (!collectingTax)
        {
            mine.elapsedSeconds += Time.deltaTime;
            mineCollectors?.Tick(Time.deltaTime);
            if (mine.resting)
            {
                mine.fatigue = Mathf.Max(0, mine.fatigue - mineRules.recoveryPerSecond * Time.deltaTime);
                mineView.Show($"강제 휴식 / 피로 {mine.fatigue:F0}\n징수원 거리 {playerController.CurrentPosition.z - mine.collectorZ:F1}");
                if (mine.fatigue <= mineRules.fatigueResume) { mine.resting = false; mineView.Hide(); if (currentState == StoryState.MovingToEncounter) playerController.ResumeMovement(); }
            }
            else if (currentState == StoryState.MovingToEncounter)
            {
                mine.fatigue += mineRules.walkingFatiguePerSecond * Time.deltaTime;
                if (mine.fatigue >= mineRules.fatigueMax)
                {
                    mine.resting = true;
                    playerController.PauseAndLookAt(playerController.CurrentPosition + Vector3.forward);
                    Debug.Log("[에셋 필요] 강제 휴식 애니메이션 미지정. 정지 상태로 회복합니다.");
                }
            }
            var collector = mineCollectors?.NearestMember(playerController.CurrentPosition);
            if (mineCollectors?.CanMeet == true && collector != null &&
                Vector3.Distance(playerController.CurrentPosition, collector.position) <= mineRules.encounterDistance && currentState != StoryState.Duel) BeginMineTax();
        }
    }

    private int MineTaxAmount => mineRules.baseTax + resolvedPlaceIds.Count;
    private void BeginMineTax()
    {
        if (collectingTax) return;
        collectingTax = true; beforeTax = currentState;
        var collector = mineCollectors.NearestMember(playerController.CurrentPosition);
        playerController.PauseAndLookAt(collector.position);
        playerController.FocusNpc(collector, false);
        currentState = StoryState.Transitioning; SetYellowInputInteractable(false);
        ShowMineTax();
    }
    private void ShowMineTax()
    {
        int type = (int)(MineChapterSettings.Roll(session.RunSeed, mine.collected, "tax") * 4);
        string[] labels = { "힘", "속도", "지능", "매력" };
        ShowTaxChoices($"징수원 조우 / {labels[type]} {MineTaxAmount}개 요구\n다른 코인은 {mineRules.alternatePaymentMultiplier}배로 대납", labels.Select((n, i) => n + " " + (MineTaxAmount * (i == type ? 1 : mineRules.alternatePaymentMultiplier))).ToArray(), slot => {
            if (!collectingTax) return;
            var cost = new int[4]; cost[slot] = MineTaxAmount * (slot == type ? 1 : mineRules.alternatePaymentMultiplier);
            if (!AbilityPayment.TryPay(statContainer.stats, cost, out var after)) { ShowMineTaxAlternatives(); return; }
            statContainer.SetStats(after); guideProgress.committedStats = after;
            mine.collected += cost.Sum(); SyncAbilityWallet(); FinishMineTax();
            if (after[0] <= 0) FinishRunAndReturnToMainMenu("징수 후 체력 0");
        });
    }
    private void ShowMineTaxAlternatives()
    {
        ShowTaxChoices("코인이 부족합니다. 대응을 선택하세요.", new[] { "다른 코인으로 납부", "매력으로 설득", "결투" }, slot => {
            if (slot == 0) { ShowMineTax(); return; }
            if (slot == 1)
            {
                bool success = MineChapterSettings.Roll(session.RunSeed, mine.persuasionAttempts, "persuade") < mineRules.PersuasionChance(statContainer.stats[3], mine.persuasionAttempts++);
                SaveGuideCheckpoint();
                if (success) FinishMineTax(); else ShowMineTaxAlternatives();
            }
            else if (duelMiniGame != null)
            {
                presentationController.ExitChoice(); taxSelected = null; currentState = StoryState.Duel;
                playerController.FocusNpc(mineCollectors.NearestMember(playerController.CurrentPosition), true);
                duelMiniGame.BeginDuel(won => { duelMiniGame.CleanupDuel(); if (won) FinishMineTax(); else FinishRunAndReturnToMainMenu("징수원 결투 패배"); }, mineRules.duelHitTarget, statContainer.stats[0], ConsumeMineDuelHealth);
            }
        });
    }
    private void ShowTaxChoices(string message, string[] choices, Action<int> selected)
    {
        mineView.Hide();
        taxOptions = choices; taxSelected = selected; taxSelection = -1;
        ResetGearSelection();
        presentationController.ShowPlayerViewControl();
        presentationController.ShowInteraction(message, "징수원");
        presentationController.ShowOptions(taxOptions, taxSelection);
        SetYellowInputInteractable(true);
    }
    private void ConfirmMineTaxChoice()
    {
        if(taxSelected == null || taxSelection < 0 || taxSelection >= taxOptions.Length) return;
        var selected = taxSelected; int slot = taxSelection;
        taxSelected = null;
        presentationController.ConfirmChoice(taxOptions[slot]);
        selected(slot);
    }
    private void FinishMineTax()
    {
        taxOptions = null; taxSelected = null; taxSelection = -1;
        presentationController.ExitChoice();
        collectingTax = false; mineCollectors.ContinueForward();
        mineView.Hide(); currentState = beforeTax;
        if (currentState == StoryState.MovingToEncounter && !mine.resting) playerController.ResumeMovement();
        SetYellowInputInteractable(currentState == StoryState.Exploring || currentState == StoryState.ShowingStory || currentState == StoryState.WaitingForChoice);
        SaveGuideCheckpoint();
        if (currentState == StoryState.ShowingStory || currentState == StoryState.WaitingForChoice) FrameActiveEncounter(false);
        else playerController.ResetNpcFraming();
        if (currentState == StoryState.WaitingForChoice) RenderEncounterOptions();
        Debug.Log($"[징수] 누적 {mine.collected}, 향후 보스 공격 배율 {mineRules.BossAttackMultiplier(mine.collected):F2}. 보스전 적용은 후속 범위.");
    }

    private void ShowMineRevisit(string place)
    {
        if (mine == null) return;
        lastMinePlace = place;
        mineView.Show("이 장소의 대화가 끝났습니다.", new[] { "다시 말 걸기", "다음 장소로 이동" }, slot => {
            if (collectingTax) return;
            mineView.Hide();
            if (slot == 0 && encounterFlow.TryGetEncounter(lastMinePlace, out var encounter))
            {
                pendingEncounter = encounter;
                var visit = mine.visits.First(v => v.placeId == lastMinePlace);
                string key = visit.openingPath.Substring(MineStoryPrefix.Length);
                string revisit = key + "_revisit";
                string returnPath = visit.granted && !string.IsNullOrEmpty(visit.returnStoryPath) ? visit.returnStoryPath : visit.openingPath;
                if (!visit.granted && ChapterContentCatalog.Info(MineStoryPrefix + key + "_refuse_retry") != null) returnPath = MineStoryPrefix + key + "_refuse_retry";
                if (visit.paidCharm && visit.granted && ChapterContentCatalog.Info(MineStoryPrefix + revisit) != null) returnPath = MineStoryPrefix + revisit;
                OpenEncounterCards(encounter, 0, returnPath);
            }
            else BeginWalkStep();
        });
    }
    private void LateUpdate() => mineCollectors?.SetVisible(currentScenarioPath == "Chapter_1/Chapter_1_01");

    private void BeginInsufficientPaymentDuel(Dialogue card)
    {
        if (mine == null || duelMiniGame == null) return;
        Debug.Log("[잔액 부족] 코인은 차감하지 않고 결투로 전환합니다.");
        currentState = StoryState.Duel; SetYellowInputInteractable(false); presentationController.ExitChoice();
        FrameActiveEncounter(true);
        duelMiniGame.BeginDuel(won => {
            duelMiniGame.CleanupDuel();
            RecordMiniGameResult(NewStoryEvent("shortage_duel:" + storyVisit, "shortage_duel_result", card), won);
            if (!won) { FinishRunAndReturnToMainMenu("잔액 부족 결투 패배"); return; }
            var visit = mine.visits.FirstOrDefault(v => v.placeId == activePlaceId);
            if (visit != null) visit.granted = true;
            SaveGuideCheckpoint(); ResolveEncounter();
        }, mineRules.duelHitTarget, statContainer.stats[0], ConsumeMineDuelHealth);
    }
}
