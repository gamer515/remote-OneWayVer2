using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static Constants;

public partial class DecisionManager
{
    private const int HighStoryAbility = 7;
    private const int LowStoryAbility = 3;
    private const string LowIntelligenceVariant = "지능_낮음";
    private const float PackRequirementSeconds = 3f;
    private const string PackRoot = "root", PackCoins = "coins", PackNegotiate = "negotiate",
        PackToss = "toss", PackTask = "task", PackDuel = "duel", PackLeave = "leave";
    private MineAuthoredStory StoryPack => envController.AuthoredMine;
    private bool IsPackMain => Authored.eventId.StartsWith("MAIN-", StringComparison.Ordinal);
    private Dictionary<string, LocalStoryDialogueJob> previousPackDialogue;
    private string PackSpeaker(string id) => IsPackMain ? StoryPack.PackName(id) : pendingEncounter?.DisplayName;
    private int StoredStoryIntelligence() => journeyCoinSupply?.IsReady == true
        ? journeyCoinSupply.AvailableCoins(2) : statContainer.stats[2];

    private int StoryAbility(int ability) => ability != 2 ? statContainer.stats[ability] :
        Authored.hasStoryPackIntelligenceAtEntry ? Authored.storyPackIntelligenceAtEntry : StoredStoryIntelligence();

    private bool HasHighStoryAbility(int ability) => StoryAbility(ability) >= HighStoryAbility;
    private JObject PackStory(string variant = null)
    {
        string storyId = activeEncounterPath.Substring(MineAuthoredStory.Prefix.Length);
        return StoryPack.PackStory(storyId, session.RunSeed, variant);
    }

    private string PackAbilityVariant(int ability, string label) => HasHighStoryAbility(ability) ? label + "_높음" :
        StoryAbility(ability) <= LowStoryAbility ? label + "_낮음" : null;

    private JObject PackDialogueStory()
    {
        string intelligence = PackAbilityVariant(2, "지능");
        if (StoryPack.IsShort)
            return intelligence == LowIntelligenceVariant ? PackStory(intelligence) : ApplyPackEmotion(PackStory());
        string charm = PackAbilityVariant(3, "매력");
        if (intelligence == null && charm == null) return ApplyPackEmotion(PackStory());
        if (intelligence == null || charm == null) return PackStory(charm ?? intelligence);
        bool lowIntelligence = intelligence == LowIntelligenceVariant;
        var primary = (JObject)PackStory(lowIntelligence ? intelligence : charm).DeepClone();
        var secondary = PackStory(lowIntelligence ? charm : intelligence);
        var original = PackStory();
        var baseOpening = (JArray)original["opening"];
        var primaryOpening = (JArray)primary["opening"];
        var secondaryOpening = (JArray)secondary["opening"];
        for (int i = 0; i < Math.Min(baseOpening.Count, Math.Min(primaryOpening.Count, secondaryOpening.Count)); i++)
            if (JToken.DeepEquals(primaryOpening[i], baseOpening[i])) primaryOpening[i] = secondaryOpening[i].DeepClone();
        if (!lowIntelligence)
            foreach (var line in secondary.SelectTokens("$..hover_hint").ToArray())
                if (primary.SelectToken(line.Parent.Parent.Path) is JObject primaryLine)
                {
                    primaryLine["text"] = line.Parent.Parent["text"].DeepClone();
                    primaryLine["hover_hint"] = line.DeepClone();
                }
        foreach (var voice in secondary.SelectTokens("$..dialogue").OfType<JArray>())
            if (JToken.DeepEquals(primary.SelectToken(voice.Path), original.SelectToken(voice.Path)) &&
                primary.SelectToken(voice.Path) is JArray unchanged)
            {
                unchanged.Replace(voice.DeepClone());
            }
        return primary;
    }

