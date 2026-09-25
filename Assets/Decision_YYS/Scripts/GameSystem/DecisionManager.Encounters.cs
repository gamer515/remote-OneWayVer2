using System;
using System.Collections.Generic;
using UnityEngine;
using static Constants;

/// <summary>Initial_01의 진행은 지문 순서가 아닌 배치된 오브젝트가 결정합니다.</summary>
public partial class DecisionManager
{
    private const float EncounterStopDistance = 1.5f;
    private EncounterFlowController encounterFlow;
    private EncounterContentRepository encounterContent;
    private EncounterFlowController.Encounter? pendingEncounter;
    private readonly HashSet<string> resolvedPlaceIds = new HashSet<string>(StringComparer.Ordinal);
    private string activePlaceId;
    private string activeEncounterPath;
    private EncounterInteractionData activeInteraction;
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
        if ((phase == "prompt" || phase == "card") && encounterFlow.TryGetEncounter(activePlaceId, out var encounter))
        {
            pendingEncounter = encounter;
            if (phase == "prompt") ShowEncounterPrompt(encounter);
            else OpenEncounterCards(encounter, activeCardIndex);
        }
        else BeginExploration();
    }

    private void HandleEncounterYellowPressed()
    {
        switch (currentState)
        {
            case StoryState.Exploring: BeginWalkStep(); break;
            case StoryState.EncounterPrompt: ConfirmEncounterOption(); break;
            case StoryState.ShowingStory: AdvanceEncounterCard(); break;
            case StoryState.WaitingForChoice:
                if (selectedGearIndex >= 0) AdvanceEncounterCard();
                break;
            case StoryState.TutorialResult: ResolveEncounter(); break;
        }
    }

    private void BeginExploration()
    {
        pendingEncounter = null;
        activePlaceId = null;
        activeEncounterPath = null;
        activeInteraction = null;
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
            endZ = first.WorldPosition.z - EncounterStopDistance;
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
            playerController.StopAndLookAt(encounter.WorldPosition);
            FaceNpcTowardPlayer(encounter.PlaceId);
            ShowEncounterPrompt(encounter);
        }
        else if (playerController.CurrentPosition.z >= encounterFlow.EpisodeEndZ - 0.01f)
            CompleteEncounterEpisode();
        else BeginExploration();
    }

    private void ShowEncounterPrompt(EncounterFlowController.Encounter encounter)
    {
        playerController.StopAndLookAt(encounter.WorldPosition);
        FaceNpcTowardPlayer(encounter.PlaceId);
        activePlaceId = encounter.PlaceId;
        activeEncounterPath = encounter.ContentPath;
        if (!encounterContent.TryLoadInteraction(encounter.ContentPath, out activeInteraction, out string error))
        {
            Debug.LogError($"[조우] {encounter.PlaceId}: {error}", this);
            ResolveEncounter();
            return;
        }
        currentState = StoryState.EncounterPrompt;
        selectedGearIndex = -1;
        ResetGearSelection();
        bettingButtonController?.SetBettingInteractable(false);
        SetYellowInputInteractable(true);
        RenderEncounterOptions();
        SaveEncounterState("prompt");
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

    private void ConfirmEncounterOption()
    {
        if (!pendingEncounter.HasValue || activeInteraction == null || selectedGearIndex < 0) return;
        EncounterInteractionOption option = activeInteraction.options[selectedGearIndex];
        if (!saveService.HasAllUnlocks(option.requiresUnlocks))
        {
            RenderEncounterOptions();
            return;
        }

        saveService.ApplyEncounterEffect(
            $"{activePlaceId}:interaction:{selectedGearIndex}",
            option.grantsUnlocks,
            option.relationshipId,
            option.relationshipDelta);
        Debug.Log($"[조우 선택] {activePlaceId}: {option.text}", this);
        switch (option.action)
        {
            case "skip": ResolveEncounter(); break;
            case "duel": StartDuelTutorial(); break;
            case "coin_heads": StartCoinTutorial(true); break;
            case "coin_tails": StartCoinTutorial(false); break;
            default: OpenEncounterCards(pendingEncounter.Value, 0); break;
        }
    }

    private void StartDuelTutorial()
    {
        currentState = StoryState.Transitioning;
        SetYellowInputInteractable(true);
        presentationController.ExitChoice();
        presentationController.ShowDialogue(new Dialogue
        {
            type = "Next",
            text = "보드 중앙에 연습용 기사 복제품을 준비합니다. 이후 대련 동작은 이 복제품을 기준으로 구성할 수 있습니다."
        });
        if (tutorialMiniGames == null)
        {
            Debug.LogError("TutorialMiniGameController가 연결되지 않았습니다.", this);
            ShowTutorialResult("손칼 대련을 시작할 수 없습니다. Inspector 참조를 확인하세요.");
            return;
        }
        tutorialMiniGames.StartDuel(created => ShowTutorialResult(
            created ? "보드 중앙에 연습용 기사 복제품을 생성했습니다." : "연습용 기사 복제품을 생성하지 못했습니다."));
    }

    private void StartCoinTutorial(bool choseHeads)
    {
        currentState = StoryState.Transitioning;
        SetYellowInputInteractable(false);
        presentationController.ExitChoice();
        presentationController.ShowDialogue(new Dialogue
        {
            type = "Next",
            text = $"{(choseHeads ? "앞면" : "뒷면")}을 선택했습니다. 금화가 멈출 때까지 기다리세요."
        });
        if (tutorialMiniGames == null)
        {
            Debug.LogError("TutorialMiniGameController가 연결되지 않았습니다.", this);
            ShowTutorialResult("금화 도박을 시작할 수 없습니다. Inspector 참조를 확인하세요.");
            return;
        }
        tutorialMiniGames.StartCoinToss(choseHeads, (won, heads) => ShowTutorialResult(
            $"금화는 {(heads ? "앞면" : "뒷면")}입니다. {(won ? "선택이 맞았습니다!" : "선택이 빗나갔습니다.")}"));
    }

    private void ShowTutorialResult(string message)
    {
        currentState = StoryState.TutorialResult;
        presentationController.ShowDialogue(new Dialogue { type = "End", text = message });
        SetYellowInputInteractable(true);
        SaveEncounterState("tutorial_result");
    }

    private void OpenEncounterCards(EncounterFlowController.Encounter encounter, int index)
    {
        activePlaceId = encounter.PlaceId;
        activeEncounterPath = encounter.ContentPath;
        if (!encounterContent.TryLoadCards(encounter.ContentPath, out activeCards, out string error) ||
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
        Dialogue card = activeCards.MainStory[activeCardIndex];
        selectedGearIndex = -1;
        ResetGearSelection();
        currentState = StoryState.Transitioning;
        SetYellowInputInteractable(false);
        bettingButtonController?.SetBettingInteractable(false);
        RecordPlayedEncounterStory(card, activePlaceId, activeEncounterPath, activeCardIndex);
        SaveEncounterState("card");
        if (card.isTransition)
            presentationController.PlayStoryTransition(card, () => CompleteEncounterCardPresentation(card));
        else
        {
            presentationController.ShowDialogue(card);
            CompleteEncounterCardPresentation(card);
        }
    }

    private void CompleteEncounterCardPresentation(Dialogue card)
    {
        if (card.IsChoice)
        {
            currentState = StoryState.WaitingForChoice;
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
            Debug.Log($"[카드 선택] {activePlaceId}: {activeCards.MainStory[activeCardIndex].options[selectedGearIndex]}", this);
        activeCardIndex++;
        if (activeCardIndex >= activeCards.MainStory.Count) ResolveEncounter();
        else PresentEncounterCard();
    }

    private void HandleEncounterGearSelection(int index)
    {
        if (encounterFlow == null ||
            (currentState != StoryState.EncounterPrompt && currentState != StoryState.WaitingForChoice)) return;
        selectedGearIndex = index >= 0 && index < 4 ? index : -1;
        RenderEncounterOptions();
    }

    private void RenderEncounterOptions()
    {
        if (currentState == StoryState.EncounterPrompt && activeInteraction != null)
            presentationController.ShowEncounterPrompt(
                activeInteraction.prompt,
                Array.ConvertAll(
                    activeInteraction.options,
                    option => saveService.HasAllUnlocks(option.requiresUnlocks)
                        ? option.text
                        : option.text + " (잠김)"),
                selectedGearIndex);
        else if (currentState == StoryState.WaitingForChoice && activeCards != null)
            presentationController.ShowOptions(activeCards.MainStory[activeCardIndex].options, selectedGearIndex);
    }

    private void ResolveEncounter()
    {
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
            phase, activePlaceId, activeCardIndex, resolvedPlaceIds);
}
