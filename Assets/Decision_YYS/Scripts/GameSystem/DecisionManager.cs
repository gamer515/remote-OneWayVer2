using UnityEngine;
using TMPro;
using static Constants;
using System.Collections;
using UnityEngine.UI;
using System.Collections.Generic;

public class DecisionManager : MonoBehaviour
{
    [SerializeField] private StatContainer statContainer;

    // [추가] 스탯 변화에 따른 전투 씬 진입을 관리하기 위한 이벤트 구독 및 처리 메서드를 클래스로 구분해야 함.
    #region CardAnimation
    [SerializeField] private RectTransform cardFront;
    [SerializeField] private RectTransform cardBack;

    [SerializeField] private TextMeshProUGUI front_Dialogue_Text;
    [SerializeField] private TextMeshProUGUI back_Dialogue_Text;
    [SerializeField] private TextMeshProUGUI option_Text;
    #endregion

    private GameState currentState;

    // [추가] 저장된 진행도 및 스탯을 관리하는 클래스로 구분해야 함.
    #region Data
    ScenarioData scenarioData;

    private OmnibusData currentOmnibus;
    // 인트로, 무협~
    private int chapterIndex = 0;
    // initial_1, initial_2~, martial_1~
    private int episodeIndex = 0;
    // 각 에피소드 내에서 지문 하나하나
    private int storyIndex = 0;

    // [추가] 현재 로드된 시나리오 파일 경로 기록
    private string currentScenarioPath;
    #endregion

    // [추가] 현재 챕터에서 플레이어가 읽은 모든 지문 기록을 클래스로 구분해야 함.
    #region
    private List<Dialogue> playedHistory = new List<Dialogue>();
    #endregion

    // 과한 coupling을 줄여야 하는데, 일단은 편의상 DecisionManager에서 직접 참조하는 중.
    [Header("View & Control Settings")]
    [SerializeField] private JoystickLikeGear gearController;
    [SerializeField] private StoryRelayManager relayManager;
    [SerializeField] private GameObject playerViewUI; 
    
    // 맵 관련 클래스랑 구분.
    private bool isPlayerViewActive = false;
    public bool IsPlayerViewActive => isPlayerViewActive; // 조이스틱에서 참조하는 프로퍼티

    [Header("Player Movement")]
    [SerializeField] private GameObject playerPrefab;
    private Player playerInstance;
    private Camera playerCamera; // 플레이어 객체에 붙은 카메라 저장용
    private float[] chapterStartZs = { 23f, 27f, 35f };
    private float[] chapterLengths = { 4f, 8f, 8f };

    // 추후에 전투 씬도 추가한 후에는, 전투 씬과 관련된 데이터 관리 및 저장 기능도 별도의 클래스로 구분하는 것을 권장.
    private SaveDataManager saveDataManager;

    private void Awake()
    {
        currentState = GameState.ShowingStory;
        saveDataManager = new SaveDataManager();
    }

    // 맵에서 플레이어 이동 관련 클래스 구분.
    private void SpawnPlayer()
    {
        if (playerInstance == null && playerPrefab != null)
        {
            GameObject go = Instantiate(playerPrefab);
            go.SetActive(true); // [추가] 플레이어를 항상 활성화된 상태로 생성합니다.
            playerInstance = go.GetComponent<Player>();
            
            // [추가] 플레이어 자식 객체에서 카메라를 찾아 저장합니다.
            playerCamera = go.GetComponentInChildren<Camera>();
            if (playerCamera != null)
            {
                // 초기에는 카메라 상태를 현재 모드에 맞춥니다.
                playerCamera.enabled = isPlayerViewActive;
            }

            // 현재 진행도에 맞는 위치 계산하여 그 자리에서 생성
            float currentZ = CalculateTargetZ();
            playerInstance.Initialize(new Vector3(-55f, 0.35f, currentZ));
        }
    }

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

    private void UpdatePlayerPosition()
    {
        if (playerInstance == null) return;
        
        float targetZ = CalculateTargetZ();
        playerInstance.SetTargetZ(targetZ);
    }

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

