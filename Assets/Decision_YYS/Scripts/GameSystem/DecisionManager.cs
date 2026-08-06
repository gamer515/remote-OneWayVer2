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
    private DecisionInputController inputController;
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
    [Header("Player Movement")]
    private DecisionPlayerController playerController;

    [SerializeField] private StatContainer statContainer;

    private void Awake()
    {
        inputController = new DecisionInputController(gearController, uiController);
        presentationController = new DecisionPresentationController(uiController, playerViewUI);
    }

    private void OnEnable()
    {
        if (statContainer != null)
        {
            statContainer.OnTargetStatReached += HandleTargetStatReached;
        }

        if (inputController != null)
        {
            inputController.GearChanged += HandleGearChanged;
            inputController.GearConfirmed += HandleChoiceConfirmed;
            inputController.ScreenClicked += HandleScreenClicked;
            inputController.Enable();
        }
    }

    private void OnDisable()
    {
        if (statContainer != null)
        {
            statContainer.OnTargetStatReached -= HandleTargetStatReached;
        }

        if (inputController != null)
        {
            inputController.GearChanged -= HandleGearChanged;
            inputController.GearConfirmed -= HandleChoiceConfirmed;
            inputController.ScreenClicked -= HandleScreenClicked;
            inputController.Disable();
        }
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

        Vector3 startPosition = startData.SavedPlayerPosition ?? envController.TerrainOrigin;
        playerController.Initialize(startPosition);
        destinationController = new DestinationController(
            envController.PlaceRegistry,
            startPosition.z);

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
        isValid &= ValidateReference(statContainer, nameof(statContainer));

        if (envController != null &&
            (envController.TerrainData == null || envController.PlaceRegistry == null))
        {
            Debug.LogError("EnvController의 장소 데이터가 초기화되지 않았습니다.", this);
            isValid = false;
        }

        return isValid;
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

        if (chapterFlowController.PrepareBattleTransition())
        {
            currentState = StoryState.Transitioning;
            sceneTransitionService.LoadBattleScene();
        }
    }

    private void CompleteChapter()
    {
        if (chapterFlowController.CompleteChapter())
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
        int currentGear = inputController?.CurrentGear ?? 0;
        string optionText = choiceController.GetOptionText(
            storyProgressController.Current,
            currentGear);
        presentationController.EnterChoice(optionText);
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

    private void HandleChoiceConfirmed(int gear)
    {
        if (currentState == StoryState.ShowingStory)
        {
            HandleScreenClicked();
            return;
        }

        if (currentState != StoryState.WaitingForChoice || storyProgressController == null) return;
        if (!storyProgressController.HasCurrentDialogue) return;

        Dialogue currentStory = storyProgressController.Current;
        ChoiceResult choice = choiceController.Resolve(currentStory, gear);

        if (choice.IsValid)
        {
            // Initial 선택은 튜토리얼 성격이므로 실제 스탯에는 반영하지 않습니다.
            if (chapterIndex > 0)
            {
                statContainer.AddStat(choice.OptionIndex, choice.StatChange);
                saveService.SaveStats(statContainer.stats);
            }

            Debug.Log($"[{choice.OptionText}] 선택됨!");
        }

        if (currentState != StoryState.Transitioning)
        {
            AdvanceStory();
        }
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
        }
    }

    private void HandleGearChanged(int gear)
    {
        if (currentState != StoryState.WaitingForChoice) return;
        if (storyProgressController == null || !storyProgressController.HasCurrentDialogue) return;
        Dialogue currentStory = storyProgressController.Current;
        if (!currentStory.IsChoice) return;

        string optionText = choiceController.GetOptionText(currentStory, gear);
        presentationController.ShowOption(optionText);
    }
}
