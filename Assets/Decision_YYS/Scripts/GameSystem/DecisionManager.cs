using System;
using System.Collections.Generic;
using UnityEngine;
using JourneyMapKit;
using static Constants;

/// <summary>
/// DecisionScene의 입력, 스토리, 이동, 저장 및 전환 흐름을 조율합니다.
/// 세부 계산과 Unity 표현은 각 전용 컨트롤러에 위임합니다.
/// </summary>
public partial class DecisionManager : MonoBehaviour
{
    private StoryState currentState;

    private DecisionSession session;
    private DecisionSaveService saveService;
    private ChapterFlowController chapterFlowController;
    private DecisionPresentationController presentationController;
    private GameProgress loadedProgress;
    private readonly SceneTransitionService sceneTransitionService = new SceneTransitionService();
    private bool addedYellowInputFallback;
    private bool addedGearInputFallback;
    private OmnibusData currentOmnibus => session?.Omnibus;
    private int chapterIndex
    {
        get => session.ChapterIndex;
        set => session.ChapterIndex = value;
    }
    private int episodeIndex
    {
        get => session.EpisodeIndex;
        set => session.EpisodeIndex = value;
    }
    private int storyIndex
    {
        get => session.StoryIndex;
        set => session.StoryIndex = value;
    }
    private string currentScenarioPath
    {
        get => session.ScenarioPath;
        set => session.ScenarioPath = value;
    }
    private List<Dialogue> playedHistory => session.PlayedHistory;
    [Header("View & Control Settings")]
    [SerializeField] private JourneyBoardInput journeyBoardInput;
    [SerializeField] private UiController uiController;
    [SerializeField] private StoryRelayManager relayManager;
    [SerializeField] private EnvController envController;
    [SerializeField] private RoadViewCameraController roadViewCameraController;
    [SerializeField] private BettingButtonController bettingButtonController;
    [SerializeField] private CoinDropController coinDropController;
    [Tooltip("코인통 왼쪽부터 Blue, Red, Yellow, Teal 순서의 Journey 코인 프리팹입니다.")]
    [SerializeField] private GameObject[] journeyCoinPrefabs;
    [SerializeField] private BackpackInventoryController backpackInventory;
    [SerializeField] private TutorialMiniGameController tutorialMiniGames;
    private JourneyCoinSupplyController journeyCoinSupply;
    [Header("Player Movement")]
    private DecisionPlayerController playerController;

    [SerializeField] private StatContainer statContainer;

    private void Awake()
    {
        presentationController = new DecisionPresentationController(uiController);

        // 프리팹에 포함된 버튼 입력기를 자동으로 연결하되 Inspector 지정도 허용합니다.
        if (bettingButtonController == null)
            bettingButtonController = FindFirstObjectByType<BettingButtonController>();
        if (journeyBoardInput == null)
            journeyBoardInput = FindFirstObjectByType<JourneyBoardInput>();
        if (coinDropController == null)
            coinDropController = FindFirstObjectByType<CoinDropController>();
        if (backpackInventory == null)
            backpackInventory = FindFirstObjectByType<BackpackInventoryController>();
        if (tutorialMiniGames == null)
            tutorialMiniGames = FindFirstObjectByType<TutorialMiniGameController>();
    }

    private void OnEnable()
    {
        if (statContainer != null)
        {
            statContainer.OnTargetStatReached += HandleTargetStatReached;
        }

        if (journeyBoardInput != null)
        {
            EnsureJourneyInputBindings();
        }
        else if (bettingButtonController != null)
        {
            bettingButtonController.YellowPressed += HandleScreenClicked;
        }
    }

    private void OnDisable()
    {
        if (statContainer != null)
        {
            statContainer.OnTargetStatReached -= HandleTargetStatReached;
        }

        if (journeyBoardInput != null)
        {
            if (addedYellowInputFallback)
                journeyBoardInput.onYellowPressed.RemoveListener(OnJourneyYellowPressed);
            if (addedGearInputFallback)
                journeyBoardInput.onGearSelected.RemoveListener(OnJourneyGearSelected);
            addedYellowInputFallback = false;
            addedGearInputFallback = false;
        }
        else if (bettingButtonController != null)
        {
            bettingButtonController.YellowPressed -= HandleScreenClicked;
        }
    }

    private void Start()
    {
        if (GameManager.Instance.CurrentState != GameState.Main)
            return;

        Initialize();
    }