    private void HandleTargetStatReached()
    {
        // Initial 챕터(인덱스 0)일 때는 무시합니다.
        if (chapterIndex == 0) return;

        Debug.Log("전투 발생! 현재 진행 상황을 저장하고 전투 씬으로 이동합니다.");

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
            saveDataManager.RecordChapterResult(chapterIndex, bestStatIndex, maxValue);

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
            saveDataManager.SaveProgress(chapterIndex, episodeIndex, storyIndex);
            
            // 5. 스탯 초기화 및 초기화된 스탯 저장
            statContainer.ResetAllStats();
            saveDataManager.SaveStats(statContainer.stats);

            // 6. 전투 씬으로 전환
            currentState = GameState.Transitioning;
            UnityEngine.SceneManagement.SceneManager.LoadScene("BattleScene");
        }
    }

    // 기존 Start() 대신 코루틴 사용
    private IEnumerator Start()
    {
        currentOmnibus = SaveManager.Instance.LoadData<OmnibusData>("Omnibus_01");

        // 문제 1 해결: AI가 아직 스토리를 만들고 있다면 대기
        if (AIAPIClient.Instance != null && AIAPIClient.Instance.isAiProcessing)
        {
            Debug.Log("[DecisionManager] AI 스토리를 기다리는 중...");
            // TODO: 여기에 "스토리 생성 중..." 같은 로딩 UI나 패널을 켜는 코드를 추가하면 더 좋아.

            yield return new WaitUntil(() => !AIAPIClient.Instance.isAiProcessing);

            // TODO: 로딩 UI 비활성화
        }

        LoadGame();
        SpawnPlayer();
        if (playerViewUI != null) playerViewUI.SetActive(true);
    }

    private void LoadGame()
    {
        // 1. 스탯 복구
        var savedStats = saveDataManager.LoadStats();
        if (savedStats != null && savedStats.stats != null)
        {
            statContainer.SetStats(savedStats.stats);
        }

        // 2. 진행도 복구
        var progress = saveDataManager.LoadProgress();
        if (progress != null)
        {
            chapterIndex = progress.chapterIndex;
            episodeIndex = progress.episodeIndex;
            storyIndex = progress.storyIndex;

            Debug.Log($"[Load] 저장된 지점에서 재시작: Chapter {chapterIndex}, Episode {episodeIndex}, Story {storyIndex}");
        }

        LoadNextStory();
    }

    private void MoveToNextChapter()
    {
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

        saveDataManager.RecordChapterResult(chapterIndex, bestStatIndex, maxValue);

        // [추가] 챕터 종료 데이터 전송 (전체 히스토리)
        if (relayManager != null)
        {
            relayManager.Relay("ChapterEnd", currentScenarioPath, playedHistory, statContainer.stats, chapterIndex);
        }

        // 2. 다음 챕터로 인덱스 변경
        chapterIndex++;
        episodeIndex = 0;
        storyIndex = 0;

        // [추가] 챕터가 바뀌었으므로 플레이 기록 초기화
        playedHistory.Clear();

        // 3. 스탯 초기화
        statContainer.ResetAllStats();

        // 4. 저장 및 다음 스토리 로드
        saveDataManager.SaveProgress(chapterIndex, episodeIndex, storyIndex);
        saveDataManager.SaveStats(statContainer.stats);

        LoadNextStory();
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

        // [테스트 로직] AI가 수정한 NewStory 파일이 있는지 먼저 확인합니다.
        string aiFileName = "NewStory_" + fullPath.Replace("/", "_");
        scenarioData = SaveManager.Instance.LoadData<ScenarioData>(aiFileName);

        // AI 수정본이 없다면 원본 데이터를 로드합니다.
        if (scenarioData == null || scenarioData.MainStory == null || scenarioData.MainStory.Count == 0)
        {
            scenarioData = SaveManager.Instance.LoadData<ScenarioData>(fullPath);
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

    private void DisplayCurrentStory()
    {
        if (scenarioData == null || scenarioData.MainStory == null || scenarioData.MainStory.Count == 0) return;

        var currentStory = scenarioData.MainStory[storyIndex];
        front_Dialogue_Text.text = SanitizeText(currentStory.text);

        // [추가] 플레이어가 읽은 지문을 기록 리스트에 추가 (중복 방지: 이미 마지막 항목과 같으면 패스)
        if (playedHistory.Count == 0 || playedHistory[playedHistory.Count - 1] != currentStory)
        {
            playedHistory.Add(currentStory);
        }

        // 배경 설정 적용
        ApplyBackground(cardFront, currentStory.background);

        if (currentStory.type == "Choice")
        {
            EnterChoiceState();
        }
        else
        {
            currentState = GameState.ShowingStory;
            option_Text.gameObject.SetActive(false);
        }

        UpdatePlayerPosition();
    }

    private void EnterChoiceState()
    {
        currentState =  GameState.WaitingForChoice;
        option_Text.gameObject.SetActive(true);

        // [수정] 캐시된 CurrentGear 대신 직접 현재 물리적 위치를 확인하여 즉시 반영
        int currentGear = (gearController != null) ? gearController.GetCurrentGearDirectly() : 0;

        if (currentGear != 0)
        {
            ShowOptionText(currentGear);
        }
        else
        {
            // [수정] 중앙(0)일 때는 안내 문구로 복구
            option_Text.text = "선택지를 선택하세요.";
        }
    }

    private void ApplyBackground(RectTransform card, string bgData)
    {
        if (string.IsNullOrEmpty(bgData) || bgData.ToLower() == "none") return;

        Image dgImg = card.GetComponent<Image>();
        if (dgImg == null) return;

        Color customColor;
        if (ColorUtility.TryParseHtmlString(bgData, out customColor))
        {
            dgImg.sprite = null;
            dgImg.color = customColor;
        }
        else
        {
            Sprite loadedSprite = Resources.Load<Sprite>(bgData);
            if (loadedSprite != null)
            {
                dgImg.sprite = loadedSprite;
                dgImg.color = Color.white;
            }
        }
    }

    public void TogglePlayerView()
    {
        isPlayerViewActive = !isPlayerViewActive;
        
        // 1. UI 활성화/비활성화
        if (playerViewUI != null)
        {
            playerViewUI.SetActive(isPlayerViewActive);
        }

        // 2. 플레이어 객체에 붙은 카메라 켜기/끄기
        if (playerCamera != null)
        {
            playerCamera.enabled = isPlayerViewActive;
        }
        
        Debug.Log(isPlayerViewActive ? "플레이어 시점 ON" : "플레이어 시점 OFF");
    }

    public void OnScreenClicked()
    {
        if (isPlayerViewActive) return; // 플레이어 시점일 때는 클릭 무시
        if (currentState ==  GameState.Transitioning || scenarioData == null || scenarioData.MainStory == null) return;
        if (storyIndex < 0 || storyIndex >= scenarioData.MainStory.Count) return;

        var currentStory = scenarioData.MainStory[storyIndex];

        if (currentState ==  GameState.ShowingStory)
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

        if (currentState ==  GameState.ShowingStory)
        {
            OnScreenClicked();
            return;
        }

        if (currentState !=  GameState.WaitingForChoice || scenarioData == null) return;
        if (storyIndex < 0 || storyIndex >= scenarioData.MainStory.Count) return;

        var currentStory = scenarioData.MainStory[storyIndex];
        int optionIndex = GetOptionIndexFromGear(gear);
        
        if (optionIndex >= 0 && optionIndex < currentStory.figure.Length)
        {
            // Initial 챕터(인덱스 0)가 아닐 때만 스탯을 증가시킵니다.
            if (chapterIndex > 0)
            {
                statContainer.AddStat(optionIndex, currentStory.figure[optionIndex]);
                saveDataManager.SaveStats(statContainer.stats);
            }

            Debug.Log($"[{currentStory.option[optionIndex]}] 선택됨!");
        }

        if (currentState !=  GameState.Transitioning)
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
                option_Text.gameObject.SetActive(false);
                currentState =  GameState.Transitioning;
                StartCoroutine(SwipeTransition(nextStory));
            }
            else
            {
                DisplayCurrentStory();
            }

            saveDataManager.SaveProgress(chapterIndex, episodeIndex, storyIndex);
        }
        else
        {
            // 현재 에피소드가 끝났으므로 다음 스토리 로드
            episodeIndex++;
            storyIndex = 0;
            saveDataManager.SaveProgress(chapterIndex, episodeIndex, storyIndex);
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
            option_Text.text = "선택지를 선택하세요.";
            return;
        }

        int index = GetOptionIndexFromGear(gear);
        if (index >= 0 && index < scenarioData.MainStory[storyIndex].option.Length)
        {
            option_Text.text = scenarioData.MainStory[storyIndex].option[index];
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

    private IEnumerator SwipeTransition(Dialogue nextStory)
    {
        back_Dialogue_Text.text = SanitizeText(nextStory.text);
        string bgData = nextStory.background;

        if (!string.IsNullOrEmpty(bgData) && bgData.ToLower() != "none")
        {
            Image dgImg = cardBack.GetComponent<Image>();
            Color customColor;

            if (ColorUtility.TryParseHtmlString(bgData, out customColor))
            {
                Debug.Log($"Color detected: {bgData}, applying color: {customColor}");
                dgImg.sprite = null;
                dgImg.color = customColor;
            }
            else
            {
                Debug.Log($"Not a color, trying to load resource: {bgData}");
                Sprite loadedSprite = Resources.Load<Sprite>(bgData);
                if (loadedSprite != null)
                {
                    dgImg.sprite = loadedSprite;
                    dgImg.color = Color.white;
                }
            }
        }

        float duration = 0.5f;
        float elasped = 0f;
        Vector2 startPos = cardFront.anchoredPosition;
        Quaternion startRot = cardFront.localRotation;

        Vector2 targetPos = startPos + new Vector2(-1000f, -200f);
        Quaternion targetRot = Quaternion.Euler(0f, 0f, 30f);

        while (elasped < duration)
        {
            elasped += Time.deltaTime;
            float t = Mathf.Clamp01(elasped / duration);

            cardFront.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            cardFront.localRotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }

        front_Dialogue_Text.text = SanitizeText(nextStory.text);

        Image frontImg = cardFront.GetComponent<Image>();
        Image backImg = cardBack.GetComponent<Image>();

        if(frontImg != null && backImg != null)
        {
            frontImg.sprite = backImg.sprite;
            frontImg.color = backImg.color;
        }

        cardFront.anchoredPosition = startPos;
        cardFront.localRotation = startRot;

        DisplayCurrentStory();
    }

    private string SanitizeText(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input.Replace("{", "").Replace("}", "");
    }
}
