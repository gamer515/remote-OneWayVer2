using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using JourneyMapKit;

public static class BuildDecisionMiniGameTestUi
{
    private const string ScenePath = "Assets/Scenes/DecisionScene.unity";
    private const string FontPath = "Assets/Decision_YYS/Fonts/ChosunCentennial_ttf SDF.asset";
    private const string PlayerHandPrefabPath =
        "Assets/UnityAssets/SimpleHands/Prefabs/WhiteHand.prefab";

    public static string All()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedByBuilder = !scene.IsValid() || !scene.isLoaded;
        if (openedByBuilder)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        try
        {
            Transform systems = Find(scene, "_Systems");
            Transform uiRoot = Find(scene, "_UI");
            Transform walkingBack = Find(scene,
                "_UI/Decision_Canvas/Decision_UI_Root/Encounter_Display/Event_View/Walking_View_Back");
            if (systems == null || uiRoot == null || walkingBack == null)
                throw new InvalidOperationException("DecisionScene의 _Systems, _UI 또는 Walking_View_Back을 찾지 못했습니다.");

            DecisionManager manager = FindComponent<DecisionManager>(scene);
            TutorialMiniGameController miniGames = FindComponent<TutorialMiniGameController>(scene);
            JourneyBoardInput boardInput = FindComponent<JourneyBoardInput>(scene);
            if (manager == null || miniGames == null || boardInput == null)
                throw new InvalidOperationException("DecisionManager, TutorialMiniGameController 또는 JourneyBoardInput 참조가 없습니다.");

            GameObject bridgeObject = GetOrCreateChild(systems, "DuelMiniGame");
            DuelMiniGameBridge bridge = GetOrAdd<DuelMiniGameBridge>(bridgeObject);
            DuelAuthoringReferences authoring = GetOrAdd<DuelAuthoringReferences>(bridgeObject);
            DuelMouseHandController mouseHand = GetOrAdd<DuelMouseHandController>(bridgeObject);
            SetReference(bridge, "setupController", miniGames);
            SetReference(bridge, "boardInput", boardInput);
            SetReference(bridge, "authoringReferences", authoring);
            SetReference(bridge, "mouseHandController", mouseHand);
            SetReference(mouseHand, "inputCamera", boardInput.inputCamera);
            SetReference(manager, "duelMiniGame", bridge);

            Transform board = GetReference<Transform>(miniGames, "miniGameBoard");
            if (board == null)
                throw new InvalidOperationException("TutorialMiniGameController의 miniGameBoard 참조가 없습니다.");
            GameObject worldRoot = GetOrCreateChild(board, "DuelAuthoring_World");
            Transform playerHandSpawn = GetOrCreateChild(worldRoot.transform, "PlayerHandSpawnPoint").transform;
            playerHandSpawn.localPosition = new Vector3(-0.65f, 0.18f, -0.35f);
            playerHandSpawn.localRotation = Quaternion.identity;
            Transform npcHandSpawn = GetOrCreateChild(worldRoot.transform, "NpcHandSpawnPoint").transform;
            npcHandSpawn.localPosition = new Vector3(0.65f, 0.18f, 0.35f);
            npcHandSpawn.localRotation = Quaternion.Euler(0f, 180f, 0f);

            Transform overlayCanvas = uiRoot.Find("DuelAuthoring_OverlayCanvas");
            if (overlayCanvas != null)
                Undo.DestroyObjectImmediate(overlayCanvas.gameObject);

            Transform legacyOverlay = Find(scene,
                "_UI/Decision_Canvas/Decision_UI_Root/DuelAuthoring_Overlay");
            if (legacyOverlay != null)
                Undo.DestroyObjectImmediate(legacyOverlay.gameObject);

            SetReference(authoring, "playerHandSpawnPoint", playerHandSpawn);
            SetReference(authoring, "npcHandSpawnPoint", npcHandSpawn);
            SetReference(authoring, "authoringOverlay", null);
            SetReference(authoring, "playerHandMoveArea", null);
            SetReference(authoring, "npcHitArea", null);
            SetReference(authoring, "guardArea", null);
            if (GetReference<GameObject>(authoring, "playerHandPrefab") == null)
            {
                GameObject playerHandPrefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(PlayerHandPrefabPath);
                if (playerHandPrefab == null)
                    throw new InvalidOperationException("테스트용 WhiteHand 프리팹을 찾지 못했습니다.");
                SetReference(authoring, "playerHandPrefab", playerHandPrefab);
            }

            GameObject bar = GetOrCreateUiChild(walkingBack, "MiniGame_TestBar");
            RectTransform barRect = (RectTransform)bar.transform;
            barRect.anchorMin = Vector2.one;
            barRect.anchorMax = Vector2.one;
            barRect.pivot = Vector2.one;
            barRect.anchoredPosition = new Vector2(-20f, -18f);
            barRect.sizeDelta = new Vector2(420f, 70f);
            bar.transform.SetAsLastSibling();

            Image barImage = GetOrAdd<Image>(bar);
            barImage.color = new Color32(63, 47, 35, 210);
            barImage.raycastTarget = false;

            HorizontalLayoutGroup layout = GetOrAdd<HorizontalLayoutGroup>(bar);
            layout.padding = new RectOffset(7, 7, 3, 3);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            Button duel = CreateButton(bar.transform, "DuelTestButton", "대련");
            Button heads = CreateButton(bar.transform, "CoinHeadsTestButton", "동전 앞");
            Button tails = CreateButton(bar.transform, "CoinTailsTestButton", "동전 뒤");

            DecisionMiniGameTestLauncher launcher = GetOrAdd<DecisionMiniGameTestLauncher>(bar);
            SetReference(launcher, "testBar", bar);
            SetReference(launcher, "decisionManager", manager);
            SetReference(launcher, "miniGames", miniGames);
            SetReference(launcher, "duelBridge", bridge);
            SetReference(launcher, "duelButton", duel);
            SetReference(launcher, "coinHeadsButton", heads);
            SetReference(launcher, "coinTailsButton", tails);

            EditorUtility.SetDirty(bridge);
            EditorUtility.SetDirty(authoring);
            EditorUtility.SetDirty(mouseHand);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(launcher);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("DecisionScene 저장에 실패했습니다.");

            return "DecisionScene: MiniGame_TestBar 3 buttons + DuelMiniGameBridge references saved.";
        }
        finally
        {
            if (openedByBuilder && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static string Verify()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            return "FAIL: DecisionScene is not loaded.";

        Transform bar = Find(scene,
            "_UI/Decision_Canvas/Decision_UI_Root/Encounter_Display/Event_View/Walking_View_Back/MiniGame_TestBar");
        Transform bridgeTransform = Find(scene, "_Systems/DuelMiniGame");
        DecisionManager manager = FindComponent<DecisionManager>(scene);
        DuelMiniGameBridge bridge = bridgeTransform != null
            ? bridgeTransform.GetComponent<DuelMiniGameBridge>()
            : null;
        DuelAuthoringReferences authoring = bridgeTransform != null
            ? bridgeTransform.GetComponent<DuelAuthoringReferences>()
            : null;
        DecisionMiniGameTestLauncher launcher = bar != null
            ? bar.GetComponent<DecisionMiniGameTestLauncher>()
            : null;

        bool buttons = bar != null &&
            bar.Find("DuelTestButton")?.GetComponent<Button>() != null &&
            bar.Find("CoinHeadsTestButton")?.GetComponent<Button>() != null &&
            bar.Find("CoinTailsTestButton")?.GetComponent<Button>() != null;
        bool managerBridge = HasReference(manager, "duelMiniGame");
        bool bridgeReferences = HasReference(bridge, "setupController") &&
                                HasReference(bridge, "boardInput") &&
                                HasReference(bridge, "authoringReferences") &&
                                HasReference(bridge, "mouseHandController");
        bool authoringReferences = HasReference(authoring, "playerHandSpawnPoint") &&
                                   HasReference(authoring, "npcHandSpawnPoint") &&
                                   HasReference(authoring, "playerHandPrefab");
        bool guideUiRemoved = Find(scene, "_UI/DuelAuthoring_OverlayCanvas") == null;
        bool launcherReferences = HasReference(launcher, "testBar") &&
                                  HasReference(launcher, "decisionManager") &&
                                  HasReference(launcher, "miniGames") &&
                                  HasReference(launcher, "duelBridge") &&
                                  HasReference(launcher, "duelButton") &&
                                  HasReference(launcher, "coinHeadsButton") &&
                                  HasReference(launcher, "coinTailsButton");

        return $"bar={bar != null}, buttons={buttons}, bridge={bridge != null}, " +
               $"managerBridge={managerBridge}, bridgeRefs={bridgeReferences}, " +
               $"authoringRefs={authoringReferences}, guideUiRemoved={guideUiRemoved}, " +
               $"launcherRefs={launcherReferences}";
    }

    public static string ExercisePlayButtons()
    {
        if (!EditorApplication.isPlaying)
            return "FAIL: Editor is not in Play Mode.";

        Scene scene = SceneManager.GetActiveScene();
        Transform bar = Find(scene,
            "_UI/Decision_Canvas/Decision_UI_Root/Encounter_Display/Event_View/Walking_View_Back/MiniGame_TestBar");
        DecisionManager manager = FindComponent<DecisionManager>(scene);
        DuelMiniGameBridge bridge = FindComponent<DuelMiniGameBridge>(scene);
        TutorialMiniGameController miniGames = FindComponent<TutorialMiniGameController>(scene);
        if (bar == null || manager == null || bridge == null || miniGames == null)
            return "FAIL: Runtime test references are missing.";

        bool directMode = bar.gameObject.activeInHierarchy && !manager.enabled;

        bar.Find("DuelTestButton").GetComponent<Button>().onClick.Invoke();
        bool duelStarted = bridge.IsRunning && bridge.CurrentKnightTarget != null;

        bar.Find("CoinHeadsTestButton").GetComponent<Button>().onClick.Invoke();
        bool headsCoinHasBody = FindGoldCoinBody() != null;

        bar.Find("CoinTailsTestButton").GetComponent<Button>().onClick.Invoke();
        bool tailsCoinHasBody = FindGoldCoinBody() != null;

        bridge.CleanupDuel();
        miniGames.Cleanup();
        return $"directMode={directMode}, duelStarted={duelStarted}, " +
               $"headsCoinRigidbody={headsCoinHasBody}, tailsCoinRigidbody={tailsCoinHasBody}";
    }

    public static string StartDuelPreview()
    {
        if (!EditorApplication.isPlaying) return "FAIL: Editor is not in Play Mode.";
        Scene scene = SceneManager.GetActiveScene();
        Transform bar = Find(scene,
            "_UI/Decision_Canvas/Decision_UI_Root/Encounter_Display/Event_View/Walking_View_Back/MiniGame_TestBar");
        DuelMiniGameBridge bridge = FindComponent<DuelMiniGameBridge>(scene);
        if (bar == null || bridge == null) return "FAIL: Duel preview references are missing.";
        bar.Find("DuelTestButton").GetComponent<Button>().onClick.Invoke();
        DuelAuthoringReferences authoring = bridge.AuthoringReferences;
        JourneyBoardInput boardInput = FindComponent<JourneyBoardInput>(scene);
        DuelMouseHandController mouseHand = FindComponent<DuelMouseHandController>(scene);
        return $"duel={bridge.IsRunning}, knight={bridge.CurrentKnightTarget != null}, " +
               $"playerHand={authoring?.SpawnedPlayerHand != null}, " +
               $"handCollider={authoring?.SpawnedPlayerHand?.GetComponentInChildren<Collider>() != null}, " +
               $"mouseControl={mouseHand != null && mouseHand.IsActive}, " +
               $"boardInputEnabled={boardInput != null && boardInput.InputEnabled}, " +
               $"playerAnchor={authoring?.PlayerHandSpawnPoint != null}, npcAnchor={authoring?.NpcHandSpawnPoint != null}";
    }

    private static Rigidbody FindGoldCoinBody()
    {
        foreach (Transform candidate in UnityEngine.Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.name != "TutorialMiniGameObjects") continue;
            Rigidbody body = candidate.GetComponentInChildren<Rigidbody>(true);
            if (body != null) return body;
        }
        return null;
    }

