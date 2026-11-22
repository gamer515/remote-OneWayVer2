using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Constants;

/// <summary>Initial_01의 진행은 지문 순서가 아닌 배치된 오브젝트가 결정합니다.</summary>
public partial class DecisionManager
{
    private EncounterFlowController encounterFlow;
    private EncounterContentRepository encounterContent;
    private EncounterFlowController.Encounter? pendingEncounter;
    private readonly HashSet<string> resolvedPlaceIds = new HashSet<string>(StringComparer.Ordinal);
    private string activePlaceId;
    private string activeEncounterPath;
    private ScenarioData activeCards;
    private int activeCardIndex;
    private int selectedGearIndex = -1;
    private int arrivalCardIndex;
    private string arrivalStoryPath;
    private bool walkingForwardJourney;
    private float roadsideWalkZ;
    private Transform roadsideActor;
    private string roadsideEncounterId;
    private enum RoadsideStep { None, Opening, Offer, PickCoinFace, Playing, Result }
    private RoadsideStep roadsideStep;
    private const string RoadsideDemand = "여긴 못 지나가. 지나가려면 돈을 내놔.";

    private void StartEncounterEpisode()
    {
        try { encounterFlow = new EncounterFlowController(envController.TerrainData, envController.PlaceRegistry, envController.CurrentTerrainEndZ); }
        catch (ArgumentException error)
        {
            Debug.LogError($"{currentScenarioPath} 조우 데이터 오류: {error.Message}", this);
            currentState = StoryState.Transitioning;
            return;
        }
        encounterContent = new EncounterContentRepository(session.RunNumber);
        resolvedPlaceIds.Clear();
        if (loadedProgress?.resolvedPlaceIds != null)
            foreach (string id in loadedProgress.resolvedPlaceIds)
                if (!string.IsNullOrWhiteSpace(id)) resolvedPlaceIds.Add(id);

        pendingEncounter = null;
        roadsideStep = RoadsideStep.None;
        if (currentScenarioPath == "Chapter_1/Chapter_1_BossPreview" &&
            Authored?.nodeId?.StartsWith(BossEntry, StringComparison.Ordinal) == true)
        {
            PresentAuthoredBoss();
            return;
        }
        activePlaceId = loadedProgress?.activePlaceId;
        activeCardIndex = loadedProgress?.activeCardIndex ?? 0;
        string phase = loadedProgress?.interactionPhase;
        if ((phase == "prompt" || phase == "card" || phase == "walking" || phase == "tutorial_result") &&
            !resolvedPlaceIds.Contains(activePlaceId) &&
            encounterFlow.TryGetEncounter(activePlaceId, out var encounter))
        {
            pendingEncounter = encounter;
            if (phase != "card" && string.IsNullOrEmpty(loadedProgress?.activeStoryPath)) BeginEncounter(encounter);
            else
            {
                restoringStoryCard = true;
                ApproachEncounter(encounter, activeCardIndex, loadedProgress?.activeStoryPath);
            }
        }
        else if (!TryStartRunIntro()) BeginExploration();
    }

    private void HandleEncounterYellowPressed()
    {
        if (journeyCoinSupply?.HasBoardPayment == true) return;
        if (collectingTax) { ConfirmMineTaxChoice(); return; }
        if (mine?.resting == true) return;
        if (guideProgress?.allocating == true) { ConfirmGuideAllocation(); return; }
        if (lastMinePlace != null) { ConfirmMineRevisit(); return; }
        if ((currentState == StoryState.Exploring || currentState == StoryState.ShowingStory ||
             currentState == StoryState.WaitingForChoice) && learningStation?.ResumeDeferredReading() == true)
        {
            uiController?.ShowReading(learningStation, true);
            return;
        }
        switch (currentState)
        {
            case StoryState.Exploring:
                if (IsPackStudyPaused)
                {
                    uiController.SetStoryNotice(null);
                    Authored.storyPackFrame = PackRoot;
                    PresentStoryPack();
                }
                else BeginWalkStep();
                break;
            case StoryState.ShowingStory: AdvanceEncounterCard(); break;
            case StoryState.WaitingForChoice:
                if (selectedGearIndex >= 0) AdvanceEncounterCard();
                break;
        }
    }