    private JObject ApplyPackEmotion(JObject source)
    {
        var dialogue = (JObject)source.DeepClone();
        foreach (var line in dialogue.Descendants().OfType<JObject>().ToArray())
        {
            if (line["speaker_id"]?.Type != JTokenType.String || line["text"]?.Type != JTokenType.String) continue;
            string npcId = IsPackMain ? (string)line["speaker_id"] : envController.TerrainData.places
                .FirstOrDefault(place => place.placeId == activePlaceId)?.storyNpcId;
            if (!StoryPack.IsPackNpc(npcId)) continue;
            string path = line.Path;
            string original = (string)line["text"];
            line["played_line_path"] = path;
            line["played_npc_id"] = npcId;
            line["original_text"] = original;
            string key = LocalStoryDialogueQueue.AuthoredJobId(activeEncounterPath, npcId);
            if (previousPackDialogue != null && previousPackDialogue.TryGetValue(key, out var job))
            {
                var previous = job.emotionLines.FirstOrDefault(value => value.authoredLinePath == path &&
                    value.originalText == original);
                if (previous != null)
                    line["text"] = string.IsNullOrEmpty(previous.generatedText)
                        ? LocalStoryEmotion.RenderEmotion(original, job.emotion)
                        : previous.generatedText;
            }
        }
        return dialogue;
    }

    private void OpenStoryPack(EncounterFlowController.Encounter encounter)
    {
        restoringStoryCard = false;
        activePlaceId = encounter.PlaceId;
        activeEncounterPath = encounter.ContentPath;
        previousPackDialogue = LoadPreviousPackDialogue();
        bool newEncounter = Authored.eventId != activePlaceId || string.IsNullOrEmpty(Authored.storyPackFrame);
        if (newEncounter || !Authored.hasStoryPackIntelligenceAtEntry)
        {
            Authored.storyPackIntelligenceAtEntry = StoredStoryIntelligence();
            Authored.hasStoryPackIntelligenceAtEntry = true;
        }
        if (newEncounter)
        {
            Authored.eventId = activePlaceId;
            Authored.nodeId = null;
            Authored.storyPackFee = 0;
            Authored.storyPackNegotiated = false;
            Authored.storyPackRevealedHint = null;
            Authored.storyPackResolution = null;
            var lines = new JArray();
            var incoming = StoryPack.PackIncoming(activePlaceId);
            string resolution = Authored.storyPackLastMainResolution;
            if (incoming != null && resolution != null &&
                incoming["after_previous_resolution"]?[resolution] is JToken reaction)
                lines.Add(new JObject { ["speaker_id"] = incoming["speaker_id"], ["text"] = reaction });
            AppendPackLines(lines, PackDialogueStory()["opening"]);
            ShowPackLines(lines, PackRoot);
            return;
        }
        if (Authored.storyPackFrame == "side-entry")
            ShowPackLines(PackDialogueStory()["opening"], PackRoot);
        else PresentStoryPack();
    }

    private Dictionary<string, LocalStoryDialogueJob> LoadPreviousPackDialogue()
    {
        if (session.RunNumber <= 1) return null;
        var store = SaveIOService.Instance;
        int sourceRun = session.RunNumber - 1;
        var queue = LocalStoryDialogueQueue.Load(store, sourceRun);
        if (queue?.finalized != true) return null;
        string mode = queue.authoredStoryPackMode;
        if (string.IsNullOrEmpty(mode))
        {
            var previous = store.LoadRunData<GameProgress>(sourceRun, "Progress");
            mode = MineAuthoredStory.ModeId(previous?.guideProgress?.authoredMine?.shortStoryPack == true);
        }
        if (mode != MineAuthoredStory.ModeId(StoryPack.IsShort)) return null;
        return queue.jobs.Where(job => job.authoredPack && job.targetRun == session.RunNumber &&
            job.emotionLines != null && !string.IsNullOrEmpty(job.emotion)).ToDictionary(job => job.jobId);
    }

    private static void AppendPackLines(JArray destination, JToken source)
    {
        if (source is JArray lines)
            foreach (var line in lines) destination.Add(line.DeepClone());
    }

    private void ShowPackLines(JToken lines, string destination)
    {
        Authored.storyPackFrame = destination;
        Authored.storyPackLines = lines?.ToString(Newtonsoft.Json.Formatting.None);
        Authored.storyPackLine = 0;
        PresentStoryPack();
    }

    private JToken PackChoice(JObject story, string id) => story["choices"].First(choice => (string)choice["id"] == id);
    private JToken PackPayment(JObject story) => PackChoice(story, "C1");
    private int PackFee => Authored.storyPackFee > 0 ? Authored.storyPackFee : (int)PackPayment(PackStory())["price"]["base_fee"];

