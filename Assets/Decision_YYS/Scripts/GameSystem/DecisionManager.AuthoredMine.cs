using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static Constants;

public partial class DecisionManager
{
    private const string PetDelivery = "@pet";
    private const string BossEntry = "@boss";
    [SerializeField] private GameObject storyPetModel;
    private static readonly string[] AuthoredStats = { "힘", "속도", "지능", "매력" };
    private MineAuthoredProgress Authored => guideProgress.authoredMine;

    private bool AuthoredBossUnlocked() =>
        StoryPack.PackMainOrder.All(id => Authored.flags.Contains(id + ":delivered"));

    private void ShowAuthoredCard(string text, string speaker, string[] options, JToken line = null)
    {
        uiController.SetDialogueClue(null, null);
        var card = new Dialogue { text = text, speakerId = speaker, options = options, type = options == null ? "Next" : "Choice" };
        if (line?["played_line_path"] != null)
        {
            card.authoredLinePath = (string)line["played_line_path"];
            card.authoredNpcId = (string)line["played_npc_id"];
            card.authoredOriginalText = (string)line["original_text"];
            RecordPlayedEncounterStory(card, activePlaceId, activeEncounterPath, Authored.storyPackLine);
        }
        activeCardIndex = 0;
        activeCards = new ScenarioData { MainStory = new System.Collections.Generic.List<Dialogue> { card } };
        currentState = StoryState.Transitioning;
        selectedGearIndex = options == null ? -1 : 0;
        journeyBoardInput?.SetChoiceCount(options?.Length ?? 0, selectedGearIndex);
        presentationController.ShowDialogue(card);
        FrameActiveEncounter(false);
        currentState = options == null ? StoryState.ShowingStory : StoryState.WaitingForChoice;
        if (options != null) RenderEncounterOptions();
        SaveGuideCheckpoint();
        SaveEncounterState("card");
        SetYellowInputInteractable(true);
    }

    private void ApplyAuthoredDelta(int[] delta)
    {
        int[] stats = MineChapterSettings.ApplyDelta(statContainer.stats, delta);
        statContainer.SetStats(stats);
        guideProgress.committedStats = stats;
        SyncAbilityWallet();
    }

    private void AddAuthoredFlags(System.Collections.Generic.IEnumerable<string> flags)
    {
        foreach (string flag in flags)
            if (!Authored.flags.Contains(flag)) Authored.flags.Add(flag);
    }

    private void FinishAuthoredEncounter()
    {
        uiController.SetDialogueClue(null, null);
        Authored.eventId = Authored.nodeId = null;
        resolvedPlaceIds.Add(activePlaceId);
        saveService.MarkEncounterCompleted(activePlaceId);
        SaveGuideCheckpoint();
        BeginExploration();
    }

    private IEnumerator DeliverAuthoredPet()
    {
        string packMessage = Authored.storyPackResolution != null ?
            (string)PackStory()["pet_on_complete"]?[Authored.storyPackResolution] : null;
        currentState = StoryState.Transitioning;
        SetYellowInputInteractable(false);
        presentationController.ExitChoice();
        presentationController.ShowWalkingView();
        presentationController.ShowPlayerViewControl();
        uiController.SetStoryHeading((string)PackStory()["title"]);
        string nextId = (string)StoryPack.PackPetRoute(Authored.eventId)?["to_story_id"];
        string recipient = nextId != null && StoryPack.PackMainOrder.Contains(nextId)
            ? StoryPack.PackName((string)StoryPack.PackIncoming(nextId)?["speaker_id"]) : null;
        uiController.SetStoryNotice(recipient != null ? $"메인 사건 종료 / {recipient}에게 펫 전언 전달 중" :
            "메인 사건 종료 / 펫 전언 전달 중");
        if (!string.IsNullOrEmpty(packMessage)) Debug.Log(packMessage, this);
        playerController.ResetNpcFraming();
        if (storyPetModel != null)
        {
            var actor = FindEncounterActor(activePlaceId, pendingEncounter?.DisplayName);
            Vector3 lane = playerController.CurrentPosition;
            Vector3 start = (actor != null ? actor.position : lane + Vector3.right * 2f) + Vector3.up * 1.4f;
            Vector3 turn = new Vector3(2f * lane.x - start.x, start.y + .35f, lane.z + 2f);
            Vector3 destination = turn + Vector3.forward * 16f;
            var model = Instantiate(storyPetModel, start, Quaternion.identity);
            model.transform.localScale *= .32f;
            yield return MoveStoryPet(model.transform, start, turn, .65f);
            yield return MoveStoryPet(model.transform, turn, destination, 1.5f);
            Destroy(model);
        }
        Authored.storyPackLastMainResolution = Authored.storyPackResolution;
        AddAuthoredFlags(new[] { Authored.eventId + ":delivered" });
        Authored.storyPackFrame = Authored.storyPackLines = Authored.storyPackResolution = null;
        FinishAuthoredEncounter();
    }

    private static IEnumerator MoveStoryPet(Transform pet, Vector3 from, Vector3 to, float duration)
    {
        pet.rotation = Quaternion.LookRotation(to - from) * Quaternion.Euler(0f, 180f, 0f);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            pet.position = Vector3.Lerp(from, to, elapsed / duration) +
                Vector3.up * (.12f * Mathf.Sin(elapsed * 18f));
            yield return null;
        }
        pet.position = to;
    }

    private void PresentAuthoredBoss()
    {
        activeEncounterPath = MineAuthoredStory.Prefix + "Boss";
        Authored.nodeId = BossEntry;
        uiController.SetStoryHeading("갱도 보스");
        ShowAuthoredCard("보스 구역에 진입합니다.", "안내", null);
    }

    private void AdvanceAuthoredBoss()
    {
        var chapter = currentOmnibus.chapters[chapterIndex];
        RelayCompletedEpisode();
        SaveEncounterState("battle");
        EnterFinalBattle("갱도 보스 구역 진입", chapter.bossStage, chapter.chapterId);
    }
}
