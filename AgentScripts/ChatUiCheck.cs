using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;
using static Constants;

public static class ChatUiCheck
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    const string FixtureName = "ChatUi_CheckFixture";
    const string PreviewName = "ChatPanel_CheckPreview";

    static Transform Root() => UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/DecisionScene.unity")
        .GetRootGameObjects().Single(o => o.name == "_UI").transform.Find("Decision_Canvas/Decision_UI_Root");
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static GameObject Fixture(out ChatHistoryUi chat, out DecisionPresentationController presentation)
    {
        var root = Root();
        Assert(root.Find(PreviewName) == null, "Clean up previous preview first.");
        var original = root.Find("ChatPanel").gameObject;
        var preview = UnityEngine.Object.Instantiate(original, root, false);
        preview.name = PreviewName;
        preview.hideFlags = HideFlags.DontSave;
        original.SetActive(false);
        preview.SetActive(true);
        chat = preview.GetComponent<ChatHistoryUi>();
        var fixture = new GameObject(FixtureName) { hideFlags = HideFlags.HideAndDontSave };
        fixture.SetActive(false);
        var main = fixture.AddComponent<MainStoryUi>();
        Set(main, "chatHistory", chat);
        var ui = fixture.AddComponent<UiController>();
        Set(ui, "mainStoryUi", main);
        presentation = new DecisionPresentationController(ui);
        return fixture;
    }
    public static object Cleanup()
    {
        var root = Root();
        var preview = root.Find(PreviewName);
        if (preview != null) UnityEngine.Object.DestroyImmediate(preview.gameObject);
        root.Find("ChatPanel").gameObject.SetActive(true);
        foreach (var fixture in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.name == FixtureName && g.scene.IsValid()))
            UnityEngine.Object.DestroyImmediate(fixture);
        Canvas.ForceUpdateCanvases();
        return new { removedPreview = root.Find(PreviewName) == null,
            messages = root.Find("ChatPanel").GetComponent<ChatHistoryUi>().MessageCount,
            dirty = root.gameObject.scene.isDirty };
    }

    public static object Verify()
    {
        if (Application.isPlaying) throw new Exception("Run in Edit mode with isolated data.");
        int checks = 0;
        var singleton = typeof(SaveIOService).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
        object previousIO = singleton.GetValue(null);
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ChatUiChecks", Guid.NewGuid().ToString("N")));
        GameObject fixture = null;
        try
        {
            fixture = Fixture(out var chat, out var presentation);
            var ui = fixture.GetComponent<UiController>();
            Assert(chat.GetComponentsInChildren<RectTransform>(true).All(rect => Mathf.Abs(rect.localPosition.z) < 0.01f),
                "Chat elements are on different canvas depths."); checks++;
            presentation.ShowDialogue(new Dialogue { text = "{어이, 자네. 여기 있으면 안 돼.}", speakerId = "Guide", type = "Next" });
            chat.RefreshLayout();
            Assert(chat.MessageCount == 1, "Dialogue was not appended."); checks++;
            Assert(chat.GetComponentsInChildren<TextMeshProUGUI>().Any(t => t.text.Contains("로키") && !t.text.Contains("{")), "Speaker/text sanitization failed."); checks++;
            presentation.ShowOptions(new[] { "하나", "둘", "셋", "넷" }, 0);
            presentation.ShowOptions(new[] { "하나", "둘", "셋", "넷" }, 1);
            Assert(chat.MessageCount == 1, "Previewing choices added messages."); checks++;
            var draft = chat.transform.Find("Composer/DraftText").GetComponent<TextMeshProUGUI>();
            var composer = (RectTransform)chat.transform.Find("Composer");
            var send = chat.transform.Find("Composer/SendButton").GetComponent<UnityEngine.UI.Button>();
            Assert(chat.DraftText == "둘" && chat.IsTyping && draft.text == "둘", "Choice draft did not follow the selected gear."); checks++;
            Assert(send.interactable, "Valid choice cannot be sent."); checks++;
            var typing = (RectTransform)chat.transform.Find("TypingIndicator");
            Assert(typing.parent == chat.transform && typing.GetComponent<ChatBubbleGraphic>() != null,
                "Typing dots did not have a separate floating bubble."); checks++;
            chat.RefreshLayout();
            Assert(typing.anchoredPosition.y >= composer.anchoredPosition.y + composer.rect.height + 16f,
                "Typing bubble overlapped the composer."); checks++;
            Assert(chat.GetComponent<UnityEngine.UI.ScrollRect>().viewport.offsetMin.y >=
                typing.anchoredPosition.y + typing.rect.height + 12f, "Typing bubble overlapped history."); checks++;
            Assert(Mathf.Abs(composer.anchoredPosition.y - 12f) < 0.01f &&
                Quaternion.Angle(chat.transform.localRotation, Quaternion.identity) < 0.01f,
                "Composer did not line up with the bottom of the chat frame."); checks++;
            var dots = typing.GetComponentsInChildren<ChatBubbleGraphic>().Where(g => g.transform != typing).ToArray();
            Call(chat, "AnimateTypingDots", 0f);
            var startHeights = dots.Select(dot => dot.rectTransform.anchoredPosition.y).ToArray();
            Call(chat, "AnimateTypingDots", 0.25f);
            Assert(dots.Select((dot, index) => Mathf.Abs(dot.rectTransform.anchoredPosition.y - startHeights[index])).Any(delta => delta > 1f),
                "Typing dots did not move across animation frames."); checks++;
            Assert(dots.Select(dot => dot.rectTransform.anchoredPosition.y).Distinct().Count() > 1 &&
                dots.All(dot => dot.rectTransform.anchoredPosition.y >= -23.01f && dot.rectTransform.anchoredPosition.y <= -12.99f),
                "Typing wave did not stay staggered inside the bubble."); checks++;
            Call(chat, "LateUpdate");
            Assert(chat.transform.Find("TypingIndicator").GetComponentsInChildren<ChatBubbleGraphic>()
                .Select(dot => dot.color.a).Distinct().Count() > 1, "Typing dots did not animate in sequence."); checks++;
            Assert(send.onClick.GetPersistentEventCount() == 1 &&
                send.onClick.GetPersistentMethodName(0) == nameof(DecisionManager.OnJourneyYellowPressed) &&
                ((DecisionManager)send.onClick.GetPersistentTarget(0)).gameObject.scene == Root().gameObject.scene,
                "Send button is not wired to the decision scene confirmation flow."); checks++;
            presentation.ShowOptions(new[] { "잠긴 답 (잠김)", "둘", "셋", "넷" }, 0);
            Assert(!send.interactable && chat.MessageCount == 1, "Locked draft became confirmable."); checks++;
            presentation.ShowOptions(new[] { "하나", "둘", "셋", "넷" }, -1);
            Assert(chat.IsTyping && chat.DraftText == "" && !send.interactable, "Unselected draft did not show pending typing."); checks++;
            string longDraft = "지나온 길을 돌아보며 지금의 선택이 다음 만남에 어떤 영향을 주는지 생각해 보았다. 당신의 이야기를 좀 더 자세히 들려주세요.";
            presentation.ShowOptions(new[] { longDraft, "둘", "셋", "넷" }, 0);
            chat.RefreshLayout();
            var draftBounds = draft.GetPreferredValues(draft.text, draft.rectTransform.rect.width, Mathf.Infinity);
            Assert(composer.rect.height > 100f && draftBounds.y <= draft.rectTransform.rect.height + 1f,
                "Long choice text overflowed the composer."); checks++;
            var draftScroll = chat.GetComponent<UnityEngine.UI.ScrollRect>();
            Assert(draftScroll.viewport.offsetMin.y >= composer.anchoredPosition.y + composer.rect.height + 20f,
                "Composer overlapped dialogue history."); checks++;
            Assert(!Root().Find("Encounter_Display/Event_View/Walking_View_Back/Walking_View/Option_Text (TMP)").gameObject.activeSelf,
                "Legacy choice text remains over the main view."); checks++;
            presentation.ConfirmChoice("알려 주세요.");
            Assert(chat.MessageCount == 2, "Confirmed choice was not appended."); checks++;
            Assert(!chat.IsTyping && chat.DraftText == "" && !send.interactable, "Confirmation did not clear the draft."); checks++;
            var scroll = chat.GetComponent<UnityEngine.UI.ScrollRect>();
            var player = scroll.content.GetChild(1).Find("Bubble") as RectTransform;
            Assert(player.anchorMin.x == 1f, "Player choice was not aligned right."); checks++;
            chat.RefreshLayout();
            foreach (var graphic in chat.GetComponentsInChildren<ChatBubbleGraphic>())
                Assert(graphic.GetComponent<CanvasRenderer>() != null && graphic.canvasRenderer.GetMesh()?.vertexCount > 0,
                    "Chat graphic has no renderable mesh: " + graphic.name);
            checks++;
            presentation.ShowWalkingView();
            Assert(chat.gameObject.activeInHierarchy && chat.MessageCount == 2, "Walking hid/cleared history."); checks++;
            presentation.ShowGuideAllocation("기어 위: 추가 / 아래: 빼기", "체5 민5 지5 매5");
            presentation.ShowGuideAllocation("기어 위: 추가 / 아래: 빼기", "체6 민4 지5 매5");
            Assert(chat.MessageCount == 2, "Live allocation status flooded chat."); checks++;
            Assert(!chat.IsTyping && !send.interactable && draft.text.Contains("체6"), "Allocation status did not use the composer safely."); checks++;
            chat.AppendDialogue("  ");
            Assert(chat.MessageCount == 2, "Whitespace message appended."); checks++;

            // Use the real choice executor with a temporary save root, never the player's saves.
            var isolated = typeof(SaveIOService).GetConstructor(Hidden, null, new[] { typeof(string) }, null)
                .Invoke(new object[] { directory });
            singleton.SetValue(null, isolated);
            var save = new SaveManager();
            var stats = fixture.AddComponent<StatContainer>();
            Set(stats, "statEntries", new List<StatContainer.StatEntry> {
                new StatContainer.StatEntry(), new StatContainer.StatEntry(),
                new StatContainer.StatEntry(), new StatContainer.StatEntry() });
            stats.SetStats(new[] { 5, 5, 5, 5 });
            var coins = fixture.AddComponent<CoinDropController>();
            coins.InitializeInventory(new[] { 10, 10, 10, 10 });
            var manager = fixture.AddComponent<DecisionManager>();
            var session = new DecisionSession(new OmnibusData {
                chapters = new List<ChapterInfo> { new ChapterInfo {
                    chapterId = "Initial", episodeIds = new List<string> { "Initial_01" } } },
                lastPlayableChapterId = "Initial"
            }, 1, 7, 0, 0, 0) { ScenarioPath = "Initial/Initial_01" };
            Set(manager, "session", session); Set(manager, "saveService", new DecisionSaveService(save));
            Set(manager, "statContainer", stats); Set(manager, "coinDropController", coins);
            Set(manager, "guideProgress", new GuideProgress()); Set(manager, "storyEvents", new List<StoryEventRecord>());
            Set(manager, "presentationController", presentation); Set(manager, "storyVisit", 1);
            Set(manager, "activePlaceId", "guide_01");
            Set(manager, "activeEncounterPath", "Initial/Initial_01/Encounters/guide");
            var card = new Dialogue { text = "자네, 어떤 답을 고르겠나?", speakerId = "Guide",
                type = "Choice", options = new[] { "잠긴 답", "알겠습니다.", "다른 답", "마지막 답" },
                choiceActions = new[] {
                    new StoryChoiceAction { action = "continue", requiresUnlocks = new[] { "chat_test_locked" } },
                    new StoryChoiceAction { action = "continue", choiceId = "chat_test_yes" },
                    new StoryChoiceAction { action = "continue" },
                    new StoryChoiceAction { action = "continue" } } };
            Set(manager, "activeCards", new ScenarioData { MainStory = new List<Dialogue> { card } });
            Set(manager, "activeCardIndex", 0); Set(manager, "currentState", StoryState.WaitingForChoice);
            var terrain = new TerrainData { places = new PlaceData[0] };
            Set(manager, "encounterFlow", new EncounterFlowController(terrain,
                new TerrainPlaceRegistry(terrain, Vector3.zero, 10f), 10f));
            var boardInput = fixture.AddComponent<JourneyMapKit.JourneyBoardInput>();
            boardInput.onGearSelected.AddListener(index => Call(manager, "HandleEncounterGearSelection", index));
            presentation.ShowWalkingView();
            for (int index = 0; index < 4; index++)
            {
                boardInput.SelectGear(index);
                string expected = card.options[index] + (index == 0 ? " (잠김)" : "");
                Assert(chat.DraftText == expected && chat.IsTyping,
                    "Gear input did not show its matching option in the chat composer: " + index); checks++;
                Assert(chat.MessageCount == 2 && send.interactable == (index != 0),
                    "Gear preview recorded a choice or allowed a locked option."); checks++;
            }
            boardInput.SelectGear(-1);
            Assert(chat.IsTyping && chat.DraftText == "" && !send.interactable,
                "Neutral gear kept a stale selected reply."); checks++;
            presentation.ExitChoice();
            Set(manager, "currentState", StoryState.ShowingStory);
            boardInput.SelectGear(1);
            Assert(!chat.IsTyping && chat.DraftText == "",
                "A non-choice dialogue showed an unrelated option."); checks++;
            Set(manager, "currentState", StoryState.WaitingForChoice);
            Set(manager, "selectedGearIndex", 0);
            Call(manager, "ExecuteStoryChoice", card);
            Assert(chat.MessageCount == 2, "Locked choice was recorded."); checks++;
            Set(manager, "selectedGearIndex", 1);
            Call(manager, "ExecuteStoryChoice", card);
            Assert(chat.MessageCount == 3, "Real confirmed choice missing."); checks++;
            Call(manager, "ExecuteStoryChoice", card);
            Assert(chat.MessageCount == 3, "Checkpoint replay duplicated choice."); checks++;

            // Long Korean messages must grow vertically and stay inside their viewport width.
            string longText = string.Concat(Enumerable.Repeat("지나온 길을 돌아보며 지금의 선택이 다음 만남에 어떤 영향을 주는지 생각해 보았다. ", 12));
            chat.AppendDialogue(longText);
            for (int i = 0; i < 12; i++) chat.AppendDialogue("지난 대사 " + i);
            chat.RefreshLayout();
            Assert(scroll.content.rect.height > scroll.viewport.rect.height, "History did not become scrollable."); checks++;
            Assert(Mathf.Abs(scroll.verticalNormalizedPosition) < 0.001f, "New message did not scroll to the bottom."); checks++;
            foreach (Transform row in scroll.content)
            {
                var bubble = (RectTransform)row.Find("Bubble");
                var text = bubble.GetComponentInChildren<TextMeshProUGUI>();
                var bounds = text.GetPreferredValues(text.text, text.rectTransform.rect.width, Mathf.Infinity);
                Assert(bounds.y <= text.rectTransform.rect.height + 1f, "Message text was clipped vertically.");
                Assert(bubble.rect.width <= scroll.content.rect.width + 1f, "Bubble overflowed viewport width.");
            }
            checks++;
            var events = UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            scroll.OnScroll(new UnityEngine.EventSystems.PointerEventData(events) { scrollDelta = new Vector2(0f, 3f) });
            float previousPosition = scroll.verticalNormalizedPosition;
            Assert(previousPosition > 0f, "Mouse wheel did not scroll to older messages."); checks++;
            chat.RefreshLayout();
            Assert(Mathf.Abs(scroll.verticalNormalizedPosition - previousPosition) < 0.001f, "Reading older messages snapped to bottom."); checks++;
            return new { checks, isolatedSaveRoot = directory, contentHeight = scroll.content.rect.height,
                viewportHeight = scroll.viewport.rect.height, wheelPosition = previousPosition };
        }
        finally
        {
            singleton.SetValue(null, previousIO);
            Cleanup();
        }
    }

    public static object Preview()
    {
        if (Application.isPlaying) throw new Exception("Edit mode preview only.");
        Fixture(out var chat, out var presentation);

        presentation.ShowDialogue(new Dialogue { type = "Next", speakerId = "Guide", text = "여기는 왕국 전용 광산이라고." });
        presentation.ConfirmChoice("당신은 누구인가요?");

        presentation.ShowOptions(new[] { "당신의 이야기를 더 들려주세요.", "알겠습니다.", "다른 곳으로 갈게요.", "잠시만요." }, 0);
        chat.RefreshLayout();
        return new { previewMessages = chat.MessageCount, draft = chat.DraftText, typing = chat.IsTyping, temporary = true };
    }
}
