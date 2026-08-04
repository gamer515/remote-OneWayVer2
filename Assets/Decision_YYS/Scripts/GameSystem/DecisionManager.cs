using UnityEngine;
using TMPro;
using static Constants;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 게임 진행 관리.
/// </summary>
public class DecisionManager : MonoBehaviour
{
    // [추가] 스탯 변화에 따른 전투 씬 진입을 관리하기 위한 이벤트 구독 및 처리 메서드를 클래스로 구분해야 함.
    // [추가] 저장된 진행도 및 스탯을 관리하는 클래스로 구분해야 함.

    #region storyState
    private StoryState currentState;

    private OmnibusData currentOmnibus;
    private ScenarioData scenarioData;

    // Initials, MartialArts, Wisdoms,...
    private int chapterIndex = 0;
    // initial_1, initial_2 | martialArt_1, martialArt_2 |...
    private int episodeIndex = 0;
    // initial_1(1), initial_1(2), initial_1(3),...
    private int storyIndex = 0;

    // [추가] 현재 로드된 시나리오 파일 경로 기록
    private string currentScenarioPath;

    // 지금까지 진행된 이야기까지 수정하는 건데,
    // 뒷 이야기를 보지 않았기에 앞부분까지 수정이 맞는 지, 아니면 이야기 분위기가 변경이
    // 되기에 전체적으로 변경을 해야 하는 지 확인해 봐야 함.
    private List<Dialogue> playedHistory = new List<Dialogue>();
    #endregion

    // 과한 coupling을 줄여야 하는데, 일단은 편의상 DecisionManager에서 직접 참조하는 중.
    [Header("View & Control Settings")]
    // 추후에 화면 전환이 일어나면 기어로 주변 환경을 보게만 만들고 그 외의 선택되는 기능은 막던가
    // 아니면 객체를 따로 만들고 넣고 해서 관리를 하도록 하자.
    [SerializeField] private JoystickLikeGear gearController;
    [SerializeField] private UiController uiController;
    
    // 이것도 전투씬에 들어가면 어떻게 플레이를 했냐에 따라 데이터 수집이 필요해서 싱글톤이 맞는 듯.
    [SerializeField] private StoryRelayManager relayManager;

    // 이거는 MapController로 이동시켜야 함.
    [SerializeField] private GameObject playerViewUI;
    #region Map & Player Movement

    // MapController.
    private bool isPlayerViewActive = false;

    [Header("Player Movement")]
    private Player playerInstance;
    // PlayerController.
    private float[] chapterStartZs = { 23f, 27f, 35f };
    [SerializeField] private GameObject terrainObj;
    private float[] chapterLengths = { 4f, 8f, 8f };
    #endregion

    // 추후에 전투 씬도 추가한 후에는, 전투 씬과 관련된 데이터 관리 및 저장 기능도 별도의 클래스로 구분하는 것을 권장.
    private SaveManager saveManager;
    // Player 객체가 생성이 되면 그때 같이 생성이 되는 형태로.
    [SerializeField] private StatContainer statContainer;

    // 이렇게 하면 statContainer에서 굳이 해당 클래스를 참조할 필요가 없겠다.
    private void OnEnable()
    {
        if (statContainer != null)
        {
            statContainer.OnTargetStatReached += HandleTargetStatReached;
        }

        if (gearController != null)
        {
            gearController.OnGearChanged += ShowOptionText;
            gearController.OnGearConfirmed += ConfirmChoice;
            gearController.OnScreenCliked += OnScreenClicked;
        }
    }

    private void OnDisable()
    {
        if (statContainer != null)
        {
            statContainer.OnTargetStatReached -= HandleTargetStatReached;
        }

        if (gearController != null)
        {
            gearController.OnGearChanged -= ShowOptionText;
            gearController.OnGearConfirmed -= ConfirmChoice;
            gearController.OnScreenCliked -= OnScreenClicked;
        }
    }