    private void BeginExploration()
    {
        packShortcutWalking = false;
        if (mine != null) mine.shortcutPursuitInvoiceId = null;
        packShortcutActor = null;
        roadsideStep = RoadsideStep.None;
        roadsideActor = null;
        roadsideEncounterId = null;
        lastMinePlace = null;
        RestoreStoryNpcFacing(activePlaceId);
        playerController?.ResetNpcFraming();
        if (guideProgress != null) guideProgress.activeDialogue = null;
        pendingEncounter = null;
        activePlaceId = null;
        activeEncounterPath = null;
        activeCards = null;
        selectedGearIndex = -1;
        currentState = StoryState.Exploring;
        journeyBoardInput?.SetChoiceCount(0);
        ResetGearSelection();
        bettingButtonController?.SetBettingInteractable(false);
        SetYellowInputInteractable(true);
        presentationController.ExitChoice();
        presentationController.ShowWalkingView();
        SaveEncounterState("explore");
    }

    private void BeginWalkStep()
    {
        mineView?.Hide();
        float startZ = playerController.CurrentPosition.z;
        // 한 번 누르면 다음 이벤트 오브젝트까지 계속 걷습니다. 그 사이 청크는 EnvController가 스트리밍합니다.
        float endZ = encounterFlow.EpisodeEndZ;
        if (currentScenarioPath == MineAuthoredStory.Episode && Authored != null)
        {
            var missing = envController.TerrainData.places.FirstOrDefault(place =>
                (place.placeId.StartsWith("MAIN_", StringComparison.Ordinal) ||
                 place.placeId.StartsWith("MAIN-", StringComparison.Ordinal)) && !resolvedPlaceIds.Contains(place.placeId));
            if (missing != null && encounterFlow.TryGetEncounter(missing.placeId, out var required) &&
                required.WorldPosition.z < startZ - .01f)
            {
                ApproachEncounter(required);
                return;
            }
        }
        pendingEncounter = null;
        if (encounterFlow.TryFindFirst(startZ, endZ, resolvedPlaceIds, out var first))
        {
            ApproachEncounter(first, journeyWalk: true);
            return;
        }
        if (startZ >= endZ - .01f) { CompleteEncounterEpisode(); return; }
        currentState = StoryState.MovingToEncounter;
        SetYellowInputInteractable(false);
        presentationController.ShowWalkingView();
        var destination = new Vector3(envController.TerrainOrigin.x, playerController.CurrentPosition.y, endZ);
        walkingForwardJourney = true;
        roadsideWalkZ = startZ;
        float speedMultiplier = GetJourneySpeedMultiplier();
        if (currentScenarioPath == MineAuthoredStory.Episode && envController.AuthoredMine != null)
            playerController.MoveToZ(endZ, speedMultiplier);
        else
            playerController.MoveAlongPath(envController.NpcPlacement != null
                ? envController.NpcPlacement.Path(playerController.CurrentPosition, destination) : new[] { destination }, speedMultiplier);
        SaveEncounterState("walking");
    }

    private float GetJourneySpeedMultiplier()
    {
        const int speedCoinIndex = 1;
        int speedCoins = journeyCoinSupply?.IsReady == true
            ? journeyCoinSupply.AvailableCoins(speedCoinIndex) : statContainer.stats[speedCoinIndex];
        return DecisionPlayerController.JourneySpeedMultiplier(speedCoins, GetStudySkillLevel("mobility"));
    }

    private int GetStudySkillLevel(string skillId) =>
        guideProgress?.mineChapter?.learning?.skills?.Find(skill => skill.id == skillId)?.value ?? 0;

    private void CompleteWalkStep()
    {
        if (TryCompletePackShortcutWalk()) return;
        if (pendingEncounter.HasValue)
        {
            var encounter = pendingEncounter.Value;
            playerController.StopAndLookAt(encounter.WorldPosition);
            FaceNpcTowardPlayer(encounter.PlaceId);
            OpenEncounterCards(encounter, arrivalCardIndex, arrivalStoryPath);
        }
        else if (playerController.CurrentPosition.z >= encounterFlow.EpisodeEndZ - 0.01f)
            CompleteEncounterEpisode();
        else BeginExploration();
    }

    private bool TryOpenRoadsideEncounter()
    {
        if (currentScenarioPath != MineAuthoredStory.Episode || envController.AuthoredMine == null ||
            encounterFlow == null || currentState != StoryState.MovingToEncounter || !walkingForwardJourney ||
            collectingTax || mine?.resting == true || playerController == null) return false;

        Vector3 position = playerController.CurrentPosition;
        float z = position.z;
        if (z <= roadsideWalkZ) return false;
        // Keep the scheduled location encounter if both would trigger at its stop.
        float endZ = Mathf.Min(z, playerController.TargetPosition.z - .01f);
        var actor = envController.FindRoadsideNpc(position.x, roadsideWalkZ, endZ, resolvedPlaceIds, out var id);
        roadsideWalkZ = z;
        if (actor == null) return false;

        BeginExploration();
        roadsideActor = actor;
        roadsideEncounterId = id;
        roadsideStep = RoadsideStep.Opening;
        ShowAuthoredCard(RoadsideDemand, "길가의 사람", null);
        return true;
    }