    private void Update()
    {
        if (currentState != StoryState.MovingToEncounter && currentState != StoryState.Transitioning)
            journeyCoinSupply?.HandleInput();

        // 이동 명령과 화면 표현은 분리하고, 실제 도착한 프레임에서 이야기 화면을 엽니다.
        if (encounterFlow != null && currentState == StoryState.MovingToEncounter &&
            playerController != null && playerController.HasReachedTarget())
        {
            CompleteWalkStep();
            return;
        }

    }

    private void Initialize()
    {
        if (!ValidateDependencies())
        {
            enabled = false;
            return;
        }

        DecisionStartData startData = GameManager.Instance.StartGame();

        if (startData?.Session?.Omnibus?.chapters == null ||
            startData.SaveManager == null || startData.Player == null)
        {
            Debug.LogError("DecisionScene 초기 데이터 또는 플레이어를 준비하지 못했습니다.");
            enabled = false;
            return;
        }

        session = startData.Session;
        loadedProgress = startData.SaveManager.LoadProgress();
        envController.SetContentRun(session.RunNumber, session.RunSeed);
        if (!envController.TryValidateAllContent(session.Omnibus, out string contentError))
        {
            Debug.LogError($"전체 콘텐츠 검증 실패: {contentError}", this);
            enabled = false;
            return;
        }
        playerController = new DecisionPlayerController(startData.Player);
        saveService = new DecisionSaveService(startData.SaveManager);
        chapterFlowController = new ChapterFlowController(
            session,
            statContainer,
            relayManager,
            saveService);

        // 중간 챕터 저장에서 시작해도 각 지형의 월드 시작점이 항상 같도록 순서대로 등록합니다.
        if (!RegisterTerrainsThroughCurrentProgress())
        {
            enabled = false;
            return;
        }

        Vector3 startPosition = startData.SavedPlayerPosition ?? envController.TerrainOrigin + new Vector3(0, 0f, 10);
        playerController.Initialize(startPosition, startData.SavedPlayerRotation);

        // 실제 시작 위치가 적용된 후 스트리밍과 카메라에 Player를 연결합니다.
        envController.BindPlayer(startData.Player);

        // 새 게임/재회차는 기준 좌표에서 시작하고, 저장 복원은 플레이어 위치로 즉시 스냅합니다.
        bool useInitialCameraPosition =
            chapterIndex == 0 && !startData.SavedPlayerPosition.HasValue;
        roadViewCameraController.Initialize(
            startData.Player.transform,
            useInitialCameraPosition);

        coinDropController.InitializeInventory(startData.RemainingCoins);
        backpackInventory?.Initialize(
            startData.StoredItemIds,
            saveService.SaveStoredItemIds);
        if (journeyBoardInput != null)
        {
            journeyCoinSupply = new JourneyCoinSupplyController(
                journeyBoardInput, journeyCoinPrefabs, coinDropController,
                saveService.SaveRemainingCoins);
            if (!journeyCoinSupply.IsReady)
                Debug.LogError("Journey 코인통·경사로·코인 프리팹 참조를 확인하세요.", this);
            journeyCoinSupply.RefreshDisplay();
        }

        if (startData.PlayerStats?.stats != null)
            statContainer.SetStats(startData.PlayerStats.stats);
        else
            statContainer.ResetForEpisode();

        LoadCurrentEpisode();
    }

    private bool ValidateDependencies()
    {
        bool isValid = true;
        isValid &= ValidateReference(journeyBoardInput, nameof(journeyBoardInput));
        isValid &= ValidateReference(uiController, nameof(uiController));
        isValid &= ValidateReference(envController, nameof(envController));
        isValid &= ValidateReference(roadViewCameraController, nameof(roadViewCameraController));
        isValid &= ValidateReference(statContainer, nameof(statContainer));
        if (journeyBoardInput == null)
            isValid &= ValidateReference(bettingButtonController, nameof(bettingButtonController));
        isValid &= ValidateReference(coinDropController, nameof(coinDropController));

        return isValid;
    }

    public void OnJourneyYellowPressed()
    {
        HandleScreenClicked();
    }

    public void OnJourneyGearSelected(int selectedIndex)
    {
        HandleEncounterGearSelection(selectedIndex);
    }

