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
        encounterContent = new EncounterContentRepository();
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
        }
    }

    private void BeginExploration()
    {
        pendingEncounter = null;
        activePlaceId = null;
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
            endZ = first.WorldPosition.z;
        }
        currentState = StoryState.MovingToEncounter;
        SetYellowInputInteractable(false);
        presentationController.ShowWalkingView();
        playerController.MoveToZ(Mathf.Max(startZ, endZ));
        SaveEncounterState("walking");
    }

    private void CompleteWalkStep()
    {
        if (pendingEncounter.HasValue)
        {
            var encounter = pendingEncounter.Value;
            playerController.StopAndLookAt(encounter.WorldPosition);
            ShowEncounterPrompt(encounter);
        }
        else if (playerController.CurrentPosition.z >= encounterFlow.EpisodeEndZ - 0.01f)
            CompleteEncounterEpisode();
        else BeginExploration();
    }

    private void ShowEncounterPrompt(EncounterFlowController.Encounter encounter)
    {
        activePlaceId = encounter.PlaceId;
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

    private void ConfirmEncounterOption()
    {
        if (!pendingEncounter.HasValue || activeInteraction == null || selectedGearIndex < 0) return;
        EncounterInteractionOption option = activeInteraction.options[selectedGearIndex];
        Debug.Log($"[조우 선택] {activePlaceId}: {option.text}", this);
        if (option.action == "skip") ResolveEncounter();
        else OpenEncounterCards(pendingEncounter.Value, 0);
    }

    private void OpenEncounterCards(EncounterFlowController.Encounter encounter, int index)
    {
        activePlaceId = encounter.PlaceId;
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
        RecordPlayedStory(card);
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
            presentationController.ShowEncounterPrompt(activeInteraction.prompt,
                Array.ConvertAll(activeInteraction.options, option => option.text), selectedGearIndex);
        else if (currentState == StoryState.WaitingForChoice && activeCards != null)
            presentationController.ShowOptions(activeCards.MainStory[activeCardIndex].options, selectedGearIndex);
    }

    private void ResolveEncounter()
    {
        if (!string.IsNullOrEmpty(activePlaceId)) resolvedPlaceIds.Add(activePlaceId);
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
