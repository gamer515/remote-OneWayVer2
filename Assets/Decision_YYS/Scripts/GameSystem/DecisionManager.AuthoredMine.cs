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

    private bool PrepareAuthoredMineProgress()
    {
        var pack = envController.AuthoredMine;
        var chapter = session.Omnibus.chapters.ElementAtOrDefault(chapterIndex);
        int mineEpisode = chapter?.episodeIds?.FindIndex(id => chapter.chapterId + "/" + id == MineAuthoredStory.Episode) ?? -1;
        if (pack == null || mineEpisode < 0) return false;
        var previous = Authored;
        bool sameMode = previous == null || previous.shortStoryPack == pack.IsShort;
        if (!sameMode)
            foreach (var record in guideProgress.runHistory)
                if (record?.card is Dialogue card && !string.IsNullOrEmpty(card.authoredLinePath) &&
                    string.IsNullOrEmpty(card.authoredStoryPackMode))
                    card.authoredStoryPackMode = MineAuthoredStory.ModeId(previous.shortStoryPack);
        if (pack.IsShort)
        {
            pack.ShortLayoutSeed = ResolveShortLayoutSeed(previous);
            if (previous?.shortStoryPack == true)
                previous.shortLayoutSeed = pack.ShortLayoutSeed.Value;
        }
        if (sameMode && previous?.contentVersion == MineAuthoredStory.PackVersion) return false;

        var delivered = sameMode && previous?.flags != null
            ? pack.PackMainOrder.Select(id => id + ":delivered").Where(previous.flags.Contains).ToList()
            : new System.Collections.Generic.List<string>();
        guideProgress.authoredMine = new MineAuthoredProgress
        {
            contentVersion = MineAuthoredStory.PackVersion,
            shortStoryPack = pack.IsShort,
            shortLayoutSeed = pack.ShortLayoutSeed ?? 0,
            flags = delivered,
            storyPackLastMainResolution = sameMode ? previous?.storyPackLastMainResolution : null
        };
        if (episodeIndex != mineEpisode && AuthoredBossUnlocked()) return false;

        episodeIndex = mineEpisode;
        loadedProgress.resolvedPlaceIds?.RemoveAll(id => !delivered.Contains(id + ":delivered"));
        loadedProgress.activePlaceId = loadedProgress.activeStoryPath = loadedProgress.interactionPhase = null;
        session.PlayedEncounterHistory.Clear();
        var mineProgress = guideProgress.mineChapter;
        mineProgress.collectorRouteVersion = 0;
        mineProgress.collectorRouteStartZ = mineProgress.collectorRouteEndZ = 0;
        mineProgress.taxPatrolActive = mineProgress.collectorReturning = false;
        mineProgress.taxInvoice = null;
        return true;
    }

    private int ResolveShortLayoutSeed(MineAuthoredProgress current)
    {
        if (current?.shortStoryPack == true)
            return current.shortLayoutSeed > 0 ? current.shortLayoutSeed : session.RunSeed;

        if (session.RunNumber > 1)
        {
            var lastRun = SaveIOService.Instance.LoadRunData<GameProgress>(session.RunNumber - 1, "Progress");
            var lastPack = lastRun?.guideProgress?.authoredMine;
            if (lastRun?.isCompleted == true && lastPack?.shortStoryPack == true)
                return lastPack.shortLayoutSeed > 0 ? lastPack.shortLayoutSeed :
                    lastRun.runSeed > 0 ? lastRun.runSeed : session.RunSeed;
        }
        return session.RunSeed;
    }

    private void ShowAuthoredCard(string text, string speaker, string[] options, JToken line = null)
    {
        uiController.SetDialogueClue(null, null);
        var card = new Dialogue { text = text, speakerId = speaker, options = options, type = options == null ? "Next" : "Choice" };
        if (line?["played_line_path"] != null)
        {
            card.authoredLinePath = (string)line["played_line_path"];
            card.authoredNpcId = (string)line["played_npc_id"];
            card.authoredOriginalText = (string)line["original_text"];
            card.authoredStoryPackMode = MineAuthoredStory.ModeId(StoryPack.IsShort);
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
        FinishEncounterAndResume(showRevisit: false);
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
            Vector3 start = actor != null ? actor.position : lane + Vector3.right * 2f;
            start.x = Mathf.MoveTowards(start.x, lane.x, .75f);
            start.y = lane.y;
            Vector3 turn = new Vector3(lane.x, lane.y, start.z + 1.5f);
            Vector3 destination = turn + Vector3.forward * 12f;
            var model = Instantiate(storyPetModel, start, Quaternion.identity);
            model.transform.localScale *= .32f;
            float footOffset = -MineAuthoredStory.RenderedBounds(model).min.y * model.transform.lossyScale.y;
            model.transform.position = StoryPetGroundPose(start, footOffset, 0f);
            yield return MoveStoryPet(model.transform, start, turn, .65f, footOffset);
            yield return MoveStoryPet(model.transform, turn, destination, 1.8f, footOffset);
            Destroy(model);
        }
        Authored.storyPackLastMainResolution = Authored.storyPackResolution;
        AddAuthoredFlags(new[] { Authored.eventId + ":delivered" });
        Authored.storyPackFrame = Authored.storyPackLines = Authored.storyPackResolution = null;
        FinishAuthoredEncounter();
    }

    private static IEnumerator MoveStoryPet(Transform pet, Vector3 from, Vector3 to, float duration, float footOffset)
    {
        Vector3 heading = to - from;
        float length = heading.magnitude;
        if (heading.sqrMagnitude > .001f)
            pet.rotation = Quaternion.LookRotation(heading);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            Vector3 point = Vector3.Lerp(from, to, elapsed / duration);
            float distance = length * elapsed / duration;
            pet.position = StoryPetGroundPose(point, footOffset, .04f * Mathf.Abs(Mathf.Sin(distance * 8f)));
            yield return null;
        }
        pet.position = StoryPetGroundPose(to, footOffset, 0f);
    }

    private static Vector3 StoryPetGroundPose(Vector3 point, float footOffset, float stride)
    {
        var probe = point + Vector3.up * .45f;
        if (Physics.Raycast(probe, Vector3.down, out var hit, 1.1f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.normal.y > .65f)
            point.y = hit.point.y;
        point.y += footOffset + stride;
        return point;
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