    private JToken[] PackNegotiationRoutes()
    {
        var options = new Dictionary<string, JToken>();
        void Add(JObject story)
        {
            foreach (var route in PackPayment(story)["negotiation"]["routes"])
            {
                string id = (string)route["id"];
                // These authored routes are conditioned on revealing a separate dialogue clue.
                string hint = (string)route["unlock_hint_id"];
                if (hint == null || hint == Authored.storyPackRevealedHint) options[id] = route;
            }
        }
        Add(StoryPack.IsShort || (PackAbilityVariant(2, "지능") == null && PackAbilityVariant(3, "매력") == null)
            ? PackDialogueStory() : PackStory());
        if (!StoryPack.IsShort && HasHighStoryAbility(2)) Add(PackStory("지능_높음"));
        if (!StoryPack.IsShort && HasHighStoryAbility(3)) Add(PackStory("매력_높음"));
        return options.Values.ToArray();
    }

    private void PresentStoryPack()
    {
        var story = PackStory();
        uiController.SetStoryHeading((string)story["title"]);
        string frame = Authored.storyPackFrame;
        var lines = Authored.storyPackLines == null ? null : JArray.Parse(Authored.storyPackLines);
        if (frame == PetDelivery && (lines == null || Authored.storyPackLine >= lines.Count))
        {
            SaveGuideCheckpoint();
            StartCoroutine(DeliverAuthoredPet());
            return;
        }
        if (lines != null && Authored.storyPackLine < lines.Count)
        {
            var line = lines[Authored.storyPackLine];
            ShowAuthoredCard((string)line["text"], PackSpeaker((string)line["speaker_id"]) ?? pendingEncounter?.DisplayName, null, line);
            uiController.SetDialogueClue(HasHighStoryAbility(2) ? (string)line["hover_hint"]?["target_text"] : null, () =>
            {
                string hint = (string)line["hover_hint"]?["id"];
                if (hint != null && hint != Authored.storyPackRevealedHint)
                {
                    Authored.storyPackRevealedHint = hint;
                    SaveGuideCheckpoint();
                }
                return (string)line["hover_hint"]?["reveal"];
            });
            return;
        }

        var last = lines?.Last;
        string speech = (string)last?["text"] ?? (string)PackDialogueStory()["opening"].Last?["text"];
        string speaker = PackSpeaker((string)last?["speaker_id"]) ?? pendingEncounter?.DisplayName;
        switch (frame)
        {
            case PackCoins:
                var options = AuthoredStats.Select(label => label + $" 코인 {PackFee}개").ToList();
                if (!Authored.storyPackNegotiated) options.Add((string)PackPayment(story)["negotiation"]["text"]);
                ShowAuthoredCard(speech, speaker, options.ToArray());
                break;
            case PackNegotiate:
                ShowAuthoredCard(speech, speaker, PackNegotiationRoutes().Select(route => (string)route["text"]).ToArray());
                break;
            case PackToss:
                ShowAuthoredCard(speech, speaker, new[] { "앞면", "뒷면" });
                break;
            case PackDuel:
                RunStoryPackDuel();
                break;
            case PackTask:
                StartCoroutine(RunStoryPackTask());
                break;
            case PackLeave:
                Authored.storyPackFrame = Authored.storyPackLines = null;
                FinishAuthoredEncounter();
                break;
            default:
                ShowAuthoredCard(speech, speaker, story["choices"].Select(choice => (string)choice["text"]).ToArray());
                break;
        }
    }