    private void EnsureJourneyInputBindings()
    {
        if (!HasPersistentListener(
                journeyBoardInput.onYellowPressed,
                nameof(OnJourneyYellowPressed)))
        {
            journeyBoardInput.onYellowPressed.AddListener(OnJourneyYellowPressed);
            addedYellowInputFallback = true;
            Debug.LogWarning(
                "JourneyBoardInput의 노란 버튼 Inspector 참조가 없어 런타임 방편책을 사용합니다.",
                this);
        }

        if (!HasPersistentListener(
                journeyBoardInput.onGearSelected,
                nameof(OnJourneyGearSelected)))
        {
            journeyBoardInput.onGearSelected.AddListener(OnJourneyGearSelected);
            addedGearInputFallback = true;
            Debug.LogWarning(
                "JourneyBoardInput의 기어 Inspector 참조가 없어 런타임 방편책을 사용합니다.",
                this);
        }
    }

    private bool HasPersistentListener(UnityEngine.Events.UnityEventBase unityEvent, string methodName)
    {
        for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
        {
            if (unityEvent.GetPersistentTarget(i) == this &&
                unityEvent.GetPersistentMethodName(i) == methodName &&
                unityEvent.GetPersistentListenerState(i) !=
                    UnityEngine.Events.UnityEventCallState.Off)
                return true;
        }

        return false;
    }

private void SetYellowInputInteractable(bool interactable)
    {
        if (journeyBoardInput != null)
            journeyBoardInput.SetYellowInteractable(interactable);
        else
            bettingButtonController?.SetYellowInteractable(interactable);
    }

    private void ResetGearSelection()
    {
        journeyBoardInput?.ResetSelection();
    }

