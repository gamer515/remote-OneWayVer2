using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static Constants;

public partial class DecisionManager
{
    private const string PackShortcutDirection = "shortcut-direction", PackShortcutFee = "shortcut-fee",
        PackShortcutTravel = "shortcut-travel", PackShortcutGame = "shortcut-game",
        PackShortcutToss = "shortcut-toss", PackShortcutCoinPlaying = "shortcut-coin-playing",
        PackShortcutDuelPlaying = "shortcut-duel-playing", PackShortcutResult = "shortcut-result",
        PackShortcutExit = "shortcut-exit";
    private const string ShortcutNpcName = "샛길의 사람";
    private Transform packShortcutActor;
    private bool packShortcutWalking;
    private float shortcutLaneX, shortcutLaneStartZ, shortcutLaneEndZ, shortcutLastZ;
    private string PackShortcutResultKey => activePlaceId + ":shortcut:result";

    private bool IsPackShortcutNpcFrame => Authored?.storyPackFrame is PackShortcutGame or PackShortcutToss
        or PackShortcutCoinPlaying or PackShortcutDuelPlaying or PackShortcutResult;

    private static JToken ShortcutSettings(JObject story) =>
        story["choices"].FirstOrDefault(choice => (string)choice["id"] == "C4")?["shortcut"] ?? story["shortcut"];

    private static int ShortcutCoinIndex(JToken token) =>
        Array.IndexOf(MineLearningStation.AbilityKeys, (string)token);

    private static (int type, int amount) ShortcutReward(JObject story)
    {
        var settings = ShortcutSettings(story);
        string coin = (string)settings?["reward_coin_type"] ?? "strength";
        int type = Array.IndexOf(MineLearningStation.AbilityKeys, coin);
        return (type, type >= 0 ? Mathf.Max(0, (int?)settings?["reward_amount"] ?? 1) : 0);
    }

    private static (int amount, int[] types) ShortcutFee(JObject story)
    {
        var toll = story["choices"].FirstOrDefault(choice => (string)choice["id"] == "C1");
        int baseFee = (int?)toll?["price"]?["base_fee"] ?? 0;
        if (baseFee > 0)
        {
            var types = toll["coin_options"]?.Select(option => ShortcutCoinIndex(option["id"]))
                .Where(index => index >= 0).Distinct().ToArray();
            return (HalfFee(baseFee), types ?? Array.Empty<int>());
        }
        var settings = ShortcutSettings(story);
        int fee = (int?)settings?["fee_base"] ?? 0;
        int type = ShortcutCoinIndex(settings?["fee_coin_type"]);
        return (fee > 0 ? HalfFee(fee) : 0, type >= 0 ? new[] { type } : Array.Empty<int>());
    }

    private static int HalfFee(int basis) => basis / 2 + basis % 2;

    private bool TryGetPackShortcut(int side, out Vector3[] route, out Transform actor)
    {
        route = null;
        actor = null;
        var place = envController.TerrainData.places.FirstOrDefault(entry => entry.placeId == activePlaceId);
        return place != null && (side == -1 || side == 1) &&
            envController.TryGetShortcutRoute(place, side, playerController.CurrentPosition.y, encounterStopDistance,
                out route, out actor);
    }

    private bool TryPresentPackShortcut(JObject story)
    {
        switch (Authored.storyPackFrame)
        {
            case PackShortcutDirection:
                ShowAuthoredCard("어느 쪽 샛길로 돌아갈까요?", "안내", new[] { "왼편으로 가기", "오른편으로 가기", "돌아가기" });
                break;
            case PackShortcutFee:
                var (amount, types) = ShortcutFee(story);
                if (amount <= 0 || types.Length == 0)
                {
                    Debug.LogWarning($"[샛길] {activePlaceId}: 이용료 기준액 또는 코인 계열이 없습니다.", this);
                    ReturnFromShortcutOffer("이 샛길의 이용료가 아직 정해지지 않았어.");
                    break;
                }
                ShowAuthoredCard("샛길을 이용하려면 이곳 주인에게 이용료를 내야 해요.", pendingEncounter?.DisplayName,
                    types.Select(type => $"{AuthoredStats[type]} 코인 {amount}개").Append("돌아가기").ToArray());
                break;
            case PackShortcutTravel:
            case PackShortcutExit:
                BeginPackShortcutWalk();
                break;
            case PackShortcutGame:
            case PackShortcutToss:
                if (!TryGetPackShortcut(Authored.storyPackShortcutSide, out _, out packShortcutActor) || !packShortcutActor)
                    ShowPackLines(null, PackShortcutExit);
                else if (Vector3.ProjectOnPlane(playerController.CurrentPosition - packShortcutActor.position, Vector3.up)
                    .sqrMagnitude < Mathf.Pow(encounterStopDistance - .05f, 2f))
                    BeginPackShortcutWalk();
                else if (Authored.storyPackFrame == PackShortcutGame)
                    ShowAuthoredCard("이쪽으로 지나가려고? 나랑 한 판 어때?", ShortcutNpcName,
                        new[] { "동전 던지기 한 판 할래요?", "저랑 한 판 겨뤄 볼래요?" });
                else ShowAuthoredCard("앞면이랑 뒷면 중 뭘 고를래?", ShortcutNpcName, new[] { "앞면", "뒷면" });
                break;
            case PackShortcutCoinPlaying:
                FinishPackShortcutGame(false, Authored.storyPackShortcutWon);
                break;
            case PackShortcutDuelPlaying:
                // An interrupted duel has no verdict to settle.
                ShowPackLines(null, PackShortcutExit);
                break;
            case PackShortcutResult:
                TryGetPackShortcut(Authored.storyPackShortcutSide, out _, out packShortcutActor);
                var receipt = storyEvents.FirstOrDefault(record => record.eventKey == PackShortcutResultKey);
                int reward = receipt?.quantity ?? 0;
                int rewardType = Array.IndexOf(MineLearningStation.AbilityKeys, receipt?.itemId);
                string text = Authored.storyPackShortcutWon
                    ? reward > 0 && rewardType >= 0 ? $"{AuthoredStats[rewardType]} 코인 {reward}개를 받았습니다." : "승리했습니다."
                    : "졌지만 길은 계속 갈 수 있어요.";
                ShowAuthoredCard(text, ShortcutNpcName, null);
                break;
            default: return false;
        }
        return true;
    }