    private void AdvanceStoryPack()
    {
        if (Authored.storyPackLines != null && Authored.storyPackLine < JArray.Parse(Authored.storyPackLines).Count)
        {
            Authored.storyPackLine++;
            PresentStoryPack();
            return;
        }
        int selected = selectedGearIndex;
        if (selected < 0) return;
        var story = PackStory();
        var voiced = PackDialogueStory();
        switch (Authored.storyPackFrame)
        {
            case PackCoins:
                if (selected == 4) { ShowPackLines(null, PackNegotiate); return; }
                if (statContainer.stats[selected] < PackFee)
                {
                    ShowPackLines(voiced["branches"]["COIN_SHORTAGE"]["dialogue"], PackRoot);
                    return;
                }
                var coin = PackPayment(story)["coin_options"][selected];
                var coinVoice = PackPayment(voiced)["coin_options"][selected];
                var cost = new int[AuthoredStats.Length];
                cost[selected] = PackFee;
                if (!TryBeginBoardPayment(cost, () => FinishPackOutcome(coin, coinVoice, selected),
                    PresentStoryPack))
                    ShowPackLines(new JArray(new JObject { ["text"] = "코인통을 사용할 수 없습니다." }), PackRoot);
                return;
            case PackNegotiate:
                ChoosePackNegotiation(selected);
                return;
            case PackToss:
                if (tutorialMiniGames == null) FinishPackGame(selected == (UnityEngine.Random.value < .5f ? 0 : 1));
                else
                {
                    currentState = StoryState.Duel;
                    SetYellowInputInteractable(false);
                    presentationController.ExitChoice();
                    tutorialMiniGames.StartCoinToss(selected == 0, (won, _) =>
                    {
                        tutorialMiniGames.Cleanup();
                        FinishPackGame(won);
                    });
                }
                return;
        }
        var choice = story["choices"][selected];
        var dialogue = voiced["choices"][selected];
        presentationController.ConfirmChoice((string)choice["text"]);
        switch ((string)choice["id"])
        {
            case "C1": ShowPackLines(dialogue["dialogue"], PackCoins); break;
            case "C2":
                ShowPackLines(dialogue["dialogue"], (string)choice["mini_game"]["type"] == "duel" ? PackDuel : PackToss);
                break;
            case "C3": BeginPackRequirement(dialogue); break;
            case "C4":
                Debug.Log((string)choice["shortcut"]["log_message"], this);
                ShowPackLines(null, PackRoot);
                break;
        }
    }

    private bool PackCondition(JToken condition)
    {
        for (int i = 0; i < MineLearningStation.AbilityKeys.Length; i++)
            if (statContainer.stats[i] < ((int?)condition?[MineLearningStation.AbilityKeys[i] + "_at_least"] ?? 0)) return false;
        return true;
    }

    private void ChoosePackNegotiation(int selected)
    {
        var route = PackNegotiationRoutes()[selected];
        if ((string)route["id"] == "N_CANCEL")
        {
            ShowPackLines(null, PackCoins);
            return;
        }
        Authored.storyPackNegotiated = true;
        bool success = PackCondition(route["condition"]);
        var outcome = route[success ? "success" : "failure"];
        Authored.storyPackFee = (int?)outcome?["payment_override"]?["amount"] ?? 0;
        if ((string)route["goto_choice_id"] == "C3" && success) { BeginPackRequirement(PackChoice(PackDialogueStory(), "C3")); return; }
        FinishPackOutcome(outcome, outcome, fallback: PackCoins);
    }

    private void BeginPackRequirement(JToken voicedChoice)
    {
        var requirement = PackChoice(PackStory(), "C3")["requirement"];
        Authored.storyPackTaskRemaining = PackRequirementSeconds;
        Authored.storyPackTaskWon = UnityEngine.Random.value < PackRequirementProbability(requirement);
        var lines = new JArray();
        AppendPackLines(lines, voicedChoice["dialogue"]);
        AppendPackLines(lines, voicedChoice["requirement"]["story_prompt_dialogue"]);
        lines.Add(new JObject { ["text"] = requirement["task"] });
        ShowPackLines(lines, PackTask);
    }

    private float PackRequirementProbability(JToken requirement)
    {
        int Read(string key) => statContainer.stats[Array.IndexOf(MineLearningStation.AbilityKeys,
            (string)requirement["required_stats"][key])];
        float effective = .75f * Read("primary") + .25f * Read("secondary");
        return Mathf.Clamp(.50f + .10f * (effective - (int)requirement["check"]["difficulty"]), .10f, .90f);
    }