    private void ShowRoadsideOffer(string speech = RoadsideDemand)
    {
        roadsideStep = RoadsideStep.Offer;
        ShowAuthoredCard(speech, "길가의 사람", new[]
        {
            "동전 던지기 한 판 할래요?", "저랑 한 판 겨뤄 볼래요?", "그냥 큰길로 돌아가자."
        });
    }

    private void AdvanceRoadsideEncounter()
    {
        if (roadsideStep == RoadsideStep.Opening)
        {
            ShowRoadsideOffer();
            return;
        }
        if (roadsideStep == RoadsideStep.Result)
        {
            ResumeAfterRoadsideEncounter();
            return;
        }
        var card = activeCards.MainStory[activeCardIndex];
        int selected = selectedGearIndex;
        if (selected < 0 || selected >= card.options.Length) return;
        presentationController.ConfirmChoice(card.options[selected]);
        if (roadsideStep == RoadsideStep.PickCoinFace)
        {
            bool heads = selected == 0;
            if (tutorialMiniGames == null || !tutorialMiniGames.IsReady)
            {
                ShowRoadsideOffer("지금은 동전을 던질 준비가 안 됐어.");
                return;
            }
            BeginRoadsideGame();
            bool won = MineChapterSettings.Roll(session.RunSeed, 0, roadsideEncounterId + ":coin") < .5;
            bool resultHeads = won ? heads : !heads;
            RecordRoadsideGame(false, won);
            tutorialMiniGames.StartRoadsideCoinToss(resultHeads, won, uiController, interrupted =>
            {
                if (roadsideStep != RoadsideStep.Playing) return;
                if (interrupted) ResumeAfterRoadsideEncounter();
                else ShowRoadsideGameResult(won);
            });
        }
        else if (selected == 0)
        {
            roadsideStep = RoadsideStep.PickCoinFace;
            ShowAuthoredCard("앞면이랑 뒷면 중 뭘 고를래?", "길가의 사람", new[] { "앞면", "뒷면" });
        }
        else if (selected == 1)
        {
            if (duelMiniGame == null)
            {
                ShowRoadsideOffer("지금은 겨룰 준비가 안 됐어.");
                return;
            }
            BeginRoadsideGame();
            FrameActiveEncounter(true);
            duelMiniGame.BeginDuel(won => FinishRoadsideGame(true, won),
                mineRules.duelHitTarget, statContainer.stats[0], ConsumeMineDuelHealth);
        }
        else ResumeAfterRoadsideEncounter();
    }

    private void BeginRoadsideGame()
    {
        roadsideStep = RoadsideStep.Playing;
        currentState = StoryState.Duel;
        SetYellowInputInteractable(false);
        presentationController.ExitChoice();
        // Checkpoint before launching so a reload cannot replay a spent encounter.
        resolvedPlaceIds.Add(roadsideEncounterId);
        SaveEncounterState("explore");
    }

    private void FinishRoadsideGame(bool duel, bool won)
    {
        if (roadsideStep != RoadsideStep.Playing) return;
        RecordRoadsideGame(duel, won);
        ShowRoadsideGameResult(won);
    }

    private void RecordRoadsideGame(bool duel, bool won)
    {
        var card = activeCards.MainStory[activeCardIndex];
        var result = NewStoryEvent(roadsideEncounterId + ":result", duel ? "duel_result" : "gamble_result", card);
        result.placeId = roadsideEncounterId;
        RecordMiniGameResult(result, won);
    }

    private void ShowRoadsideGameResult(bool won)
    {
        roadsideStep = RoadsideStep.Result;
        ShowAuthoredCard(won ? "이번엔 네가 이겼네." : "이번엔 내가 이겼어.", "길가의 사람", null);
    }

    private void ResumeAfterRoadsideEncounter()
    {
        resolvedPlaceIds.Add(roadsideEncounterId);
        BeginExploration();
        BeginWalkStep();
    }

    private void BeginEncounter(EncounterFlowController.Encounter encounter)
    {
        ApproachEncounter(encounter);
        Debug.Log($"[조우] {encounter.PlaceId} ({encounter.ContentPath})", this);
    }

