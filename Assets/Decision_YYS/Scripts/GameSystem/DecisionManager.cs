using System;
using System.Collections.Generic;
using UnityEngine;
using static Constants;

/// <summary>
/// DecisionScene의 입력, 스토리, 이동, 저장 및 전환 흐름을 조율합니다.
/// 세부 계산과 Unity 표현은 각 전용 컨트롤러에 위임합니다.
/// </summary>
public class DecisionManager : MonoBehaviour
{
    private StoryState currentState;

    private DecisionSession session;
    private StoryProgressController storyProgressController;
    private DestinationController destinationController;
    private DecisionSaveService saveService;
    private ChapterFlowController chapterFlowController;
    private DecisionPresentationController presentationController;
    private readonly ChoiceController choiceController = new ChoiceController();
    private readonly ScenarioRepository scenarioRepository = new ScenarioRepository();
    private readonly SceneTransitionService sceneTransitionService = new SceneTransitionService();
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
    [SerializeField] private JoystickLikeGear gearController;
    [SerializeField] private UiController uiController;
    [SerializeField] private StoryRelayManager relayManager;
    [SerializeField] private EnvController envController;
    [SerializeField] private GameObject playerViewUI;
    [SerializeField] private RoadViewCameraController roadViewCameraController;
    [SerializeField] private BettingButtonController bettingButtonController;
    [SerializeField] private CoinDropController coinDropController;
    [Header("Player Movement")]
    private DecisionPlayerController playerController;

    [SerializeField] private StatContainer statContainer;

    private void Awake()
    {
        presentationController = new DecisionPresentationController(uiController, playerViewUI);

        // 프리팹에 포함된 버튼 입력기를 자동으로 연결하되 Inspector 지정도 허용합니다.
        if (bettingButtonController == null)
            bettingButtonController = FindFirstObjectByType<BettingButtonController>();
        if (coinDropController == null)
            coinDropController = FindFirstObjectByType<CoinDropController>();
    }

    private void OnEnable()
    {
        if (statContainer != null)
        {
            statContainer.OnTargetStatReached += HandleTargetStatReached;
        }

        if (bettingButtonController != null)
            bettingButtonController.YellowPressed += HandleScreenClicked;
        if (coinDropController != null)
            coinDropController.BettingCompleted += HandleBettingCompleted;

    }

    private void OnDisable()
    {
        if (statContainer != null)
        {
            statContainer.OnTargetStatReached -= HandleTargetStatReached;
        }

        if (bettingButtonController != null)
            bettingButtonController.YellowPressed -= HandleScreenClicked;
        if (coinDropController != null)
            coinDropController.BettingCompleted -= HandleBettingCompleted;

    }

    private void Start()
    {
        if (GameManager.Instance.CurrentState != GameState.Main)
            return;

        Initialize();
    }

    private void Initialize()
    {
        if (!ValidateDependencies())
        {
            enabled = false;
            return;
        }

        DecisionStartData startData = GameManager.Instance.StartGame();

        if (startData?.Session?.Omnibus?.MainStories == null ||
            startData.SaveManager == null || startData.Player == null)
        {
            Debug.LogError("DecisionScene 초기 데이터 또는 플레이어를 준비하지 못했습니다.");
            enabled = false;
            return;
        }

        session = startData.Session;
        playerController = new DecisionPlayerController(startData.Player);
        saveService = new DecisionSaveService(startData.SaveManager);
        storyProgressController = new StoryProgressController(session);
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

        Vector3 startPosition = startData.SavedPlayerPosition ?? envController.TerrainOrigin + new Vector3(0, 1f, 10);
        playerController.Initialize(startPosition);

        // 실제 시작 위치가 적용된 후 스트리밍과 카메라에 Player를 연결합니다.
        envController.BindPlayer(startData.Player);

        // 새 게임/재회차는 기준 좌표에서 시작하고, 저장 복원은 플레이어 위치로 즉시 스냅합니다.
        bool useInitialCameraPosition =
            chapterIndex == 0 && !startData.SavedPlayerPosition.HasValue;
        roadViewCameraController.Initialize(
            startData.Player.transform,
            useInitialCameraPosition);

        if (startData.PlayerStats?.stats != null)
            statContainer.SetStats(startData.PlayerStats.stats);

        LoadCurrentEpisode();
        presentationController.ShowPlayerViewControl();
    }

    private bool ValidateDependencies()
    {
        bool isValid = true;
        isValid &= ValidateReference(gearController, nameof(gearController));
        isValid &= ValidateReference(uiController, nameof(uiController));
        isValid &= ValidateReference(envController, nameof(envController));
        isValid &= ValidateReference(roadViewCameraController, nameof(roadViewCameraController));
        isValid &= ValidateReference(statContainer, nameof(statContainer));
        isValid &= ValidateReference(bettingButtonController, nameof(bettingButtonController));
        isValid &= ValidateReference(coinDropController, nameof(coinDropController));

        return isValid;
    }