    private void Start()
    {
        // GameState가 특정 시점에 따른 수정을 해야 한다.
        if (GameManager.Instance.CurrentState == GameState.Main)
        {
            PlayerStats playerStats = null;
            (currentOmnibus, chapterIndex, episodeIndex, storyIndex, playerInstance, playerStats, saveManager) = GameManager.Instance.StartGame();

            LoadNextStory();

            if (playerStats != null && playerStats.stats != null)
            {
                statContainer.SetStats(playerStats.stats);
            }

            float currentZ = CalculateTargetZ();
            Vector3 tVector3 = terrainObj.transform.position;
            playerInstance.Initialize(tVector3);
            //playerInstance.Initialize(new Vector3(-55f, 0.35f, currentZ));
            if (playerViewUI != null) playerViewUI.SetActive(true);
            
            currentState = StoryState.ShowingStory;
        }

    }

    private void LoadNextStory()
    {
        if (currentOmnibus == null || currentOmnibus.MainStories == null || chapterIndex >= currentOmnibus.MainStories.Count)
        {
            Debug.Log("모든 메인 스토리가 종료되었습니다.");
            return;
        }

        var mainStory = currentOmnibus.MainStories[chapterIndex];

        if (episodeIndex >= mainStory.Title.Count)
        {
            MoveToNextChapter();
            return;
        }

        string folder = mainStory.Chapter;
        string file = mainStory.Title[episodeIndex];
        string fullPath = $"{folder}/{file}";

        currentScenarioPath = fullPath; // 현재 파일 경로 저장

        // 추후에 다음 이야기 파일을 찾기 위해서는 하드 코딩을 피해야 한다.
        // [테스트 로직] AI가 수정한 NewStory 파일이 있는지 먼저 확인합니다.
        string aiFileName = "NewStory_" + fullPath.Replace("/", "_");
        scenarioData = SaveIOService.Instance.LoadData<ScenarioData>(aiFileName);

        // AI 수정본이 없다면 원본 데이터를 로드합니다.
        if (scenarioData == null || scenarioData.MainStory == null || scenarioData.MainStory.Count == 0)
        {
            scenarioData = SaveIOService.Instance.LoadData<ScenarioData>(fullPath);
        }
        else
        {
            Debug.Log($"<color=yellow><b>[AI 적용 완료]</b> 수정된 시나리오 데이터를 사용합니다: {aiFileName}</color>");
        }
        
        if (scenarioData != null)
        {
            if (storyIndex >= scenarioData.MainStory.Count) storyIndex = 0;
            DisplayCurrentStory();
            UpdatePlayerPosition();
        }
        else
        {
            Debug.LogError($"스토리를 불러올 수 없습니다: {fullPath}");
        }
    }

    private void HandleTargetStatReached()
    {
        // Initial 챕터(인덱스 0)일 때는 무시합니다.
        if (chapterIndex == 0) return;

        Debug.Log("전투 발생! 현재 진행 상황을 저장하고 전투 씬으로 이동합니다.");

        // player에서 statContainer의 기능을 가져와서 쓰는 걸로.
        // [중요] 전투 씬으로 넘어가기 직전에 현재 챕터 결과 기록 및 다음 챕터 준비
        if (currentOmnibus != null && chapterIndex < currentOmnibus.MainStories.Count)
        {
            // 1. 현재 완료된 챕터의 최고 스탯 결과 기록
            int bestStatIndex = 0;
            int maxValue = -1;
            int[] currentStats = statContainer.stats;
            for (int i = 0; i < currentStats.Length; i++)
            {
                if (currentStats[i] > maxValue)
                {
                    maxValue = currentStats[i];
                    bestStatIndex = i;
                }
            }
            saveManager.RecordChapterResult(chapterIndex, bestStatIndex, maxValue);

            // 2. 외부 데이터 전송 (이미 필터링된 핵심 데이터 전송)
            if (relayManager != null)
            {
                relayManager.Relay("MidTransition", currentScenarioPath, playedHistory, statContainer.stats, chapterIndex);
            }

            // 3. 다음 챕터로 인덱스 준비
            chapterIndex++;
            episodeIndex = 0;
            storyIndex = 0;

            // 4. 저장 (씬이 다시 로드될 때 여기서부터 시작하기 위함)
            saveManager.SaveProgress(chapterIndex, episodeIndex, storyIndex);
            
            // 5. 스탯 초기화 및 초기화된 스탯 저장
            statContainer.ResetAllStats();
            saveManager.SaveStats(statContainer.stats);

            // 6. 전투 씬으로 전환
            currentState = StoryState.Transitioning;
            UnityEngine.SceneManagement.SceneManager.LoadScene("BattleScene");
        }
    }

