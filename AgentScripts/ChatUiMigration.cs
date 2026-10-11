using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;

public static class ChatUiRoundedMigration
{
    const string ScenePath = "Assets/Scenes/DecisionScene.unity";
    const string RootPath = "_UI/Decision_Canvas/Decision_UI_Root";
    const string WalkingPath = "Encounter_Display/Event_View/Walking_View_Back/Walking_View";

    static Transform Find(string path)
    {
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) throw new Exception("DecisionScene must be open.");
        var parts = path.Split('/');
        var root = scene.GetRootGameObjects().Single(o => o.name == parts[0]).transform;
        return root.Find(string.Join("/", parts.Skip(1))) ?? throw new Exception(path);
    }

    static void Record(UnityEngine.Object value) => Undo.RecordObject(value, "Add conversation panel");
    static void Persist(UnityEngine.Object value)
    {
        EditorUtility.SetDirty(value);
        if (PrefabUtility.IsPartOfPrefabInstance(value))
            PrefabUtility.RecordPrefabInstancePropertyModifications(value);
    }
    static T Component<T>(GameObject gameObject) where T : UnityEngine.Component
    {
        var existing = gameObject.GetComponent<T>();
        return existing != null ? existing : Undo.AddComponent<T>(gameObject);
    }
    static RectTransform Create(string name, Transform parent)
    {
        var existing = parent.Find(name);
        if (existing != null) return (RectTransform)existing;
        var gameObject = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(gameObject, "Add conversation panel");
        Undo.SetTransformParent(gameObject.transform, parent, "Add conversation panel");
        gameObject.transform.localPosition = Vector3.zero;
        gameObject.transform.localRotation = Quaternion.identity;
        gameObject.transform.localScale = Vector3.one;
        return (RectTransform)gameObject.transform;
    }
    static void TopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        Record(rect);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        var position = rect.localPosition; position.z = 0f; rect.localPosition = position;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        Persist(rect);
    }
    static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        Record(rect);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = min; rect.offsetMax = max;
        var position = rect.localPosition; position.z = 0f; rect.localPosition = position;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        Persist(rect);
    }
    static UnityEngine.UI.Image Image(RectTransform rect, Color color, bool raycast = false)
    {
        var image = Component<UnityEngine.UI.Image>(rect.gameObject);
        Record(image); image.color = color; image.raycastTarget = raycast; Persist(image);
        return image;
    }
    static TextMeshProUGUI Text(RectTransform rect, TextMeshProUGUI source, string value,
        float size, TextAlignmentOptions alignment)
    {
        var label = Component<TextMeshProUGUI>(rect.gameObject);
        Record(label); label.font = source.font; label.fontSharedMaterial = source.fontSharedMaterial;
        label.fontSize = size; label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = alignment; label.color = new Color(0.22f, 0.16f, 0.10f, 1f);
        label.margin = Vector4.zero; label.raycastTarget = false; label.text = value;
        Persist(label); return label;
    }
    static void Field(UnityEngine.Object owner, string name, UnityEngine.Object value)
    {
        Record(owner);
        var serialized = new SerializedObject(owner);
        var property = serialized.FindProperty(name) ?? throw new Exception(name);
        property.objectReferenceValue = value; serialized.ApplyModifiedProperties(); Persist(owner);
    }
    static void Hide(Transform target)
    {
        Record(target.gameObject); target.gameObject.SetActive(false); Persist(target.gameObject);
    }

    public static object Apply()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play before migrating.");
        var root = Find(RootPath);
        var walking = root.Find(WalkingPath);
        var mainStory = root.Find("Encounter_Display").GetComponent<MainStoryUi>();
        var dialogue = walking.Find("Front_Dialogue_Text (TMP)").GetComponent<TextMeshProUGUI>();
        var option = walking.Find("Option_Text (TMP)").GetComponent<TextMeshProUGUI>();
        var frame = (RectTransform)root.Find("JourneyInterfaceV3_Frames/Frame_Map_370x556");
        if (root.Find("ChatPanel") != null) throw new Exception("ChatPanel already exists.");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace face with conversation panel");
        try
        {
            Hide(root.Find("Face_Display"));


            Hide(dialogue.transform);

            // Keep the middle chat and right map as separate columns above the coin rack.
            TopLeft(frame, 1159.01f, -16f, 370f, 490f);
            var panel = Create("ChatPanel", root);
            TopLeft(panel, 1159.01f, -16f, 370f, 490f);
            var background = Create("Background", panel);
            Stretch(background, new Vector2(14f, 14f), new Vector2(-14f, -14f));
            Image(background, new Color(0.956f, 0.941f, 0.867f, 1f));
            var header = Create("Header", panel);
            TopLeft(header, 28f, -22f, 691f, 42f);
            Text(header, dialogue, "대화 기록", 32f, TextAlignmentOptions.MidlineLeft);
            var divider = Create("HeaderDivider", panel);
            TopLeft(divider, 26f, -70f, 695f, 2f);
            Image(divider, new Color(0.64f, 0.49f, 0.28f, 0.55f));

            var viewport = Create("Viewport", panel);
            Stretch(viewport, new Vector2(26f, 24f), new Vector2(-42f, -84f));
            Image(viewport, Color.white, true);
            var mask = Component<UnityEngine.UI.Mask>(viewport.gameObject);
            Record(mask); mask.showMaskGraphic = false; Persist(mask);
            var content = Create("Content", viewport);
            Record(content);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero; Persist(content);
            var layout = Component<UnityEngine.UI.VerticalLayoutGroup>(content.gameObject);
            Record(layout); layout.spacing = 14f; layout.padding = new RectOffset(2, 2, 8, 8);
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false; Persist(layout);
            var minimum = Component<UnityEngine.UI.LayoutElement>(content.gameObject);
            Record(minimum); minimum.minHeight = 382f; Persist(minimum);
            var fitter = Component<UnityEngine.UI.ContentSizeFitter>(content.gameObject);
            Record(fitter);
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize; Persist(fitter);

            var empty = Create("EmptyLabel", panel);
            Stretch(empty, new Vector2(30f, 30f), new Vector2(-30f, -84f));
            var emptyLabel = Text(empty, dialogue, "대화가 시작되면\n여기에 기록됩니다.", 28f, TextAlignmentOptions.Center);
            Record(emptyLabel); emptyLabel.color = new Color(0.48f, 0.39f, 0.27f, 1f); Persist(emptyLabel);
            var template = Create("MessageTemplate", panel);
            TopLeft(template, 0f, 0f, 600f, 100f);
            var messageTemplate = Text(template, dialogue, "", 30f, TextAlignmentOptions.TopLeft);
            Hide(template);

            var track = Create("Scrollbar", panel);
            Record(track); track.anchorMin = new Vector2(1f, 0f); track.anchorMax = Vector2.one;
            track.pivot = new Vector2(1f, 0.5f);
            track.offsetMin = new Vector2(-28f, 24f); track.offsetMax = new Vector2(-20f, -84f); Persist(track);
            Image(track, new Color(0.62f, 0.48f, 0.28f, 0.14f), true);
            var handle = Create("Handle", track);
            Stretch(handle, Vector2.zero, Vector2.zero);
            var handleImage = Image(handle, new Color(0.62f, 0.46f, 0.24f, 0.65f), true);
            var scrollbar = Component<UnityEngine.UI.Scrollbar>(track.gameObject);
            Record(scrollbar); scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop; Persist(scrollbar);
            var scroll = Component<UnityEngine.UI.ScrollRect>(panel.gameObject);
            Record(scroll); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f; scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalNormalizedPosition = 0f; Persist(scroll);

            var chat = Component<ChatHistoryUi>(panel.gameObject);
            Field(chat, "scrollRect", scroll); Field(chat, "content", content);
            Field(chat, "messageTemplate", messageTemplate); Field(chat, "emptyLabel", emptyLabel);
            Field(mainStory, "chatHistory", chat);

            // Keep the current choice and live allocation status visible on the large game view.
            var optionRect = option.rectTransform;
            Record(optionRect);
            optionRect.anchorMin = new Vector2(0.25f, 0f);
            optionRect.anchorMax = new Vector2(0.98f, 0f);
            optionRect.pivot = new Vector2(0.5f, 0f);
            optionRect.anchoredPosition = new Vector2(0f, 22f);
            optionRect.sizeDelta = new Vector2(0f, 110f); Persist(optionRect);
            Record(option); option.fontSize = 28f; option.alignment = TextAlignmentOptions.Center; Persist(option);
            Hide(option.transform);
            ArrangeChatAndMap(root);
            Canvas.ForceUpdateCanvases();
            chat.RefreshLayout();
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            EditorSceneManager.SaveScene(root.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            return new { panel = panel.name, width = panel.rect.width, height = panel.rect.height,
                faceHidden = !root.Find("Face_Display").gameObject.activeSelf,
                scrollWired = scroll.content == content && scroll.viewport == viewport };
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }



    static ChatBubbleGraphic Rounded(RectTransform rect, Color color, float radius, bool raycast = false)
    {
        var oldImage = rect.GetComponent<UnityEngine.UI.Image>();
        if (oldImage != null) Undo.DestroyObjectImmediate(oldImage);
        Component<CanvasRenderer>(rect.gameObject);
        var graphic = Component<ChatBubbleGraphic>(rect.gameObject);
        Record(graphic); graphic.color = color; graphic.cornerRadius = radius;
        graphic.raycastTarget = raycast; graphic.showTail = false; graphic.sendArrow = false;
        graphic.SetAllDirty();
        Persist(graphic); return graphic;
    }

    // Move chat toward the main view and place the divider and scroll rail between chat and map.
    static void ArrangeChatAndMap(Transform root)
    {
        const float chatX = 1080.22f, chatWidth = 423.78f;
        var frame = (RectTransform)root.Find("JourneyInterfaceV3_Frames/Frame_Map_370x556");
        TopLeft(frame, chatX, -16f, chatWidth, 490f);
        var divider = (RectTransform)root.Find("JourneyInterfaceV3_Frames/Divider_70x1080");
        TopLeft(divider, 1508f, 0f, 24f, 506f);
        var panel = (RectTransform)root.Find("ChatPanel");
        TopLeft(panel, chatX, -16f, chatWidth, 490f);
        foreach (var child in panel.GetComponentsInChildren<RectTransform>(true))
        {
            Record(child);
            var position = child.localPosition;
            position.z = 0f;
            child.localPosition = position;
            child.localRotation = Quaternion.identity;
            Persist(child);
        }
        var source = panel.Find("MessageTemplate").GetComponent<TextMeshProUGUI>();
        Rounded((RectTransform)panel.Find("Background"), new Color(0.97f, 0.95f, 0.89f, 1f), 22f);
        TopLeft((RectTransform)panel.Find("Header"), 24f, -16f, chatWidth - 48f, 38f);
        var header = panel.Find("Header").GetComponent<TextMeshProUGUI>();
        Record(header); header.fontSize = 28f; Persist(header);
        TopLeft((RectTransform)panel.Find("HeaderDivider"), 22f, -60f, chatWidth - 44f, 1.5f);
        Stretch((RectTransform)panel.Find("Viewport"), new Vector2(18f, 144f), new Vector2(-18f, -74f));
        Stretch((RectTransform)panel.Find("EmptyLabel"), new Vector2(28f, 144f), new Vector2(-28f, -74f));
        Record(source); source.fontSize = 25f; Persist(source);
        var scrollTrack = (RectTransform)panel.Find("Scrollbar");
        Record(scrollTrack);
        scrollTrack.offsetMin = new Vector2(13f, 144f);
        scrollTrack.offsetMax = new Vector2(19f, -74f);
        Persist(scrollTrack);

        var mapFrame = (RectTransform)root.Find("JourneyInterfaceV3_Frames/Frame_Face_384x520");
        TopLeft(mapFrame, 1536f, -16f, 370f, 490f);
        Record(mapFrame.gameObject); mapFrame.gameObject.SetActive(true); Persist(mapFrame.gameObject);
        var map = (RectTransform)root.Find("RawImage");
        TopLeft(map, 1536f, -16f, 370f, 490f);
        Record(map.gameObject); map.gameObject.SetActive(true); Persist(map.gameObject);
        var mapView = (RectTransform)map.Find("RawImage_Road_View_And_Map");
        Stretch(mapView, new Vector2(5f, 10f), new Vector2(-5f, -10f));
        Record(mapView.gameObject); mapView.gameObject.SetActive(true); Persist(mapView.gameObject);
        if (mapView.GetComponent<UnityEngine.UI.RawImage>().texture == null)
            throw new Exception("Existing map texture is missing.");
        Hide(root.Find("Face_Display"));

        var composer = Create("Composer", panel);
        Record(composer);
        composer.anchorMin = Vector2.zero; composer.anchorMax = new Vector2(1f, 0f);
        composer.pivot = new Vector2(0.5f, 0f);
        composer.anchoredPosition = new Vector2(0f, 12f);
        composer.sizeDelta = new Vector2(-36f, 100f); Persist(composer);
        Rounded(composer, new Color(0.85f, 0.84f, 0.76f, 1f), 24f);
        var stateRect = Create("DraftState", composer);
        TopLeft(stateRect, 18f, -10f, chatWidth - 122f, 24f);
        var stateLabel = Text(stateRect, source, "내 답변", 18f, TextAlignmentOptions.MidlineLeft);
        Record(stateLabel); stateLabel.color = new Color(0.49f, 0.39f, 0.24f, 1f); Persist(stateLabel);
        var draftRect = Create("DraftText", composer);
        Stretch(draftRect, new Vector2(18f, 14f), new Vector2(-64f, -36f));
        var draftLabel = Text(draftRect, source, "선택지를 기다리는 중...", 25f, TextAlignmentOptions.TopLeft);
        var typing = (RectTransform)(panel.Find("TypingIndicator") ?? composer.Find("TypingIndicator"));
        if (typing == null) typing = Create("TypingIndicator", panel);
        else if (typing.parent != panel) Undo.SetTransformParent(typing, panel, "Float typing indicator above the composer");
        Record(typing);
        typing.anchorMin = typing.anchorMax = typing.pivot = new Vector2(1f, 0f);
        typing.localRotation = Quaternion.identity;
        typing.localScale = Vector3.one;
        var typingPosition = typing.localPosition; typingPosition.z = 0f; typing.localPosition = typingPosition;
        typing.anchoredPosition = new Vector2(-18f, 128f);
        typing.sizeDelta = new Vector2(76f, 44f); Persist(typing);
        var typingBubble = Rounded(typing, new Color(0.74f, 0.77f, 0.65f, 1f), 18f);
        Record(typingBubble); typingBubble.showTail = true; typingBubble.tailRight = true; Persist(typingBubble);
        var dots = new ChatBubbleGraphic[3];
        for (int index = 0; index < 3; index++)
        {
            var dot = Create("Dot_" + index, typing);
            TopLeft(dot, 18f + index * 16f, -18f, 8f, 8f);
            dots[index] = Rounded(dot, new Color(0.72f, 0.53f, 0.19f, 1f), 4f);
            Record(dots[index]); dots[index].borderWidth = 0f; Persist(dots[index]);
        }
        var send = Create("SendButton", composer);
        Record(send);
        send.anchorMin = send.anchorMax = new Vector2(1f, 0.5f);
        send.pivot = new Vector2(0.5f, 0.5f);
        send.anchoredPosition = new Vector2(-30f, 0f);
        send.sizeDelta = new Vector2(42f, 42f); Persist(send);
        var sendBackground = Rounded(send, new Color(0.64f, 0.45f, 0.24f, 1f), 21f, true);
        var icon = Create("SendIcon", send);
        Stretch(icon, new Vector2(7f, 7f), new Vector2(-7f, -7f));
        var iconGraphic = Rounded(icon, new Color(0.99f, 0.97f, 0.88f, 1f), 0f);
        Record(iconGraphic); iconGraphic.sendArrow = true; Persist(iconGraphic);
        var button = Component<UnityEngine.UI.Button>(send.gameObject);
        Record(button); button.targetGraphic = sendBackground;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.1f, 1f, 1f);
        colors.pressedColor = new Color(0.8f, 0.78f, 0.72f, 1f);
        colors.disabledColor = new Color(0.78f, 0.76f, 0.67f, 0.5f);
        button.colors = colors;
        button.interactable = false;
        var manager = root.gameObject.scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<DecisionManager>(true)).SingleOrDefault();
        if (manager == null) throw new Exception("DecisionManager is missing.");
        bool wired = Enumerable.Range(0, button.onClick.GetPersistentEventCount())
            .Any(i => button.onClick.GetPersistentTarget(i) == manager &&
                button.onClick.GetPersistentMethodName(i) == nameof(DecisionManager.OnJourneyYellowPressed));
        if (!wired) UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, manager.OnJourneyYellowPressed);
        Persist(button);

        var chat = panel.GetComponent<ChatHistoryUi>();
        Field(chat, "composer", composer); Field(chat, "draftLabel", draftLabel);
        Field(chat, "draftStateLabel", stateLabel); Field(chat, "typingIndicator", typing);
        Field(chat, "sendButton", button);
        Record(chat);
        var chatSerialized = new SerializedObject(chat);
        var dotField = chatSerialized.FindProperty("typingDots"); dotField.arraySize = dots.Length;
        for (int i = 0; i < dots.Length; i++) dotField.GetArrayElementAtIndex(i).objectReferenceValue = dots[i];
        chatSerialized.ApplyModifiedProperties(); Persist(chat);
        Hide(root.Find(WalkingPath + "/Option_Text (TMP)"));
        Canvas.ForceUpdateCanvases();
        chat.SetComposerVisible(false);
        chat.RefreshLayout();
    }

    public static object RestoreMap()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play before correcting layout.");
        var root = Find(RootPath);
        var texture = root.Find("RawImage/RawImage_Road_View_And_Map").GetComponent<UnityEngine.UI.RawImage>().texture;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Keep chat and map in separate columns");
        try
        {
            ArrangeChatAndMap(root);
            if (root.Find("RawImage/RawImage_Road_View_And_Map").GetComponent<UnityEngine.UI.RawImage>().texture != texture)
                throw new Exception("Map texture changed unexpectedly.");
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            if (!EditorSceneManager.SaveScene(root.gameObject.scene)) throw new Exception("Scene save failed.");
            Undo.CollapseUndoOperations(group);
            return Inspect();
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static object Inspect()
    {
        var root = Find(RootPath);
        var panel = root.Find("ChatPanel");
        var chat = panel.GetComponent<ChatHistoryUi>();
        var scroll = panel.GetComponent<UnityEngine.UI.ScrollRect>();
        var main = root.Find("Encounter_Display").GetComponent<MainStoryUi>();
        return new {
            faceHidden = !root.Find("Face_Display").gameObject.activeInHierarchy,
            mapActive = root.Find("RawImage/RawImage_Road_View_And_Map").gameObject.activeInHierarchy,
            mapTexture = AssetDatabase.GetAssetPath(root.Find("RawImage/RawImage_Road_View_And_Map").GetComponent<UnityEngine.UI.RawImage>().texture),
            chatWidth = ((RectTransform)panel).rect.width,
            mapWidth = ((RectTransform)root.Find("RawImage")).rect.width,
            firstGap = ((RectTransform)panel).anchoredPosition.x - 1076.22f,
            secondGap = ((RectTransform)root.Find("RawImage")).anchoredPosition.x -
                (((RectTransform)panel).anchoredPosition.x + ((RectTransform)panel).rect.width),
            dividerBetweenChatAndMap = ((RectTransform)root.Find("JourneyInterfaceV3_Frames/Divider_70x1080")).anchoredPosition.x >=
                ((RectTransform)panel).anchoredPosition.x + ((RectTransform)panel).rect.width &&
                ((RectTransform)root.Find("JourneyInterfaceV3_Frames/Divider_70x1080")).anchoredPosition.x +
                ((RectTransform)root.Find("JourneyInterfaceV3_Frames/Divider_70x1080")).rect.width <=
                ((RectTransform)root.Find("RawImage")).anchoredPosition.x,
            scrollbarBetweenChatAndMap = ((RectTransform)panel.Find("Scrollbar")).offsetMin.x > 0f &&
                ((RectTransform)panel.Find("Scrollbar")).offsetMax.x < 32f,
            composerPresent = panel.Find("Composer") != null,
            draftText = chat.DraftText,
            typing = chat.IsTyping,
            panelActive = panel.gameObject.activeInHierarchy,
            mainWired = new SerializedObject(main).FindProperty("chatHistory").objectReferenceValue == chat,
            messages = chat.MessageCount,
            contentHeight = scroll.content.rect.height,
            viewportHeight = scroll.viewport.rect.height,
            position = scroll.verticalNormalizedPosition,
            eventSystems = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length,
            raycaster = panel.GetComponentInParent<UnityEngine.UI.GraphicRaycaster>() != null,
            missingScripts = root.GetComponentsInChildren<MonoBehaviour>(true).Count(m => m == null)
        };
    }
}