    private void ApproachEncounter(EncounterFlowController.Encounter encounter, int index = 0, string storyPath = null,
        bool journeyWalk = false)
    {
        roadsideWalkZ = playerController.CurrentPosition.z;
        pendingEncounter = encounter; arrivalCardIndex = index; arrivalStoryPath = storyPath;
        activePlaceId = encounter.PlaceId; activeCardIndex = index; activeEncounterPath = storyPath;
        var placement = envController.NpcPlacement;
        var target = placement != null ? placement.ApproachPoint(encounter.WorldPosition, envController.TerrainOrigin, encounterStopDistance)
            : encounter.WorldPosition - Vector3.forward * encounterStopDistance;
        currentState = StoryState.MovingToEncounter; SetYellowInputInteractable(false); presentationController.ShowWalkingView();
        bool authored = encounter.ContentPath.StartsWith(MineAuthoredStory.Prefix, StringComparison.Ordinal);
        if (authored)
        {
            var place = envController.TerrainData.places.FirstOrDefault(entry => entry.placeId == encounter.PlaceId);
            float offset = place?.authoredLocationId != null ? place.storyLocationDepth.x - encounterStopDistance : 0f;
            float targetZ = encounter.WorldPosition.z + offset;
            walkingForwardJourney = journeyWalk && targetZ > playerController.CurrentPosition.z;
            float speedMultiplier = walkingForwardJourney ? GetJourneySpeedMultiplier() : 1f;
            playerController.MoveToZ(targetZ, speedMultiplier);
        }
        else
        {
            walkingForwardJourney = journeyWalk && target.z > playerController.CurrentPosition.z;
            float speedMultiplier = walkingForwardJourney ? GetJourneySpeedMultiplier() : 1f;
            playerController.MoveAlongPath(placement != null
                ? placement.Path(playerController.CurrentPosition, target) : new[] { target }, speedMultiplier);
        }
        SaveEncounterState("walking");
    }

    private void RestoreStoryNpcFacing(string placeId) => SetStoryNpcFacing(placeId, null);

    private bool SetStoryNpcFacing(string placeId, string speaker)
    {
        if (string.IsNullOrEmpty(placeId)) return false;
        var place = envController?.TerrainData?.places?.FirstOrDefault(item => item.placeId == placeId);
        if (place?.storyActors == null) return false;
        var root = envController.FindStoryPlace(place);
        if (root == null) return true;
        foreach (var actor in place.storyActors)
        {
            var socket = root.Find("ActorSocket_" + actor.name);
            if (socket == null) continue;
            socket.localRotation = actor.idleRotation;
            if (speaker != actor.name) continue;
            Vector3 direction = playerController.CurrentPosition - socket.position;
            direction.y = 0;
            if (direction.sqrMagnitude > .0001f) socket.rotation = Quaternion.LookRotation(direction);
        }
        return true;
    }

    private void FaceNpcTowardPlayer(string placeId)
    {
        if (playerController == null || !playerController.IsAvailable || string.IsNullOrWhiteSpace(placeId))
            return;
        if (SetStoryNpcFacing(placeId, activeCards?.MainStory?.ElementAtOrDefault(activeCardIndex)?.speakerId))
            return;

        string objectName = $"Place_{StoryVisualId(placeId)}";
        foreach (Transform candidate in FindObjectsByType<Transform>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (candidate.name != objectName) continue;
            bool sockets = false;
            foreach (Transform actor in candidate)
            {
                if (!actor.name.StartsWith("ActorSocket_", StringComparison.Ordinal)) continue;
                sockets = true;
                Vector3 direction = playerController.CurrentPosition - actor.position; direction.y = 0;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    float variation = (float)MineChapterSettings.Roll(session.RunSeed, 0, placeId + ":" + actor.name);
                    float angle = Mathf.Lerp(-26f, 26f, variation);
                    actor.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(0, angle, 0);
                }
            }
            if (!sockets)
            {
                Vector3 direction = playerController.CurrentPosition - candidate.position; direction.y = 0;
                if (direction.sqrMagnitude > 0.0001f) candidate.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
            return;
        }
    }