    private void MoveToNextChapter()
    {
        // player에서 statContainer의 기능을 가져와서 쓰는 걸로.
        // 1. 현재 챕터 결과 기록
        int bestStatIndex = 0;
        int maxValue = -1;
        int[] currentStats = statContainer.stats;
        for (int i = 0; i < currentStats.Length; i++)
        {
            if (currentStats[i] > maxValue)
            {
                maxValue = currentStats[i];
                bestStatIndex = i;
            }
        }

        saveManager.RecordChapterResult(chapterIndex, bestStatIndex, maxValue);

        // [추가] 챕터 종료 데이터 전송 (전체 히스토리)
        if (relayManager != null)
        {
            relayManager.Relay("ChapterEnd", currentScenarioPath, playedHistory, statContainer.stats, chapterIndex);
        }

        // GameManager
        // 2. 다음 챕터로 인덱스 변경
        chapterIndex++;
        episodeIndex = 0;
        storyIndex = 0;

        // Player or statContainer
        // [추가] 챕터가 바뀌었으므로 플레이 기록 초기화
        playedHistory.Clear();
        // 3. 스탯 초기화
        statContainer.ResetAllStats();

        // GameManager
        // 4. 저장 및 다음 스토리 로드
        saveManager.SaveProgress(chapterIndex, episodeIndex, storyIndex);
        saveManager.SaveStats(statContainer.stats);

        LoadNextStory();
    }

    // PlayerController.
    private float CalculateTargetZ()
    {
        if (currentOmnibus == null || scenarioData == null || chapterIndex >= chapterStartZs.Length) 
            return 23f;

        // 현재 챕터의 전체 에피소드 수
        int totalEpisodes = currentOmnibus.MainStories[chapterIndex].Title.Count;
        if (totalEpisodes <= 0) totalEpisodes = 1;

        // 현재 에피소드의 전체 스토리(지문) 수
        int totalStories = (scenarioData.MainStory != null && scenarioData.MainStory.Count > 0) ? scenarioData.MainStory.Count : 1;

        // 챕터 내 진행도 계산 (0.0 ~ 1.0)
        float episodeProgress = (float)episodeIndex / totalEpisodes;
        float storyProgressInEpisode = ((float)storyIndex / totalStories) / totalEpisodes;
        float totalChapterProgress = episodeProgress + storyProgressInEpisode;

        // 목표 Z 계산
        return chapterStartZs[chapterIndex] + (totalChapterProgress * chapterLengths[chapterIndex]);
    }

    private float CalculateTargetZForDestination()
    {
        // 해당 목적지까지 찾는 거 확인. => 
        return 0f;
    }

    // PlayerController.
    private void UpdatePlayerPosition()
    {
        if (playerInstance == null) return;
        
        float targetZ = CalculateTargetZ();
        playerInstance.SetTargetZ(targetZ);
    }

    private void DisplayCurrentStory()
    {
        if (scenarioData == null || scenarioData.MainStory == null || scenarioData.MainStory.Count == 0) return;

        var currentStory = scenarioData.MainStory[storyIndex];
        uiController.ChangeUiText(TextTarget.FrontDialogue, currentStory);

        // [추가] 플레이어가 읽은 지문을 기록 리스트에 추가 (중복 방지: 이미 마지막 항목과 같으면 패스)
        if (playedHistory.Count == 0 || playedHistory[playedHistory.Count - 1] != currentStory)
        {
            playedHistory.Add(currentStory);
        }

        // 배경 설정 적용
        uiController.ChangeBackground(currentStory.background);

        if (currentStory.type == "Choice")
        {
            EnterChoiceState();
        }
        else
        {
            currentState = StoryState.ShowingStory;
            uiController.ActiveOptionTextUi(false);
        }

        UpdatePlayerPosition();
    }