    private bool RegisterTerrainsThroughCurrentProgress()
    {
        if (currentOmnibus?.MainStories == null) return false;

        int lastChapterIndex = Mathf.Min(chapterIndex, currentOmnibus.MainStories.Count - 1);
        for (int currentChapterIndex = 0;
             currentChapterIndex <= lastChapterIndex;
             currentChapterIndex++)
        {
            var chapter = currentOmnibus.MainStories[currentChapterIndex];
            if (chapter?.Title == null) return false;

            int episodeCount = currentChapterIndex < lastChapterIndex
                ? chapter.Title.Count
                : Mathf.Min(episodeIndex + 1, chapter.Title.Count);

            for (int currentEpisodeIndex = 0;
                 currentEpisodeIndex < episodeCount;
                 currentEpisodeIndex++)
            {
                if (!envController.RegisterTerrain(
                    chapter.Chapter,
                    chapter.Title[currentEpisodeIndex]))
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
        if (currentOmnibus?.MainStories == null ||
            chapterIndex < 0 || chapterIndex >= currentOmnibus.MainStories.Count)
        {
            Debug.Log("모든 메인 스토리가 종료되었습니다.");
            currentState = StoryState.Transitioning;
            // 여기서는 완료만 기록합니다. 다음 회차 생성은 추후 엔딩 화면의 시작 버튼에서 호출합니다.
            saveService?.CompleteCurrentRun();
            return;
        }

        var mainStory = currentOmnibus.MainStories[chapterIndex];

        if (mainStory?.Title == null || mainStory.Title.Count == 0)
        {
            Debug.LogError($"챕터 {chapterIndex}에 에피소드가 없습니다.");
            currentState = StoryState.Transitioning;
            return;
        }

        if (episodeIndex < 0)
            episodeIndex = 0;

        if (episodeIndex >= mainStory.Title.Count)
        {
            CompleteChapter();
            return;
        }

        if (string.IsNullOrWhiteSpace(mainStory.Chapter) ||
            string.IsNullOrWhiteSpace(mainStory.Title[episodeIndex]))
        {
            Debug.LogError($"챕터 {chapterIndex}, 에피소드 {episodeIndex}의 경로가 비어 있습니다.");
            currentState = StoryState.Transitioning;
            return;
        }

        // 스탯의 내부 인덱스는 유지하면서 현재 챕터의 네 덕목 이름만 UI에 반영합니다.
        statContainer.SetChapterStatNames(mainStory.Chapter);

        // 새 지형은 기존 지형을 지우지 않고 마지막 전역 청크 뒤에 이어 붙입니다.
        if (!envController.RegisterTerrain(
            mainStory.Chapter,
            mainStory.Title[episodeIndex]))
        {
            currentState = StoryState.Transitioning;
            return;
        }

        // 챕터별 목적지는 해당 지형의 월드 시작점을 가진 Registry로 다시 계산합니다.
        destinationController = new DestinationController(
            envController.PlaceRegistry,
            playerController.TargetPosition.z);

        ScenarioLoadResult loadResult = scenarioRepository.Load(
            mainStory.Chapter,
            mainStory.Title[episodeIndex]);
        ScenarioData loadedScenario = loadResult.Scenario;
        currentScenarioPath = loadResult.SourcePath;

        if (loadResult.UsesAiRevision)
        {
            Debug.Log($"<color=yellow><b>[AI 적용 완료]</b> 수정된 시나리오 데이터를 사용합니다: {currentScenarioPath}</color>");
        }
        
        if (loadedScenario != null)
        {
            storyProgressController.SetScenario(loadedScenario);
            destinationController.BeginScenario(loadedScenario, storyIndex);
            PresentCurrentStory();
        }
        else
        {
            Debug.LogError(
                $"스토리를 불러올 수 없습니다: {currentScenarioPath}. " +
                loadResult.ErrorMessage);
        }
    }

    private void HandleTargetStatReached()
    {
        if (currentState == StoryState.Transitioning || chapterFlowController == null)
            return;

        // Initial은 성향을 소개하는 구간이므로 스탯 임계치 전투를 발생시키지 않습니다.
        if (chapterIndex == 0) return;

        Debug.Log("전투 발생! 현재 진행 상황을 저장하고 전투 씬으로 이동합니다.");

        if (chapterFlowController.PrepareBattleTransition(
            playerController.TargetPosition))
        {
            currentState = StoryState.Transitioning;
            sceneTransitionService.LoadBattleScene();
        }
    }

    private void CompleteChapter()
    {
        if (chapterFlowController.CompleteChapter(
            playerController.TargetPosition))
            LoadCurrentEpisode();
    }

    private void UpdatePlayerPosition()
    {
        if (playerController == null || !playerController.IsAvailable || destinationController == null) return;
        
        float targetZ = destinationController.GetTargetZ(storyIndex);
        playerController.MoveToZ(targetZ);
    }

    private void PresentCurrentStory()
    {
        if (storyProgressController == null || !storyProgressController.HasCurrentDialogue) return;

        Dialogue currentStory = storyProgressController.Current;
        presentationController.ShowDialogue(currentStory);
        RecordPlayedStory(currentStory);

        if (currentStory.IsChoice)
        {
            EnterChoiceState();
        }
        else
        {
            currentState = StoryState.ShowingStory;
            presentationController.ExitChoice();
            bettingButtonController?.SetYellowInteractable(true);
            bettingButtonController?.SetBettingInteractable(false);
        }

        UpdatePlayerPosition();
    }

    private void RecordPlayedStory(Dialogue dialogue)
    {
        if (dialogue == null)
            return;

        if (playedHistory.Count == 0 || playedHistory[playedHistory.Count - 1] != dialogue)
            playedHistory.Add(dialogue);
    }

    private void EnterChoiceState()
    {
        currentState = StoryState.WaitingForChoice;
        // 기어는 이제 코인 종류만 선택하므로 이야기 선택지와 연결하지 않습니다.
        presentationController.EnterChoice();
        bettingButtonController?.SetYellowInteractable(false);
        bettingButtonController?.SetBettingInteractable(true);
    }

    private void HandleScreenClicked()
    {
        if (currentState == StoryState.Transitioning || storyProgressController == null) return;
        if (!storyProgressController.HasCurrentDialogue) return;

        Dialogue currentStory = storyProgressController.Current;

        if (currentState == StoryState.ShowingStory)
        {
            if (currentStory.IsChoice)
            {
                EnterChoiceState();
            }
            else
            {
                AdvanceStory();
            }
        }
    }

    private void HandleBettingCompleted(BettingResult result)
    {
        if (currentState != StoryState.WaitingForChoice ||
            storyProgressController == null ||
            !storyProgressController.HasCurrentDialogue ||
            result == null)
        {
            return;
        }

        Dialogue currentStory = storyProgressController.Current;
        ChoiceResult choice = choiceController.ResolveOption(
            currentStory,
            result.WinningCoinIndex);

        if (!choice.IsValid)
        {
            Debug.LogError($"코인 종류 {result.WinningCoinIndex}에 해당하는 선택지를 찾지 못했습니다.", this);
            bettingButtonController?.SetBettingInteractable(true);
            return;
        }

        // Initial은 선택 방식을 소개하는 구간이므로 실제 스탯에는 반영하지 않습니다.
        if (chapterIndex > 0)
        {
            statContainer.AddStat(choice.OptionIndex, choice.StatChange);
            saveService.SaveStats(statContainer.stats);
        }

        Debug.Log(
            $"[{choice.OptionText}] 선택됨 - 코인 {result.TotalCoins}개, " +
            $"우세 종류 {result.WinningCoinIndex}");

        if (currentState != StoryState.Transitioning)
            AdvanceStory();
    }

    private void AdvanceStory()
    {
        StoryAdvanceResult result = storyProgressController.Advance();
        if (result == StoryAdvanceResult.NextDialogue)
        {
            destinationController.OnStoryAdvanced(storyIndex);

            var nextStory = storyProgressController.Current;

            if (nextStory.isTransition)
            {
                currentState = StoryState.Transitioning;
                RecordPlayedStory(nextStory);
                UpdatePlayerPosition();
                presentationController.PlayStoryTransition(
                    nextStory,
                    HandleStoryTransitionCompleted);
            }
            else
            {
                PresentCurrentStory();
            }

            saveService.SaveProgress(session, playerController.TargetPosition);
        }
        else if (result == StoryAdvanceResult.EpisodeCompleted)
        {
            saveService.SaveProgress(session);
            LoadCurrentEpisode();
        }
        else
        {
            Debug.LogError("현재 스토리를 진행할 수 없습니다.");
            currentState = StoryState.Transitioning;
        }
    }

    private void HandleStoryTransitionCompleted()
    {
        // 카드 전환 중에는 Transitioning을 유지하고, 연출이 끝난 뒤에만 입력 가능한 상태로 복귀합니다.
        if (storyProgressController == null || !storyProgressController.HasCurrentDialogue)
            return;

        if (storyProgressController.Current.IsChoice)
        {
            EnterChoiceState();
        }
            else
            {
                currentState = StoryState.ShowingStory;
                presentationController.ExitChoice();
                bettingButtonController?.SetYellowInteractable(true);
                bettingButtonController?.SetBettingInteractable(false);
            }
    }

}