    private bool RegisterTerrainsThroughCurrentProgress()
    {
        if (currentOmnibus?.chapters == null) return false;

        int lastChapterIndex = Mathf.Min(chapterIndex, currentOmnibus.chapters.Count - 1);
        for (int currentChapterIndex = 0;
             currentChapterIndex <= lastChapterIndex;
             currentChapterIndex++)
        {
            var chapter = currentOmnibus.chapters[currentChapterIndex];
            if (chapter?.episodeIds == null) return false;

            int episodeCount = currentChapterIndex < lastChapterIndex
                ? chapter.episodeIds.Count
                : Mathf.Min(episodeIndex + 1, chapter.episodeIds.Count);

            for (int currentEpisodeIndex = 0;
                 currentEpisodeIndex < episodeCount;
                 currentEpisodeIndex++)
            {
                if (!envController.RegisterTerrain(
                    chapter.chapterId,
                    chapter.episodeIds[currentEpisodeIndex]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool ValidateReference(UnityEngine.Object reference, string fieldName)
    {
        if (reference != null)
            return true;

        Debug.LogError($"DecisionManager 필수 참조 누락: {fieldName}", this);
        return false;
    }

    private void LoadCurrentEpisode()
    {
        if (currentOmnibus?.chapters == null ||
            chapterIndex < 0 || chapterIndex >= currentOmnibus.chapters.Count)
        {
            currentState = StoryState.Transitioning;

            int completedRun = session?.RunNumber ?? 1;
            saveService?.CompleteCurrentRun();
            int nextRun = GameManager.Instance.PrepareNextPlaythrough();

            Debug.Log(
                $"[DecisionManager] {completedRun}회차의 마지막 에피소드가 종료되었습니다. " +
                $"{nextRun}회차를 Initial부터 시작하도록 준비하고 메인 메뉴로 이동합니다.");
            sceneTransitionService.LoadMainMenuScene();
            return;
        }

        var mainStory = currentOmnibus.chapters[chapterIndex];

        if (mainStory?.episodeIds == null || mainStory.episodeIds.Count == 0)
        {
            Debug.LogError($"챕터 {chapterIndex}에 에피소드가 없습니다.");
            currentState = StoryState.Transitioning;
            return;
        }

        if (episodeIndex < 0)
            episodeIndex = 0;

        if (episodeIndex >= mainStory.episodeIds.Count)
        {
            CompleteChapter();
            return;
        }

        if (string.IsNullOrWhiteSpace(mainStory.chapterId) ||
            string.IsNullOrWhiteSpace(mainStory.episodeIds[episodeIndex]))
        {
            Debug.LogError($"챕터 {chapterIndex}, 에피소드 {episodeIndex}의 경로가 비어 있습니다.");
            currentState = StoryState.Transitioning;
            return;
        }

        // 스탯의 내부 인덱스는 유지하면서 현재 챕터의 네 덕목 이름만 UI에 반영합니다.
        statContainer.SetChapterStatNames(mainStory.chapterId);
        coinDropController.SetChapterMaterials(mainStory.chapterId);

        // 새 지형은 기존 지형을 지우지 않고 마지막 전역 청크 뒤에 이어 붙입니다.
        if (!envController.RegisterTerrain(
            mainStory.chapterId,
            mainStory.episodeIds[episodeIndex]))
        {
            currentState = StoryState.Transitioning;
            return;
        }

        currentScenarioPath = mainStory.chapterId + "/" + mainStory.episodeIds[episodeIndex];
        StartEncounterEpisode();
    }

    private void HandleTargetStatReached()
    {
        if (currentState == StoryState.Transitioning || statContainer == null)
            return;

        // Initial은 성향을 소개하는 구간이므로 임계치 검사 결과를 남기지 않습니다.
        if (chapterIndex == 0) return;

        // Decision 흐름을 먼저 완성하기 위해 전투 전환은 잠시 비활성화하고 로그만 남깁니다.
        Debug.Log(
            $"[DecisionManager] 능력치 임계치 도달. 전투 전환은 현재 비활성화 상태입니다. " +
            $"현재 수치: [{string.Join(", ", statContainer.stats)}]",
            this);
    }

    private void CompleteChapter()
    {
        if (chapterFlowController.CompleteChapter(
            playerController.TargetPosition,
            playerController.CurrentRotation))
            LoadCurrentEpisode();
    }

    private void RecordPlayedStory(Dialogue dialogue)
    {
        if (dialogue == null)
            return;

        if (playedHistory.Count == 0 || playedHistory[playedHistory.Count - 1] != dialogue)
            playedHistory.Add(dialogue);
    }

    private void RecordPlayedEncounterStory(
        Dialogue dialogue,
        string placeId,
        string encounterPath,
        int cardIndex)
    {
        RecordPlayedStory(dialogue);
        if (dialogue == null || string.IsNullOrWhiteSpace(encounterPath))
            return;

        List<PlayedEncounterCardRecord> history = session.PlayedEncounterHistory;
        if (history.Count > 0)
        {
            PlayedEncounterCardRecord last = history[history.Count - 1];
            if (last != null && last.encounterPath == encounterPath && last.cardIndex == cardIndex)
                return;
        }

        history.Add(new PlayedEncounterCardRecord
        {
            placeId = placeId,
            encounterPath = encounterPath,
            cardIndex = cardIndex,
            card = dialogue
        });
    }

    private void HandleScreenClicked()
    {
        HandleEncounterYellowPressed();
    }

    private void RelayCompletedEpisode()
    {
        CompletedEpisodeRecord completedEpisode = new CompletedEpisodeRecord
        {
            scenarioPath = currentScenarioPath,
            storyHistory = new List<Dialogue>(session.PlayedHistory),
            encounterHistory = new List<PlayedEncounterCardRecord>(session.PlayedEncounterHistory),
            bettingDecisions = new List<BettingDecisionRecord>(session.BettingDecisions),
            finalStats = statContainer.stats,
            remainingCoins = coinDropController.RemainingCoins
        };
        session.CompletedEpisodes.Add(completedEpisode);
        saveService.SaveCompletedEpisode(completedEpisode);

        // 최종 성향은 챕터 종료 시 확정하며, 여기서는 에피소드별 작은 기록만 보관합니다.
        session.PlayedHistory.Clear();
        session.PlayedEncounterHistory.Clear();
        session.BettingDecisions.Clear();
    }

    private void FinishCurrentEpisode()
    {
        RelayCompletedEpisode();
        ResetEpisodeResources();
        saveService.SaveCheckpoint(
            session, statContainer.stats,
            playerController.CurrentPosition, playerController.CurrentRotation);
        saveService.SaveRemainingCoins(coinDropController.RemainingCoins);
        LoadCurrentEpisode();
    }

    private void ResetEpisodeResources()
    {
        statContainer.ResetForEpisode();
        coinDropController.ResetInventory();
        journeyCoinSupply?.ResetBoard();
        ResetGearSelection();
        Debug.Log(
            $"[DecisionManager] 새 에피소드 자원 초기화 - " +
            $"능력치 [{string.Join(", ", statContainer.stats)}], " +
            $"코인 [{string.Join(", ", coinDropController.RemainingCoins)}]",
            this);
    }

}
