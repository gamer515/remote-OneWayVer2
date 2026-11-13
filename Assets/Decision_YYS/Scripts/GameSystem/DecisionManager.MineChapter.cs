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
    private string MineStoryPrefix => currentScenarioPath + "/Encounters/";
    private MineChapterSettings mineRules;
    private MineChapterProgress mine;
    private MineChapterActivityView mineView;
    private string lastMinePlace;
    [SerializeField] private GameObject mineCollectorPrefab;
    private MineCollectorProcession mineCollectors;
    private bool collectingTax;
    private float mineSaveAt;
    private StoryState beforeTax;
    private int beforeTaxSelection;
    private int taxWitnessStep;
    private Transform taxWitness;
    private const string MineActorSocketPrefix = "ActorSocket_";
    private string[] taxOptions;
    private Action<int> taxSelected;
    private int taxSelection = -1;

    private void InitializeMineChapter()
    {
        var chapter = currentOmnibus.chapters[chapterIndex];
        if (string.IsNullOrEmpty(chapter.rulesDocument) ||
            chapter.rulesEpisodes != null && !chapter.rulesEpisodes.Contains(chapter.episodeIds[episodeIndex]))
        {
            mineCollectors?.SetVisible(false);
            StopChapterSky();
            mineRules = null;
            taxRules = null;
            mine = null;
            UpdateChapterCalendar();
            return;
        }
        mineRules = MineChapterSettings.Load(chapter.rulesDocument);
        mine = guideProgress.mineChapter ??= new MineChapterProgress();
        mineRules.dayCycle?.RestoreClock(mine);
        if (mine.taxChapterId != chapter.chapterId)
        {
            mine.taxChapterId = chapter.chapterId;
            mine.nextTaxDay = 0;
            mine.taxPatrolActive = false;
            mine.collectorOpeningCompleted = false;
            mine.collectorReturning = false;
            mine.taxInvoice = null;
        }
        taxRules = ChapterTaxSettings.Load(ChapterContentCatalog.ChapterFolder(chapter.chapterId));
        UpdateTaxSchedule();
        StartChapterSky();
        UpdateChapterSky(mineRules.dayCycle?.Daylight(mine.elapsedSeconds) ?? 1f);
        UpdateChapterCalendar();
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
        var origin = envController.PlaceRegistry.GetChunkWorldPosition(0);
        var npcPositions = envController.TerrainData.places
            .Where(place => !string.IsNullOrEmpty(place.characterId))
            .SelectMany(place => (place.actorPositions ?? new[] { place.position })
                .Select(position => envController.PlaceRegistry.GetChunkWorldPosition(place.chunkIndex) + position));
        var placement = envController.NpcPlacement;
        var doors = mine.variedRoomLayout && placement != null ? placement.Places.Select(placement.Doors) : null;
        float patrolLength = (envController.LoadedChunkEndZ ?? envController.CurrentTerrainEndZ) - origin.z;
        mineCollectors?.Initialize(mineCollectorPrefab, mine, origin,
            patrolLength, session.RunSeed, mineRules.collectorArrivalSeconds,
            mineRules.collectorNpcWaitMin, mineRules.collectorNpcWaitMax, npcPositions, doors);
        mineCollectors?.SetVisible(mineCollectors.IsVisible);
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

    private bool mineLaborInProgress;

    private IEnumerator RunMineActivity(MineChapterAction action, string key)
    {
        bool labor = action.kind == "labor";
        mineLaborInProgress = labor;
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
                var collector = labor && mine.taxPatrolActive && mineCollectors?.Finished == false
                    ? mineCollectors.NearestMember(playerController.CurrentPosition) : null;
                string distance = collector != null
                    ? $"\n징수원 거리 {Vector3.Distance(collector.position, playerController.CurrentPosition):F1}u" : "";
                mineView.Show(labor ? $"요구사항 수행 중 / {mine.activityRemaining:F1}초\n이동과 능력 성장 중지{distance}" :
                    $"경주 / SPACE 연타\n{mine.racePresses}/{mineRules.racePressTarget} / 남은 시간 {mine.activityRemaining:F1}초");
                if (!labor) tutorialMiniGames?.UpdateRaceTrack((float)mine.racePresses / mineRules.racePressTarget, 1 - mine.activityRemaining / mineRules.raceSeconds);
                if (!labor && mine.racePresses >= mineRules.racePressTarget) break;
            }
            yield return null;
        }
        mineLaborInProgress = false;
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
        if (Time.unscaledTime >= mineSaveAt) { mineSaveAt = Time.unscaledTime + 1; SaveGuideCheckpoint(); }
        mine.elapsedSeconds += Time.deltaTime;
        float daylight = mineRules.dayCycle?.Daylight(mine.elapsedSeconds) ?? 1f;
        TickTaxSchedule(daylight);
        UpdateChapterSky(daylight);
        UpdateChapterCalendar();
        if (!collectingTax && daylight > 0 && mine.taxPatrolActive &&
            mineCollectors is { Finished: false } procession)
        {
            var position = playerController.CurrentPosition;
            bool hasCollection = mine.HasTaxInvoice && procession.CanMeet;
            var nearby = hasCollection ? procession.MemberInRange(position, mineRules.encounterDistance) : null;
            if (nearby == null)
            {
                procession.Tick(Time.deltaTime * daylight);
                if (hasCollection) nearby = procession.MemberInRange(position, mineRules.encounterDistance);
            }
            if (CanOpenCollectorDialogue())
            {
                if (nearby != null) { ResolveCollectorContact(nearby); return; }
                var witnessCollector = procession.NearestMember(position);
                if (witnessCollector != null) TryBeginMineWitness(witnessCollector);
            }
        }
        if (!collectingTax && journeyCoinSupply?.HasBoardPayment != true)
        {
            if (mine.resting)
            {
                mine.fatigue = Mathf.Max(0, mine.fatigue - mineRules.recoveryPerSecond * Time.deltaTime);
                string distance = mine.taxPatrolActive ? $"\n징수원 거리 {Mathf.Abs(playerController.CurrentPosition.z - mine.collectorZ):F1}" : "";
                mineView.Show($"강제 휴식 / 피로 {mine.fatigue:F0}{distance}");
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
        }
    }

    private bool CanOpenCollectorDialogue() => journeyCoinSupply?.HasBoardPayment != true && !guideProgress.allocating &&
        currentState != StoryState.Duel && (currentState != StoryState.Transitioning || mineLaborInProgress);

    private void BeginCollectorDialogue(Transform collector, Transform witness = null)
    {
        collectingTax = true; beforeTax = currentState;
        beforeTaxSelection = selectedGearIndex;
        var residents = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(actor => actor.name.StartsWith(MineActorSocketPrefix, StringComparison.Ordinal) &&
                (actor.position - collector.position).sqrMagnitude < 36f)
            .Select(actor => actor.position).ToArray();
        mineCollectors.ArrangeForDialogue(playerController.CurrentPosition, residents, witness?.position);
        playerController.FocusNpc(collector, false);
        playerController.PauseAndLookAt(collector.position);
        currentState = StoryState.Transitioning; SetYellowInputInteractable(false);
        mineView.Hide();
        presentationController.ExitChoice();
        presentationController.ShowPlayerViewControl();
        journeyBoardInput?.SetChoiceCount(0);
    }

    private void ResolveCollectorContact(Transform collector)
    {
        var invoice = mine.taxInvoice;
        if (invoice.CanPass(statContainer.stats, out int available, out int largest))
        {
            BeginCollectorDialogue(collector);
            presentationController.ShowInteraction(DescribeCollectorPass(invoice, available, largest), "징수원");
            StartCoroutine(FinishCollectorPass());
            return;
        }
        BeginCollectorDialogue(collector);
        ShowMineTax();
    }

    private IEnumerator FinishCollectorPass()
    {
        yield return new WaitForSecondsRealtime(5f);
        FinishMineTax();
    }

    private void TryBeginMineWitness(Transform collector)
    {
        if (mine.collectorNpcPause <= 0 ||
            (mine.collectorNpcPauseIndex == mine.collectorNpcWitnessIndex &&
             mine.collectorNpcActorIndex == mine.collectorNpcWitnessActor) ||
            mineCollectors.CurrentNpcPosition is not Vector3 point ||
            Vector3.Distance(playerController.CurrentPosition, point) > mineRules.encounterDistance) return;
        var actor = Array.Find(FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
            target => target.name.StartsWith(MineActorSocketPrefix, StringComparison.Ordinal) &&
                      (target.position - point).sqrMagnitude < .001f);
        if (actor == null) return;

        BeginCollectorDialogue(collector, actor);
        taxWitness = actor;
        mine.collectorNpcWitnessIndex = mine.collectorNpcPauseIndex;
        mine.collectorNpcWitnessActor = mine.collectorNpcActorIndex;
        taxWitnessStep = 1;
        presentationController.ShowInteraction("너도 세금을 걷어 간다. 세금을 내라.", "징수원");
        SetYellowInputInteractable(true);
    }
    private void ShowTaxChoices(string message, string[] choices, Action<int> selected)
    {
        mineView.Hide();
        taxOptions = choices; taxSelected = selected; taxSelection = 0;
        presentationController.ShowPlayerViewControl();
        presentationController.ShowInteraction(message, "징수원");
        journeyBoardInput?.SetChoiceCount(choices.Length);
        presentationController.ShowOptions(taxOptions, taxSelection);
        SetYellowInputInteractable(true);
    }
    private void ConfirmMineTaxChoice()
    {
        if (taxWitnessStep == 1)
        {
            taxWitnessStep = 2;
            presentationController.ShowInteraction("또 내라고? 젠장 끝도 없구만.", taxWitness.name.Substring(MineActorSocketPrefix.Length));
            playerController.FocusNpc(taxWitness, false);
            return;
        }
        if (taxWitnessStep == 2)
        {
            FinishCollectorDialogue(false);
            return;
        }
        if(taxSelected == null || taxSelection < 0 || taxSelection >= taxOptions.Length) return;
        var selected = taxSelected; int slot = taxSelection;
        taxSelected = null;
        presentationController.ConfirmChoice(taxOptions[slot]);
        selected(slot);
    }
    private void FinishMineTax()
    {
        mine.taxInvoice = null;
        FinishCollectorDialogue(true);
    }

    private void FinishCollectorDialogue(bool playerTaxSettled)
    {
        taxWitnessStep = 0; taxWitness = null;
        taxOptions = null; taxSelected = null; taxSelection = -1;
        journeyBoardInput?.SetChoiceCount(0);
        presentationController.ExitChoice();
        collectingTax = false;
        mineCollectors.RestoreDialoguePositions();
        if (playerTaxSettled) mineCollectors.ContinueForward();
        mineView.Hide(); currentState = beforeTax;
        if (currentState == StoryState.MovingToEncounter && !mine.resting) playerController.ResumeMovement();
        SetYellowInputInteractable(currentState == StoryState.Exploring || currentState == StoryState.ShowingStory || currentState == StoryState.WaitingForChoice);
        SaveGuideCheckpoint();
        presentationController.ShowWalkingView();
        if (currentState == StoryState.ShowingStory || currentState == StoryState.WaitingForChoice)
        {
            var card = activeCards.MainStory[activeCardIndex];
            presentationController.ShowDialogue(card);
            FrameActiveEncounter(false);
            if (currentState == StoryState.WaitingForChoice)
            {
                selectedGearIndex = beforeTaxSelection;
                journeyBoardInput?.SetChoiceCount(card.options.Length, selectedGearIndex);
                RenderEncounterOptions();
            }
        }
        else if (mineLaborInProgress) FrameActiveEncounter(false);
        else
        {
            playerController.ResetNpcFraming();
            if (lastMinePlace != null) ShowMineRevisit(lastMinePlace, beforeTaxSelection);
        }
        Debug.Log($"[징수] 누적 {mine.collected}, 향후 보스 공격 배율 {mineRules.BossAttackMultiplier(mine.collected):F2}. 보스전 적용은 후속 범위.");
    }

    private static readonly string[] MineRevisitOptions = { "다시 말 걸기", "다음 장소로 이동" };

    private void ShowMineRevisit(string place, int selection = 0)
    {
        if (mine == null) return;
        lastMinePlace = place;
        selectedGearIndex = selection;
        presentationController.ShowPlayerViewControl();
        presentationController.ShowInteraction("이 장소의 대화가 끝났습니다.", "안내");
        journeyBoardInput?.SetChoiceCount(MineRevisitOptions.Length, selection);
        RenderMineRevisit();
    }

    private void RenderMineRevisit() => presentationController.ShowOptions(MineRevisitOptions, selectedGearIndex);

    private void ConfirmMineRevisit()
    {
        string place = lastMinePlace;
        int slot = selectedGearIndex;
        lastMinePlace = null;
        journeyBoardInput?.SetChoiceCount(0);
        presentationController.ExitChoice();
        if (slot == 0 && encounterFlow.TryGetEncounter(place, out var encounter))
        {
            pendingEncounter = encounter;
            var visit = mine.visits.First(v => v.placeId == place);
            string key = visit.openingPath.Substring(MineStoryPrefix.Length);
            string revisit = key + "_revisit";
            string returnPath = visit.granted && !string.IsNullOrEmpty(visit.returnStoryPath) ? visit.returnStoryPath : visit.openingPath;
            if (!visit.granted && ChapterContentCatalog.Info(MineStoryPrefix + key + "_refuse_retry") != null) returnPath = MineStoryPrefix + key + "_refuse_retry";
            if (visit.paidCharm && visit.granted && ChapterContentCatalog.Info(MineStoryPrefix + revisit) != null) returnPath = MineStoryPrefix + revisit;
            OpenEncounterCards(encounter, 0, returnPath);
        }
        else BeginWalkStep();
    }
    private void LateUpdate() => mineCollectors?.SetVisible(mine != null && mineCollectors.IsVisible);

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