    private bool ExecuteStoryChoice(Dialogue card, bool boardPaid)
    {
        if (card.choiceActions == null || card.choiceActions.Length == 0)
        {
            presentationController.ConfirmChoice(card.options[selectedGearIndex]);
            return false;
        }
        StoryChoiceAction choice = card.choiceActions[selectedGearIndex];
        // A restored, confirmed exchange must continue even after its required item was consumed.
        bool confirmed=storyEvents.Exists(e=>e.eventKey=="choice:"+storyVisit && e.choiceSlot==selectedGearIndex);
        if (!confirmed && (!saveService.HasAllUnlocks(choice.requiresUnlocks) || !saveService.HasAllItems(choice.requiresItems)))
        {
            RenderEncounterOptions();
            return true;
        }
        if (!confirmed && !AbilityPayment.CanPay(statContainer.stats, choice.abilityCost))
        {
            presentationController?.ConfirmChoice(card.options[selectedGearIndex]);
            if (!string.IsNullOrEmpty(choice.insufficientStoryPath)) OpenStoryBranch(choice.insufficientStoryPath);
            else BeginInsufficientPaymentDuel(card);
            return true;
        }
        if (!confirmed && !boardPaid && choice.abilityCost?.Any(amount => amount > 0) == true)
        {
            bool locationPayment = pendingEncounter.HasValue && pendingEncounter.Value.PlaceId == activePlaceId;
            if (TryBeginBoardPayment(choice.abilityCost, () => AdvanceEncounterCard(true), () =>
                {
                    presentationController.ShowDialogue(card);
                    journeyBoardInput?.SetChoiceCount(card.options.Length);
                    RenderEncounterOptions();
                }, useNpcBowl: locationPayment)) return true;
            if (locationPayment)
            {
                uiController.SetStoryNotice("납부통을 사용할 수 없습니다.");
                return true;
            }
        }
        RecordStoryChoice(card, choice);
        int relationshipDelta = choice.relationshipDelta *
            (statContainer.stats.Length == 4 && statContainer.stats[3] == 0 ? 2 : 1);
        saveService.ApplyEncounterEffect(
            $"visit:{storyVisit}:choice:{selectedGearIndex}",
            choice.grantsUnlocks, choice.relationshipId, relationshipDelta);
        var selected = storyEvents.FindLast(e => e.eventKey == "choice:" + storyVisit);
        var interactions = saveService.ApplyNarrativeInteractions(selected);
        foreach (var interaction in interactions) presentationController.ShowInteraction(interaction.notice, selected.characterId);
        if (choice.abilityCost != null && statContainer.stats[0] <= 0)
        {
            FinishRunAndReturnToMainMenu("체력 0으로 사망");
            return true;
        }
        switch (choice.action)
        {
            case "skip": ResolveEncounter(); return true;
            case "story":
                string nextPath=storyEvents.FindLast(e=>e.eventKey=="choice:"+storyVisit)?.resultStoryPath;
                OpenStoryBranch(string.IsNullOrEmpty(nextPath)?choice.storyPath:nextPath); return true;
            default: return false;
        }
    }

    private void StartDuelTutorial(Dialogue card)
    {
        FrameActiveEncounter(true);
        currentState = StoryState.Duel;
        SetYellowInputInteractable(false);
        presentationController.ExitChoice();
        if (duelMiniGame == null)
        {
            Debug.LogError("DuelMiniGameBridge가 연결되지 않았습니다.", this);
            OpenStoryBranch(card.errorStoryPath);
            return;
        }
        var resultRecord = NewStoryEvent("duel_result:" + storyVisit, "duel_result", card);
        duelMiniGame.BeginDuel(
            won =>
            {
                RecordMiniGameResult(resultRecord, won);
                OpenStoryBranch(won ? card.winStoryPath : card.loseStoryPath);
            },
            card.duelHitTarget > 0 ? card.duelHitTarget : 3,
            coinDropController.RemainingCoins[0], ConsumeDuelHealthCoin);
    }

    private int ConsumeDuelHealthCoin()
    {
        coinDropController.TryConsumeCoin(0); // Health 재고이며 StatContainer의 별도 능력치 값은 변경하지 않습니다.
        journeyCoinSupply?.RefreshDisplay();
        saveService.SaveRemainingCoins(coinDropController.RemainingCoins);
        return coinDropController.RemainingCoins[0];
    }

    private void StartCoinTutorial(bool choseHeads, Dialogue card)
    {
        currentState = StoryState.Transitioning;
        SetYellowInputInteractable(false);
        presentationController.ExitChoice();
        if (tutorialMiniGames == null)
        {
            Debug.LogError("TutorialMiniGameController가 연결되지 않았습니다.", this);
            OpenStoryBranch(card.errorStoryPath);
            return;
        }
        var resultRecord = NewStoryEvent("gamble_result:" + storyVisit, "gamble_result", card);
        resultRecord.choiceId = choseHeads ? "heads" : "tails";
        tutorialMiniGames.StartCoinToss(choseHeads, (won, heads) =>
        {
            RecordMiniGameResult(resultRecord, won);
            OpenStoryBranch(won ? card.winStoryPath : card.loseStoryPath);
        });
    }