    private void EnterChoiceState()
    {
        currentState = StoryState.WaitingForChoice;
        uiController.ActiveOptionTextUi(true);

        // [수정] 캐시된 CurrentGear 대신 직접 현재 물리적 위치를 확인하여 즉시 반영
        int currentGear = (gearController != null) ? gearController.GetCurrentGearDirectly() : 0;

        if (currentGear != 0)
        {
            ShowOptionText(currentGear);
        }
        else
        {
            // [수정] 중앙(0)일 때는 안내 문구로 복구
            uiController.ChangeUiText(TextTarget.Option, text: "선택지를 선택하세요.");
        }
    }

    public void OnScreenClicked()
    {
        if (isPlayerViewActive) return; // 플레이어 시점일 때는 클릭 무시
        if (currentState == StoryState.Transitioning || scenarioData == null || scenarioData.MainStory == null) return;
        if (storyIndex < 0 || storyIndex >= scenarioData.MainStory.Count) return;

        var currentStory = scenarioData.MainStory[storyIndex];

        if (currentState == StoryState.ShowingStory)
        {
            if (currentStory.type == "Choice")
            {
                EnterChoiceState();
            }
            else
            {
                ProceedToNextStory();
            }
        }
    }

    public void ConfirmChoice(int gear)
    {
        // 플레이어 시점일 때는 선택 무시
        if (isPlayerViewActive) return; 

        if (currentState == StoryState.ShowingStory)
        {
            OnScreenClicked();
            return;
        }

        if (currentState != StoryState.WaitingForChoice || scenarioData == null) return;
        if (storyIndex < 0 || storyIndex >= scenarioData.MainStory.Count) return;

        var currentStory = scenarioData.MainStory[storyIndex];
        int optionIndex = GetOptionIndexFromGear(gear);
        
        if (optionIndex >= 0 && optionIndex < currentStory.figure.Length)
        {
            // Initial 챕터(인덱스 0)가 아닐 때만 스탯을 증가시킵니다.
            if (chapterIndex > 0)
            {
                statContainer.AddStat(optionIndex, currentStory.figure[optionIndex]);
                saveManager.SaveStats(statContainer.stats);
            }

            Debug.Log($"[{currentStory.option[optionIndex]}] 선택됨!");
        }

        if (currentState != StoryState.Transitioning)
        {
            ProceedToNextStory();
        }
    }

    private void ProceedToNextStory()
    {
        storyIndex++;  
        if (scenarioData != null && scenarioData.MainStory != null && storyIndex < scenarioData.MainStory.Count)
        {
            var nextStory = scenarioData.MainStory[storyIndex];

            if (nextStory.isTransition)
            {
                currentState = StoryState.Transitioning;
                uiController.ActiveOptionTextUi(false);
                uiController.ChangeUiImage(nextStory);
            }

            DisplayCurrentStory();

            saveManager.SaveProgress(chapterIndex, episodeIndex, storyIndex);
        }
        else
        {
            // 현재 에피소드가 끝났으므로 다음 스토리 로드
            episodeIndex++;
            storyIndex = 0;
            saveManager.SaveProgress(chapterIndex, episodeIndex, storyIndex);
            LoadNextStory();
        }
    }

    public void ShowOptionText(int gear)
    {
        if (scenarioData == null || scenarioData.MainStory == null || storyIndex < 0 || storyIndex >= scenarioData.MainStory.Count) return;
        if (scenarioData.MainStory[storyIndex].type != "Choice") return;

        // [추가] 기어가 중앙(0)이면 안내 문구로 복구
        if (gear == 0)
        {
            uiController.ChangeUiText(TextTarget.Option, text: "선택지를 선택하세요.");
            return;
        }

        int index = GetOptionIndexFromGear(gear);
        if (index >= 0 && index < scenarioData.MainStory[storyIndex].option.Length)
        {
            uiController.ChangeUiText(TextTarget.Option, text: scenarioData.MainStory[storyIndex].option[index]);
        }
    }

    private int GetOptionIndexFromGear(int gear)
    {
        if (gear == (int) Gear.EvilGood) return 0;
        if (gear == (int) Gear.EvilBad) return 1;
        if (gear == (int) Gear.GoodGood) return 2;
        if (gear == (int) Gear.GoodBad) return 3;
        return -1;
    }
}
