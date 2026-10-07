using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static Constants;

public partial class DecisionManager
{
    private bool guideCoinBusy;
    private PlayableGraph guideKneelGraph;
    private Animator guideKneelAnimator;
    private bool guideKneelRootMotion;
    private Vector3 guideKneelPosition;
    private Quaternion guideKneelRotation;

    private string GuideEffectKey(Dialogue card) => activePlaceId + ":" + activeEncounterPath + ":" + card.eventIdStable;

    private void SaveGuideCheckpoint(string endReason = null)
    {
        if (saveService == null || session == null || guideProgress == null || storyEvents == null) return;
        saveService.SaveStoryCheckpoint(guideProgress, storyEvents, session.PlayedEncounterHistory,
            coinDropController.RemainingCoins, storyVisit, endReason);
    }

    private StoryEventRecord NewStoryEvent(string key, string kind, Dialogue card)
    {
        string characterId = card.speakerId;
        if (string.IsNullOrWhiteSpace(characterId) && pendingEncounter.HasValue)
            characterId = pendingEncounter.Value.DisplayName;
        TextAsset profile = Resources.Load<TextAsset>("Story_Json_Data/Characters/" + characterId);
        StoryCharacter character = profile != null ? JsonUtility.FromJson<StoryCharacter>(profile.text) : null;
        string[] path = (currentScenarioPath ?? "").Split('/');
        return new StoryEventRecord
        {
            eventKey = key, sequence = storyEvents.Count + 1, kind = kind, run = session.RunNumber,
            chapterId = path.Length > 0 ? path[0] : "", episodeId = path.Length > 1 ? path[1] : "",
            terrainId = currentScenarioPath, terrainName = envController?.TerrainData?.terrainName,
            placeId = activePlaceId, characterId = characterId,
            characterName = character?.name ?? characterId,
            storyPath = activeEncounterPath, cardId = card.eventIdStable ?? card.id.ToString(),
            displayedText = card.text, semanticText = card.semanticText ?? card.text,
            coinsBefore = coinDropController.RemainingCoins, statsBefore = statContainer.stats
        };
    }

    private void RecordStoryChoice(Dialogue card, StoryChoiceAction choice)
    {
        string key = "choice:" + storyVisit;
        if (storyEvents.Exists(record => record.eventKey == key)) return;
        var record = NewStoryEvent(key, "choice", card);
        record.choiceId = string.IsNullOrWhiteSpace(choice.choiceId) ?
            activeEncounterPath + ":" + record.cardId + ":" + selectedGearIndex : choice.choiceId;
        record.displayedText = card.options[selectedGearIndex];
        record.choiceSlot = selectedGearIndex;
        record.semanticText = record.displayedText;
        record.relationshipId = choice.relationshipId;
        record.relationshipDelta = choice.relationshipDelta * (statContainer.stats[3] == 0 ? 2 : 1);
        record.coinsAfter = coinDropController.RemainingCoins;
        record.statsAfter = statContainer.stats;
        storyEvents.Add(record);
        SaveGuideCheckpoint();
    }

    private void RecordMiniGameResult(StoryEventRecord record, bool won)
    {
        if (storyEvents.Exists(e => e.eventKey == record.eventKey)) return;
        record.sequence = storyEvents.Count + 1;
        record.reason = won ? "win" : "lose";
        record.coinsAfter = coinDropController.RemainingCoins;
        record.statsAfter = statContainer.stats;
        storyEvents.Add(record);
        SaveGuideCheckpoint();
    }