    private void OpenStoryBranch(string storyPath)
    {
        if (pendingEncounter.HasValue) OpenEncounterCards(pendingEncounter.Value, 0, storyPath);
    }

    private void OpenEncounterCards(EncounterFlowController.Encounter encounter, int index, string storyPath = null)
    {
        if (encounter.ContentPath.StartsWith(MineAuthoredStory.Prefix, StringComparison.Ordinal))
        {
            OpenStoryPack(encounter);
            return;
        }
        activePlaceId = encounter.PlaceId;
        activeEncounterPath = string.IsNullOrWhiteSpace(storyPath) ? encounter.ContentPath : storyPath;
        if (string.IsNullOrWhiteSpace(storyPath)) activeEncounterPath = MineOpeningPath(encounter.PlaceId, activeEncounterPath);
        if (string.IsNullOrWhiteSpace(storyPath))
            activeEncounterPath = LocalStoryNotebook.ResolveOpeningPath(SaveIOService.Instance,encounter.DisplayName,activeEncounterPath);
        activeEncounterPath=ChapterContentCatalog.ResolveStoryPath(activeEncounterPath,session.RunNumber);
        int[] stats = statContainer.stats;
        bool isGuide = encounter.DisplayName == "Guide";
        var pinned = restoringStoryCard && guideProgress?.activeDialogue?.placeId == activePlaceId ? guideProgress.activeDialogue : null;
        if (!encounterContent.TryLoadCards(activeEncounterPath, out activeCards, out string error,
                !isGuide && stats.Length == 4 && stats[2] == 0,
                !isGuide && stats.Length == 4 && stats[3] == 0, pinned) ||
            activeCards.MainStory.Count == 0)
        {
            Debug.LogError($"[조우 카드] {encounter.PlaceId}: {error}", this);
            ResolveEncounter();
            return;
        }
        guideProgress.activeDialogue = encounterContent.Capture(activePlaceId, activeEncounterPath, activeCards);
        activeCardIndex = Mathf.Clamp(index, 0, activeCards.MainStory.Count - 1);
        PresentEncounterCard();
    }

    private void PresentEncounterCard()
    {
        if (!restoringStoryCard) storyVisit++;
        restoringStoryCard = false;
        Dialogue card = activeCards.MainStory[activeCardIndex];
        // ResetSelection은 기어 선택 이벤트를 즉시 호출하므로 이전 Choice 상태부터 해제합니다.
        currentState = StoryState.Transitioning;
        SetYellowInputInteractable(false);
        selectedGearIndex = -1;
        journeyBoardInput?.SetChoiceCount(0);
        ResetGearSelection();
        bettingButtonController?.SetBettingInteractable(false);
        RecordPlayedEncounterStory(card, activePlaceId, activeEncounterPath, activeCardIndex);
        SaveEncounterState("card");
        SaveGuideCheckpoint();
        presentationController.ShowDialogue(card);
        FrameActiveEncounter(false);
        CompleteEncounterCardPresentation(card);
    }

    private void CompleteEncounterCardPresentation(Dialogue card)
    {
        if (card.guideAction != null) { StartCoroutine(RunGuideAction(card)); return; }
        if (!string.IsNullOrWhiteSpace(card.startAction))
        {
            switch (card.startAction)
            {
                case "duel": StartDuelTutorial(card); break;
                case "coin_heads": StartCoinTutorial(true, card); break;
                case "coin_tails": StartCoinTutorial(false, card); break;
            }
            return;
        }
        if (card.IsChoice)
        {
            currentState = StoryState.WaitingForChoice;
            // 선택 저장 후 분기 이동 전에 종료된 경우 같은 선택을 이어 갑니다.
            StoryEventRecord confirmed = storyEvents?.FindLast(record => record.eventKey == "choice:" + storyVisit);
            if (confirmed != null && confirmed.choiceSlot >= 0 && confirmed.choiceSlot < card.options.Length)
            {
                selectedGearIndex = confirmed.choiceSlot;
                AdvanceEncounterCard();
                return;
            }
            selectedGearIndex = 0;
            journeyBoardInput?.SetChoiceCount(card.options.Length);
            RenderEncounterOptions();
        }
        else
        {
            currentState = StoryState.ShowingStory;
            presentationController.ExitChoice();
        }
        SetYellowInputInteractable(true);
    }