    private bool TryAdvancePackShortcut()
    {
        int selected = selectedGearIndex;
        switch (Authored.storyPackFrame)
        {
            case PackShortcutDirection:
                if (selected == 2) ReturnFromShortcutOffer();
                else if (selected == 0 || selected == 1)
                {
                    int side = selected == 0 ? -1 : 1;
                    if (!TryGetPackShortcut(side, out _, out _))
                    {
                        Debug.LogWarning($"[샛길] {activePlaceId}: {(side < 0 ? "왼편" : "오른편")} 우회 경로를 확보할 수 없습니다.", this);
                        ReturnFromShortcutOffer("이쪽은 돌아갈 공간이 없어요.");
                    }
                    else
                    {
                        Authored.storyPackShortcutSide = side;
                        ShowPackLines(null, PackShortcutFee);
                    }
                }
                break;
            case PackShortcutFee:
                ChoosePackShortcutPayment(selected);
                break;
            case PackShortcutGame:
                if (selected == 0) ShowPackLines(null, PackShortcutToss);
                else if (selected == 1) StartPackShortcutDuel();
                break;
            case PackShortcutToss:
                if (selected == 0 || selected == 1) StartPackShortcutToss(selected == 0);
                break;
            case PackShortcutResult:
                ShowPackLines(null, PackShortcutExit);
                break;
            case PackShortcutTravel:
            case PackShortcutExit:
                if (!packShortcutWalking) BeginPackShortcutWalk();
                break;
            default: return false;
        }
        return true;
    }

    private void ReturnFromShortcutOffer(string notice = null)
    {
        if (mine != null) mine.shortcutPursuitInvoiceId = null;
        Authored.storyPackShortcutSide = 0;
        ShowPackLines(null, PackRoot);
        uiController.SetStoryNotice(notice);
    }

    private void ChoosePackShortcutPayment(int selected)
    {
        var (amount, types) = ShortcutFee(PackStory());
        if (selected == types.Length) { ReturnFromShortcutOffer(); return; }
        if (selected < 0 || selected >= types.Length || amount <= 0) return;
        var cost = new int[AuthoredStats.Length];
        cost[types[selected]] = amount;
        if (!AbilityPayment.CanPay(statContainer.stats, cost))
        {
            ReturnFromShortcutOffer("이용료를 내기에는 코인이 부족해요.");
            return;
        }
        string placeId = activePlaceId;
        if (!TryBeginBoardPayment(cost, () =>
        {
            if (activePlaceId != placeId || Authored.storyPackFrame != PackShortcutFee) return;
            if (!AbilityPayment.CanPay(statContainer.stats, cost) ||
                !TryGetPackShortcut(Authored.storyPackShortcutSide, out _, out _))
            {
                ReturnFromShortcutOffer("이용료를 낼 수 없거나 경로가 막혔어요.");
                return;
            }
            ApplyAuthoredDelta(cost.Select(value => -value).ToArray());
            ReceiveMinePayment(new StoryChoiceAction { abilityCost = cost, receiverId = pendingEncounter?.DisplayName });
            ShowPackLines(null, PackShortcutTravel);
        }, () => ReturnFromShortcutOffer(), useNpcBowl: true))
            ReturnFromShortcutOffer("코인통을 사용할 수 없습니다.");
    }