    private static Button CreateButton(Transform parent, string name, string label)
    {
        GameObject buttonObject = GetOrCreateUiChild(parent, name);
        LayoutElement element = GetOrAdd<LayoutElement>(buttonObject);
        element.preferredWidth = 128f;
        element.preferredHeight = 64f;

        Image image = GetOrAdd<Image>(buttonObject);
        image.color = new Color32(214, 190, 144, 255);
        image.raycastTarget = true;

        Button button = GetOrAdd<Button>(buttonObject);
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.94f, 0.78f, 1f);
        colors.pressedColor = new Color(0.76f, 0.64f, 0.46f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        GameObject labelObject = GetOrCreateUiChild(buttonObject.transform, "Label");
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = GetOrAdd<TextMeshProUGUI>(labelObject);
        text.text = label;
        text.fontSize = 24f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color32(48, 35, 25, 255);
        text.raycastTarget = false;
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font != null) text.font = font;
        return button;
    }

    private static GameObject GetOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        GameObject created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, "Create " + name);
        created.transform.SetParent(parent, false);
        return created;
    }

    private static GameObject GetOrCreateUiChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;
        GameObject created = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(created, "Create " + name);
        created.transform.SetParent(parent, false);
        return created;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    private static void SetReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new MissingFieldException(target.GetType().Name, propertyName);
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool HasReference(UnityEngine.Object target, string propertyName)
    {
        if (target == null) return false;
        SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
        return property != null && property.objectReferenceValue != null;
    }

    private static T GetReference<T>(UnityEngine.Object target, string propertyName)
        where T : UnityEngine.Object
    {
        SerializedProperty property = new SerializedObject(target).FindProperty(propertyName);
        return property != null ? property.objectReferenceValue as T : null;
    }

    private static Transform Find(Scene scene, string path)
    {
        string[] segments = path.Split('/');
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != segments[0]) continue;
            Transform current = root.transform;
            for (int i = 1; i < segments.Length && current != null; i++)
                current = current.Find(segments[i]);
            return current;
        }
        return null;
    }

    private static T FindComponent<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T result = root.GetComponentInChildren<T>(true);
            if (result != null) return result;
        }
        return null;
    }
}