    private IEnumerator RunGuideAction(Dialogue card)
    {
        string key = GuideEffectKey(card);
        GuideCoinAction action = card.guideAction;
        if (action.kind == "allocate")
        {
            // 확정 직후 종료된 체크포인트는 같은 배분을 다시 열지 않습니다.
            var confirmed = storyEvents.FindLast(e => e.eventKey == "allocation:" + storyVisit);
            if (confirmed != null) { RouteGuideAllocation(card, confirmed.reason); yield break; }
            if (!guideProgress.allocating || guideProgress.allocationVisit != storyVisit)
            {
                guideProgress.allocating = true;
                guideProgress.allocationVisit = storyVisit;
                guideProgress.allocationAttempt++;
                guideProgress.allocationMode = 0;
                guideProgress.allocationCoinsBefore = coinDropController.RemainingCoins;
                guideProgress.allocationStatsBefore = statContainer.stats;
                guideProgress.allocation = new int[4];
                journeyCoinSupply?.ResetBoard();
                // 배분 중의 0은 초안입니다. 확정 전 Stats는 유지하고 사망 조건을 검사하지 않습니다.
                SaveGuideCheckpoint();
            }
            yield return AnimateGuideInventory(guideProgress.allocation);
            currentState = StoryState.WaitingForChoice;
            SetYellowInputInteractable(true);
            ShowGuideAllocation();
            yield break;
        }
        if (!guideProgress.completedEffects.Contains(key))
        {
            currentState = StoryState.Transitioning;
            SetYellowInputInteractable(false);
            if (guideProgress.activeEffectId != key)
            {
                guideProgress.activeEffectId = key;
                guideProgress.effectStage = 0;
                guideProgress.effectBefore = coinDropController.RemainingCoins;
                if (action.kind == "take_knowledge") guideProgress.knowledgeBefore = coinDropController.RemainingCoins;
                if (action.kind == "punish") guideProgress.punishmentBefore = coinDropController.RemainingCoins;
                SaveGuideCheckpoint();
            }
            switch (action.kind)
            {
                case "take_knowledge":
                    int[] target = (int[])guideProgress.knowledgeBefore.Clone();
                    target[2] = Math.Max(0, target[2] - action.amount);
                    yield return AnimateGuideInventory(target);
                    break;
                case "return_knowledge":
                    if (guideProgress.knowledgeBefore?.Length == 4)
                        yield return AnimateGuideInventory(guideProgress.knowledgeBefore);
                    break;
                case "punish":
                    if (guideProgress.effectStage == 0)
                    {
                        yield return AnimateGuideInventory(new[] { 1, 1, 1, 1 });
                        guideProgress.effectStage = 1; SaveGuideCheckpoint();
                    }
                    if (guideProgress.effectStage == 1)
                    {
                        yield return AnimateGuideInventory(new[] { 20, 20, 20, 20 });
                        guideProgress.effectStage = 2; SaveGuideCheckpoint();
                    }
                    int[] restored = (int[])guideProgress.punishmentBefore.Clone();
                    restored[0] = 1;
                    yield return AnimateGuideInventory(restored);
                    break;
                case "kneel": yield return PlayGuideKneel(); break;
            }
            guideProgress.completedEffects.Add(key);
            var record = NewStoryEvent(key, action.kind, card);
            record.coinsBefore = guideProgress.effectBefore;
            record.coinsAfter = coinDropController.RemainingCoins;
            record.statsAfter = statContainer.stats;
            storyEvents.Add(record);
            guideProgress.activeEffectId = null;
            SaveGuideCheckpoint();
        }
        currentState = StoryState.ShowingStory;
        SetYellowInputInteractable(true);
    }

    private IEnumerator AnimateGuideInventory(int[] target)
    {
        for (int type = 0; type < 4; type++)
        {
            while (coinDropController.RemainingCoins[type] != target[type])
            {
                int[] counts = coinDropController.RemainingCoins;
                bool adding = counts[type] < target[type];
                if (!adding)
                {
                    counts[type]--; coinDropController.InitializeInventory(counts);
                    journeyCoinSupply?.RefreshDisplay(); SaveGuideCheckpoint();
                }
                if (journeyCoinSupply != null) yield return journeyCoinSupply.AnimateStoryCoin(type, adding);
                if (adding)
                {
                    counts[type]++; coinDropController.InitializeInventory(counts);
                    journeyCoinSupply?.RefreshDisplay(); SaveGuideCheckpoint();
                }
            }
        }
    }