    private IEnumerator RunStoryPackTask()
    {
        var requirement = PackChoice(PackStory(), "C3")["requirement"];
        SetYellowInputInteractable(false);
        currentState = StoryState.Transitioning;
        mineLaborInProgress = true;
        Authored.storyPackTaskRemaining = Mathf.Min(PackRequirementSeconds, Authored.storyPackTaskRemaining);
        SaveGuideCheckpoint();
        while (Authored.storyPackTaskRemaining > 0)
        {
            if (!collectingTax)
            {
                Authored.storyPackTaskRemaining = Mathf.Max(0, Authored.storyPackTaskRemaining - Time.deltaTime);
                uiController.SetStoryNotice($"요구사항 수행 중 / {Mathf.CeilToInt(Authored.storyPackTaskRemaining)}초");
            }
            yield return null;
        }
        uiController.SetStoryNotice(null);
        mineLaborInProgress = false;
        bool won = Authored.storyPackTaskWon;
        string key = won ? "success" : "failure";
        var voiced = PackChoice(PackDialogueStory(), "C3")["requirement"];
        var speech = new JArray();
        AppendPackLines(speech, voiced[key]["dialogue"]);
        AppendPackLines(speech, voiced[won ? "npc_advice_on_success" : "npc_advice_on_failure"]);
        FinishPackOutcome(requirement[key], new JObject { ["dialogue"] = speech });
    }

    private void RunStoryPackDuel()
    {
        if (duelMiniGame == null) { ShowPackLines(null, PackRoot); return; }
        currentState = StoryState.Duel;
        SetYellowInputInteractable(false);
        presentationController.ExitChoice();
        FrameActiveEncounter(true);
        duelMiniGame.BeginDuel(won =>
        {
            duelMiniGame.CleanupDuel();
            FinishPackGame(won);
        }, mineRules.duelHitTarget, statContainer.stats[0], ConsumeMineDuelHealth);
    }

    private void FinishPackGame(bool won)
    {
        string outcome = won ? "success" : "failure";
        FinishPackOutcome(PackChoice(PackStory(), "C2")["mini_game"][outcome],
            PackChoice(PackDialogueStory(), "C2")["mini_game"][outcome]);
    }

    private void FinishPackOutcome(JToken outcome, JToken voiced, int coin = -1, string fallback = PackRoot)
    {
        var result = outcome?["result"];
        var delta = MineLearningStation.AbilityKeys.Select(key => (int?)result?["stats"]?[key] ?? 0).ToArray();
        if (coin >= 0) delta[coin] = -PackFee;
        if (delta.Any(amount => amount != 0)) ApplyAuthoredDelta(delta);
        AddAuthoredFlags(result?["reward_flags"]?.Values<string>() ?? Enumerable.Empty<string>());
        bool completed = (bool?)result?[IsPackMain ? "main_progress" : "side_completed"] ?? false;
        if (completed) Authored.storyPackResolution = (string)result?["resolution"] ?? "worked";
        ShowPackLines(voiced?["dialogue"], completed ? (IsPackMain ? PetDelivery : PackLeave) : fallback);
    }

    private string PackChoiceHint(int index)
    {
        if (Authored?.eventId == null || (!HasHighStoryAbility(2) && !HasHighStoryAbility(3))) return null;
        float? probability = Authored.storyPackFrame switch
        {
            PackCoins when index < 4 => statContainer.stats[index] >= PackFee ? 1f : 0f,
            PackNegotiate => PackCondition(PackNegotiationRoutes()[index]["condition"]) ? 1f : 0f,
            PackToss => .5f,
            PackRoot when index == 0 => statContainer.stats.Any(stat => stat >= PackFee) ? 1f : 0f,
            PackRoot when index == 1 && (string)PackChoice(PackStory(), "C2")["mini_game"]["type"] == "gambling" => .5f,
            PackRoot when index == 2 => PackRequirementProbability(PackChoice(PackStory(), "C3")["requirement"]),
            PackRoot when index == 3 => 0f,
            _ => null
        };
        if (!probability.HasValue)
            return Authored.storyPackFrame == PackRoot && index == 1 ?
                "결투 결과는 전투로 결정됩니다." : null;
        string number = HasHighStoryAbility(2) ? $"성공 확률 {Mathf.RoundToInt(probability.Value * 100)}%" : null;
        string words = HasHighStoryAbility(3) ? probability.Value switch
        {
            < .25f => "가능성 없을 것 같아",
            < .50f => "괜찮은데",
            < .75f => "충분하군",
            _ => "쌉가능"
        } : null;
        return string.Join(" / ", new[] { number, words }.Where(text => text != null));
    }
}