    private void BeginPackShortcutWalk()
    {
        if (npcPaymentConfirmed && npcPaymentRoot != null) return;
        if (!TryGetPackShortcut(Authored.storyPackShortcutSide, out var route, out var actor))
        {
            // A paid route may be restored before a changed map is repaired.
            ShowAuthoredCard("지금은 샛길을 불러올 수 없어요. 버튼을 누르면 다시 확인합니다.", "안내", null);
            return;
        }
        bool exiting = Authored.storyPackFrame == PackShortcutExit;
        var position = playerController.CurrentPosition;
        shortcutLaneX = route[1].x;
        shortcutLaneStartZ = route[1].z;
        shortcutLaneEndZ = route[2].z;
        shortcutLastZ = position.z;
        bool onSide = Mathf.Abs(position.x - route[1].x) < .6f &&
            position.z >= route[1].z - .05f && position.z <= route[2].z;
        if (!exiting && actor != null)
        {
            var stop = new Vector3(route[1].x, position.y, actor.position.z - encounterStopDistance);
            route = onSide ? new[] { stop } : new[] { route[0], route[1], stop };
        }
        else if (exiting && onSide)
            route = route.Skip(2).ToArray();
        packShortcutActor = exiting ? null : actor;
        packShortcutWalking = true;
        walkingForwardJourney = false;
        currentState = StoryState.MovingToEncounter;
        presentationController.ExitChoice();
        presentationController.ShowWalkingView();
        journeyBoardInput?.SetChoiceCount(0);
        SetYellowInputInteractable(false);
        uiController.SetStoryNotice(null);
        SaveGuideCheckpoint();
        SaveEncounterState("card");
        playerController.MoveAlongPath(route, GetJourneySpeedMultiplier());
    }

    private bool TryCompletePackShortcutWalk()
    {
        if (!packShortcutWalking) return false;
        packShortcutWalking = false;
        if (packShortcutActor != null)
        {
            if (Authored.storyPackFrame == PackShortcutTravel) ShowPackLines(null, PackShortcutGame);
            else PresentStoryPack();
        }
        else
        {
            if (mine != null) mine.shortcutPursuitInvoiceId = null;
            Authored.storyPackShortcutSide = 0;
            if (IsPackMain)
            {
                Authored.storyPackResolution = "shortcut";
                ShowPackLines(null, PetDelivery);
            }
            else ShowPackLines(null, PackLeave);
        }
        return true;
    }

    private void BeginPackShortcutGame(string frame)
    {
        Authored.storyPackFrame = frame;
        currentState = StoryState.Duel;
        SetYellowInputInteractable(false);
        journeyBoardInput?.SetChoiceCount(0);
        presentationController.ExitChoice();
        SaveGuideCheckpoint();
        SaveEncounterState("card");
    }

    private void StartPackShortcutToss(bool choseHeads)
    {
        if (tutorialMiniGames?.IsReady != true) { ShowPackLines(null, PackShortcutGame); return; }
        bool won = MineChapterSettings.Roll(session.RunSeed, 0, activePlaceId + ":shortcut:coin") < .5;
        Authored.storyPackShortcutWon = won;
        BeginPackShortcutGame(PackShortcutCoinPlaying);
        tutorialMiniGames.StartRoadsideCoinToss(won ? choseHeads : !choseHeads, won, uiController,
            _ => FinishPackShortcutGame(false, won));
    }

    private void StartPackShortcutDuel()
    {
        if (duelMiniGame == null) { ShowPackLines(null, PackShortcutGame); return; }
        BeginPackShortcutGame(PackShortcutDuelPlaying);
        FrameActiveEncounter(true);
        // Existing duel rules use a local health counter; the shortcut has no additional coin loss.
        duelMiniGame.BeginDuel(won => FinishPackShortcutGame(true, won),
            mineRules.duelHitTarget, statContainer.stats[0]);
    }

    private void FinishPackShortcutGame(bool duel, bool won)
    {
        if (Authored.storyPackFrame != (duel ? PackShortcutDuelPlaying : PackShortcutCoinPlaying)) return;
        Authored.storyPackShortcutWon = won;
        Authored.storyPackFrame = PackShortcutResult;
        var (type, amount) = ShortcutReward(PackStory());
        int reward = won ? amount : 0;
        string key = PackShortcutResultKey;
        if (!storyEvents.Any(record => record.eventKey == key))
        {
            var card = activeCards?.MainStory?.FirstOrDefault() ?? new Dialogue { speakerId = ShortcutNpcName };
            var record = NewStoryEvent(key, duel ? "duel_result" : "gamble_result", card);
            record.characterId = packShortcutActor != null ? packShortcutActor.name : ShortcutNpcName;
            record.characterName = ShortcutNpcName;
            if (reward > 0)
            {
                var delta = new int[AuthoredStats.Length];
                delta[type] = reward;
                ApplyAuthoredDelta(delta);
                record.itemId = MineLearningStation.AbilityKeys[type];
                record.quantity = reward;
                record.direction = "received";
            }
            RecordMiniGameResult(record, won);
        }
        PresentStoryPack();
    }
}
