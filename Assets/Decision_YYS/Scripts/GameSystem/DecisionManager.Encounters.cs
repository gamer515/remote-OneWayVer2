using System;
using System.Collections.Generic;
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
        activePlaceId = loadedProgress?.activePlaceId;
        activeCardIndex = loadedProgress?.activeCardIndex ?? 0;
        string phase = loadedProgress?.interactionPhase;
        if ((phase == "prompt" || phase == "card" || phase == "tutorial_result") && encounterFlow.TryGetEncounter(activePlaceId, out var encounter))
        {
            pendingEncounter = encounter;
            if (phase != "card") BeginEncounter(encounter); // 이전 Interaction 체크포인트 호환
            else
            {
                restoringStoryCard = true;
                playerController.StopAndLookAt(encounter.WorldPosition);
                FaceNpcTowardPlayer(encounter.PlaceId);
                OpenEncounterCards(encounter, activeCardIndex, loadedProgress?.activeStoryPath);
            }
        }
        else BeginExploration();
    }

    private void HandleEncounterYellowPressed()
    {
        if (guideProgress?.allocating == true) { ConfirmGuideAllocation(); return; }
        switch (currentState)
        {
            case StoryState.Exploring: BeginWalkStep(); break;
            case StoryState.ShowingStory: AdvanceEncounterCard(); break;
            case StoryState.WaitingForChoice:
                if (selectedGearIndex >= 0) AdvanceEncounterCard();
                break;
        }
    }

    private void BeginExploration()
    {
        pendingEncounter = null;
        activePlaceId = null;
        activeEncounterPath = null;
        activeCards = null;
        selectedGearIndex = -1;
        currentState = StoryState.Exploring;
        ResetGearSelection();
        bettingButtonController?.SetBettingInteractable(false);
        SetYellowInputInteractable(true);
        presentationController.ExitChoice();
        presentationController.ShowWalkingView();
        SaveEncounterState("explore");
    }

    private void BeginWalkStep()
    {
        float startZ = playerController.CurrentPosition.z;
        if (startZ >= encounterFlow.EpisodeEndZ - 0.01f) { CompleteEncounterEpisode(); return; }
        // 한 번 누르면 다음 이벤트 오브젝트까지 계속 걷습니다. 그 사이 청크는 EnvController가 스트리밍합니다.
        float endZ = encounterFlow.EpisodeEndZ;
        pendingEncounter = null;
        if (encounterFlow.TryFindFirst(startZ, endZ, resolvedPlaceIds, out var first))
        {
            pendingEncounter = first;
            endZ = first.WorldPosition.z - encounterStopDistance;
        }
        currentState = StoryState.MovingToEncounter;
        SetYellowInputInteractable(false);
        presentationController.ShowWalkingView();
        playerController.MoveToZ(endZ);
        SaveEncounterState("walking");
    }

    private void CompleteWalkStep()
    {
        if (pendingEncounter.HasValue)
        {
            var encounter = pendingEncounter.Value;
            BeginEncounter(encounter);
        }
        else if (playerController.CurrentPosition.z >= encounterFlow.EpisodeEndZ - 0.01f)
            CompleteEncounterEpisode();
        else BeginExploration();
    }

    private void BeginEncounter(EncounterFlowController.Encounter encounter)
    {
        playerController.StopAndLookAt(encounter.WorldPosition);
        FaceNpcTowardPlayer(encounter.PlaceId);
        OpenEncounterCards(encounter, 0);
        Debug.Log($"[조우] {encounter.PlaceId} ({encounter.ContentPath})", this);
    }

    private void FaceNpcTowardPlayer(string placeId)
    {
        if (playerController == null || !playerController.IsAvailable || string.IsNullOrWhiteSpace(placeId))
            return;

        string objectName = $"Place_{placeId}";
        foreach (Transform candidate in FindObjectsByType<Transform>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (candidate.name != objectName) continue;
            Vector3 direction = playerController.CurrentPosition - candidate.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                candidate.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            return;
        }
    }

    private bool ExecuteStoryChoice(Dialogue card)
    {
        if (card.choiceActions == null || card.choiceActions.Length == 0) return false;
        StoryChoiceAction choice = card.choiceActions[selectedGearIndex];
        if (!saveService.HasAllUnlocks(choice.requiresUnlocks))
        {
            RenderEncounterOptions();
            return true;
        }
        RecordStoryChoice(card, choice);
        int relationshipDelta = choice.relationshipDelta *
            (statContainer.stats.Length == 4 && statContainer.stats[3] == 0 ? 2 : 1);
        saveService.ApplyEncounterEffect(
            $"visit:{storyVisit}:choice:{selectedGearIndex}",
            choice.grantsUnlocks, choice.relationshipId, relationshipDelta);
        switch (choice.action)
        {
            case "skip": ResolveEncounter(); return true;
            case "story": OpenStoryBranch(choice.storyPath); return true;
            default: return false;
        }
    }

    private void StartDuelTutorial(Dialogue card)
    {
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
        activePlaceId = encounter.PlaceId;
        activeEncounterPath = string.IsNullOrWhiteSpace(storyPath) ? encounter.ContentPath : storyPath;
        int[] stats = statContainer.stats;
        bool isGuide = encounter.DisplayName == "Guide";
        if (!encounterContent.TryLoadCards(activeEncounterPath, out activeCards, out string error,
                !isGuide && stats.Length == 4 && stats[2] == 0,
                !isGuide && stats.Length == 4 && stats[3] == 0) ||
            activeCards.MainStory.Count == 0)
        {
            Debug.LogError($"[조우 카드] {encounter.PlaceId}: {error}", this);
            ResolveEncounter();
            return;
        }
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
        ResetGearSelection();
        bettingButtonController?.SetBettingInteractable(false);
        RecordPlayedEncounterStory(card, activePlaceId, activeEncounterPath, activeCardIndex);
        SaveEncounterState("card");
        SaveGuideCheckpoint();
        presentationController.ShowDialogue(card);
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
            if (confirmed != null && confirmed.choiceSlot >= 0 && confirmed.choiceSlot < 4)
            {
                selectedGearIndex = confirmed.choiceSlot;
                AdvanceEncounterCard();
                return;
            }
            RenderEncounterOptions();
        }
        else
        {
            currentState = StoryState.ShowingStory;
            presentationController.ExitChoice();
        }
        SetYellowInputInteractable(true);
    }

    private void AdvanceEncounterCard()
    {
        if (currentState == StoryState.WaitingForChoice)
        {
            Dialogue card = activeCards.MainStory[activeCardIndex];
            Debug.Log($"[카드 선택] {activePlaceId}: {card.options[selectedGearIndex]}", this);
            if (ExecuteStoryChoice(card)) return;
        }
        Dialogue current = activeCards.MainStory[activeCardIndex];
        if (!string.IsNullOrWhiteSpace(current.nextStoryPath)) { OpenStoryBranch(current.nextStoryPath); return; }
        activeCardIndex++;
        if (activeCardIndex >= activeCards.MainStory.Count) ResolveEncounter();
        else PresentEncounterCard();
    }

    private void HandleEncounterGearSelection(int index)
    {
        if (guideProgress?.allocating == true)
        {
            // 기존 기어의 좌상/우상은 추가, 좌하/우하는 빼기입니다.
            if (index >= 0 && index < 4) guideProgress.allocationMode = index == 0 || index == 2 ? 1 : -1;
            ShowGuideAllocation();
            SaveGuideCheckpoint();
            return;
        }
        if (encounterFlow == null || currentState != StoryState.WaitingForChoice) return;
        selectedGearIndex = index >= 0 && index < 4 ? index : -1;
        RenderEncounterOptions();
    }

    private void RenderEncounterOptions()
    {
        if (currentState != StoryState.WaitingForChoice || activeCards?.MainStory == null ||
            activeCardIndex < 0 || activeCardIndex >= activeCards.MainStory.Count) return;
        Dialogue card = activeCards.MainStory[activeCardIndex];
        if (card == null || !card.IsChoice || card.options == null || card.options.Length != 4) return;
        string[] options = (string[])card.options.Clone();
        if (card.choiceActions != null && card.choiceActions.Length == 4)
            for (int index = 0; index < 4; index++)
                if (!saveService.HasAllUnlocks(card.choiceActions[index].requiresUnlocks))
                    options[index] += " (잠김)";
        presentationController.ShowOptions(options, selectedGearIndex);
    }

    private void ResolveEncounter()
    {
        duelMiniGame?.CleanupDuel();
        tutorialMiniGames?.Cleanup();
        if (!string.IsNullOrEmpty(activePlaceId))
        {
            resolvedPlaceIds.Add(activePlaceId);
            saveService?.MarkEncounterCompleted(activePlaceId);
        }
        BeginExploration();
    }

    private void CompleteEncounterEpisode()
    {
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
}