    private void AdvanceEncounterCard(bool boardPaid = false)
    {
        if (roadsideStep != RoadsideStep.None)
        {
            AdvanceRoadsideEncounter();
            return;
        }
        if (activeEncounterPath == MineAuthoredStory.Prefix + "Boss")
        {
            AdvanceAuthoredBoss();
            return;
        }
        if (activeEncounterPath?.StartsWith(MineAuthoredStory.Prefix, StringComparison.Ordinal) == true)
        {
            AdvanceStoryPack();
            return;
        }
        if (currentState == StoryState.WaitingForChoice)
        {
            Dialogue card = activeCards.MainStory[activeCardIndex];
            Debug.Log($"[카드 선택] {activePlaceId}: {card.options[selectedGearIndex]}", this);
            if (ExecuteStoryChoice(card, boardPaid)) return;
        }
        Dialogue current = activeCards.MainStory[activeCardIndex];
        if (TryExecuteMineAction(current)) return;
        if (!string.IsNullOrWhiteSpace(current.nextStoryPath)) { OpenStoryBranch(current.nextStoryPath); return; }
        activeCardIndex++;
        if (activeCardIndex >= activeCards.MainStory.Count && !string.IsNullOrEmpty(mine?.pendingOpeningPath))
        {
            string next = mine.pendingOpeningPath; mine.pendingOpeningPath = null; OpenStoryBranch(next);
        }
        else if (activeCardIndex >= activeCards.MainStory.Count) ResolveEncounter();
        else PresentEncounterCard();
    }

    private void HandleEncounterGearSelection(int index)
    {
        if (journeyCoinSupply?.HasBoardPayment == true) return;
        if (collectingTax)
        {
            taxSelection = index >= 0 && index < (taxOptions?.Length ?? 0) ? index : -1;
            presentationController.ShowOptions(taxOptions, taxSelection);
            return;
        }
        if (guideProgress?.allocating == true)
        {
            // 기존 기어의 좌상/우상은 추가, 좌하/우하는 빼기입니다.
            if (index >= 0 && index < 4) guideProgress.allocationMode = index == 0 || index == 2 ? 1 : -1;
            ShowGuideAllocation();
            SaveGuideCheckpoint();
            return;
        }
        if (lastMinePlace != null)
        {
            selectedGearIndex = index;
            RenderMineRevisit();
            return;
        }
        if (encounterFlow == null || currentState != StoryState.WaitingForChoice) return;
        selectedGearIndex = index >= 0 && index < (activeCards?.MainStory?[activeCardIndex]?.options?.Length ?? 0) ? index : -1;
        RenderEncounterOptions();
    }

    private void RenderEncounterOptions()
    {
        if (currentState != StoryState.WaitingForChoice || activeCards?.MainStory == null ||
            activeCardIndex < 0 || activeCardIndex >= activeCards.MainStory.Count) return;
        Dialogue card = activeCards.MainStory[activeCardIndex];
        if (card == null || !card.IsChoice || card.options == null || card.options.Length < 2) return;
        string[] options = (string[])card.options.Clone();
        if (card.choiceActions != null && card.choiceActions.Length == card.options.Length)
            for (int index = 0; index < card.options.Length; index++)
            {
                if (!saveService.HasAllUnlocks(card.choiceActions[index].requiresUnlocks) || !saveService.HasAllItems(card.choiceActions[index].requiresItems))
                    options[index] += " (잠김)";
                var cost = card.choiceActions[index].abilityCost;
                if (cost != null)
                {
                    string[] labels = { "힘", "속도", "지능", "매력" };
                    for (int stat = 0; stat < cost.Length; stat++)
                        if (cost[stat] > 0) options[index] += $" ({labels[stat]} -{cost[stat]})";
                    if (!AbilityPayment.CanPay(statContainer.stats, cost)) options[index] += " (능력 부족)";
                }
            }
        presentationController.ShowOptions(options, selectedGearIndex);
        int first = selectedGearIndex >= 0 ? Mathf.Max(0, selectedGearIndex - 3) : 0;
        bool hasPackHints = envController.AuthoredMine != null && activeEncounterPath != null &&
            (activeEncounterPath.StartsWith(MineAuthoredStory.Prefix + "MAIN-", StringComparison.Ordinal) ||
             activeEncounterPath.StartsWith(MineAuthoredStory.Prefix + "SIDE-", StringComparison.Ordinal));
        uiController.SetChoiceHints(hasPackHints ? new Func<int, string>(PackChoiceHint) : null,
            first, Mathf.Min(4, options.Length - first));
    }