    private IEnumerator PlayGuideKneel()
    {
        Animator animator = playerController?.PlayerAnimator;
        if (animator == null || guideKneelClip == null)
        {
            Debug.LogWarning("[Guide] kneel 클립 또는 플레이어 Animator 연결을 확인하세요.", this);
            yield break;
        }
        guideKneelGraph = PlayableGraph.Create("GuideKneel");
        guideKneelGraph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
        var playable = AnimationClipPlayable.Create(guideKneelGraph, guideKneelClip);
        playable.SetApplyFootIK(false);
        var output = AnimationPlayableOutput.Create(guideKneelGraph, "Kneel", animator);
        output.SetSourcePlayable(playable);
        guideKneelAnimator = animator;
        guideKneelRootMotion = animator.applyRootMotion;
        guideKneelPosition = animator.transform.localPosition;
        guideKneelRotation = animator.transform.localRotation;
        animator.applyRootMotion = false;
        guideKneelGraph.Play();
        try
        {
            float elapsed = 0f;
            while (elapsed < guideKneelClip.length)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        finally { RestoreGuideKneel(); }
    }

    private void RestoreGuideKneel()
    {
        if (guideKneelGraph.IsValid()) guideKneelGraph.Destroy();
        if (guideKneelAnimator == null) return;
        guideKneelAnimator.applyRootMotion = guideKneelRootMotion;
        guideKneelAnimator.transform.SetLocalPositionAndRotation(guideKneelPosition, guideKneelRotation);
        guideKneelAnimator = null;
    }

    private void HandleGuideAllocationInput()
    {
        if (guideCoinBusy || currentState != StoryState.WaitingForChoice) return;
        int type = journeyCoinSupply?.GetClickedSupplyIndex() ?? -1;
        if (type >= 0 && guideProgress.allocationMode != 0 &&
            GuideAllocationRules.TryChange(guideProgress.allocation, type, guideProgress.allocationMode))
        {
            coinDropController.InitializeInventory(guideProgress.allocation);
            journeyCoinSupply?.RefreshDisplay();
            SaveGuideCheckpoint();
            ShowGuideAllocation();
            StartCoroutine(AnimateAllocationCoin(type, guideProgress.allocationMode > 0));
        }
    }

    private IEnumerator AnimateAllocationCoin(int type, bool adding)
    {
        guideCoinBusy = true;
        if (journeyCoinSupply != null) yield return journeyCoinSupply.AnimateStoryCoin(type, adding);
        guideCoinBusy = false;
    }

    private void ShowGuideAllocation()
    {
        int[] allocation = guideProgress.allocation;
        string mode = guideProgress.allocationMode == 0 ? "기어 방향을 선택해" :
            guideProgress.allocationMode > 0 ? "추가 모드" : "빼기 모드";
        string text = "기어 위: 추가 · 아래: 빼기\n" +
            $"남은 {GuideAllocationRules.Remaining(allocation)}/20 · {mode}";
        string summary = $"체{allocation[0]} 민{allocation[1]} 지{allocation[2]} 매{allocation[3]}";
        presentationController.ShowGuideAllocation(text, summary);
    }

    private void ConfirmGuideAllocation()
    {
        if (guideCoinBusy) return;
        string outcome = GuideAllocationRules.Outcome(guideProgress.allocation);
        if (outcome == "incomplete" || outcome == "invalid") { ShowGuideAllocation(); return; }
        Dialogue card = activeCards.MainStory[activeCardIndex];
        var record = NewStoryEvent("allocation:" + storyVisit, "allocation", card);
        record.coinsBefore = guideProgress.allocationCoinsBefore;
        record.statsBefore = guideProgress.allocationStatsBefore;
        record.coinsAfter = (int[])guideProgress.allocation.Clone();
        record.statsAfter = (int[])guideProgress.allocation.Clone();
        record.reason = outcome;
        storyEvents.Add(record);
        guideProgress.allocating = false;
        guideProgress.committedStats = (int[])guideProgress.allocation.Clone();
        statContainer.SetStats(guideProgress.committedStats);
        SaveGuideCheckpoint(outcome == "death" ? "guide_health_zero" : null);
        Debug.Log($"[Guide] 20개 배분 확정: [{string.Join(", ", guideProgress.committedStats)}], {outcome}", this);
        RouteGuideAllocation(card, outcome);
    }

    private void RouteGuideAllocation(Dialogue card, string outcome)
    {
        if (outcome == "death")
        {
            FinishRunAndReturnToMainMenu("체력 0으로 사망");
            return;
        }
        string path = outcome == "retry" ? card.guideAction.retryStoryPath : card.guideAction.readyStoryPath;
        if (outcome == "ready" && statContainer.stats[3] == 0 &&
            !string.IsNullOrWhiteSpace(card.guideAction.charmZeroStoryPath)) path = card.guideAction.charmZeroStoryPath;
        OpenStoryBranch(path);
    }

    private void QueueLocalChapterRevision(string reason)
    {
        if (session?.Omnibus?.chapters == null || session.ChapterIndex < 0) return;
        int index = Math.Min(session.ChapterIndex, session.Omnibus.PlayableChapterCount - 1);
        var chapter = session.Omnibus.chapters[index];
        var paths = chapter.episodeIds.ConvertAll(id => chapter.chapterId + "/" + id);
        var request = new LocalStoryRevisionRequest
        {
            sourceRun = session.RunNumber, targetRun = session.RunNumber + 1,
            chapterId = chapter.chapterId, scenarioPaths = paths.ToArray(), reason = reason,
            finalStats = statContainer?.stats,
            events = storyEvents ?? new List<StoryEventRecord>(),
            playedCards = new List<PlayedEncounterCardRecord>(guideProgress.runHistory)
        };
        SaveIOService.Instance.SaveRunCheckpoint(session.RunNumber, "LocalStoryRevisionRequest", request);
        Debug.Log("[Story] 다음 회차 로컬 이야기 변경 요청 저장. 모델 연결 전에는 원본 이야기를 사용합니다.", this);
    }
}