    private void ResolveEncounter()
    {
        duelMiniGame?.CleanupDuel();
        tutorialMiniGames?.Cleanup();
        FinishEncounterAndResume(showRevisit: pendingEncounter.HasValue);
    }

    private void FinishEncounterAndResume(bool showRevisit)
    {
        string placeId = activePlaceId;
        if (!string.IsNullOrEmpty(placeId))
        {
            resolvedPlaceIds.Add(placeId);
            SaveGuideCheckpoint();
            saveService?.MarkEncounterCompleted(placeId);
            try
            {
                LocalStoryDialogueQueue.EncounterEnded(SaveIOService.Instance, session.RunNumber, placeId);
            }
            catch (Exception e)
            {
                LocalStoryLog.Write(LocalStoryLogStage.Deferred, "장소 대사 생성 보류: " + e.Message, this);
            }
        }

        uiController?.SetStoryNotice(null);
        BeginExploration();
        if (showRevisit) ShowMineRevisit(placeId);
        LocalStoryRuntimeService.TryResumePending(recover: false);
        LocalStoryNotebook.Refresh(SaveIOService.Instance);
    }

    private void CompleteEncounterEpisode()
    {
        if (currentScenarioPath == MineAuthoredStory.Episode && envController.AuthoredMine != null &&
            !AuthoredBossUnlocked())
        {
            BeginExploration();
            presentationController.ShowInteraction("필수 사건과 펫 전언을 모두 마쳐야 합니다.", "안내");
            return;
        }
        var chapter = currentOmnibus.chapters[chapterIndex];
        if (currentScenarioPath == "Chapter_1/Chapter_1_BossPreview" && Authored != null && envController.AuthoredMine != null)
        {
            PresentAuthoredBoss();
            return;
        }
        if (episodeIndex == chapter.episodeIds.Count - 1 && chapter.bossStage > 0)
        {
            RelayCompletedEpisode();
            SaveEncounterState("battle");
            EnterFinalBattle("챕터 보스 도착", chapter.bossStage, chapter.chapterId);
            return;
        }
        if (episodeIndex == chapter.episodeIds.Count - 1 && chapter.stopAtEnd)
        {
            SaveEncounterState("walking");
            currentState = StoryState.Transitioning;
            SetYellowInputInteractable(false);
            Debug.Log($"[Chapter] {chapter.chapterId} 콘텐츠의 현재 끝에 도착했습니다.", this);
            return;
        }
        if (chapter.episodeIds[episodeIndex] == chapter.previewEndEpisodeId)
        {
            FinishRunAndReturnToMainMenu("보스 미리보기 완료");
            return;
        }
        encounterFlow = null;
        episodeIndex++;
        storyIndex = 0; // 다음 레거시 에피소드에만 적용됩니다.
        FinishCurrentEpisode();
    }

    private void SaveEncounterState(string phase) =>
        saveService?.SaveEncounterProgress(
            session,
            playerController.CurrentPosition,
            playerController.CurrentRotation,
            phase, activePlaceId, activeCardIndex, resolvedPlaceIds, activeEncounterPath);

    private void FrameActiveEncounter(bool fullBody)
    {
        if (packShortcutActor != null && IsPackShortcutNpcFrame)
        {
            playerController.FocusNpc(packShortcutActor, fullBody);
            playerController.StopAndLookAt(packShortcutActor.position);
            return;
        }
        if (roadsideActor != null && roadsideStep != RoadsideStep.None)
        {
            playerController.FocusNpc(roadsideActor, fullBody);
            playerController.StopAndLookAt(roadsideActor.position);
            return;
        }
        if (playerController == null || string.IsNullOrEmpty(activePlaceId)) return;
        string speaker = activeCards?.MainStory?[activeCardIndex]?.speakerId;
        var actor = FindEncounterActor(activePlaceId, speaker);
        if (actor == null) return;
        FaceNpcTowardPlayer(activePlaceId);
        playerController.FocusNpc(actor, fullBody);
        playerController.StopAndLookAt(actor.position);
    }

    private Transform FindEncounterActor(string placeId, string speaker)
    {
        placeId = StoryVisualId(placeId);
        var root = Array.Find(FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
            transform => transform.name == "Place_" + placeId);
        var actor = string.IsNullOrEmpty(speaker) ? null : root?.Find("ActorSocket_" + speaker);
        return actor != null && actor.GetComponentInChildren<Renderer>() != null ? actor : root;
    }

    private string StoryVisualId(string placeId) => envController?.TerrainData?.places
        ?.FirstOrDefault(place => place.placeId == placeId)?.sharedStoryVisualId ?? placeId;
}
