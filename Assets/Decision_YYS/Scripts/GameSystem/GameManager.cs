using UnityEngine;
using static Constants;

/// <summary>
/// 씬을 넘어 유지되는 게임 상태와 DecisionScene의 초기 데이터를 제공합니다.
/// </summary>
public class GameManager : MonoBehaviour
{
    private static GameManager instance;

    public static GameManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<GameManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject("GameManager");
                    instance = go.AddComponent<GameManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }

    [Header("Game State")]
    private GameState currentState;
    public GameState CurrentState { get => currentState; set => currentState = value; }

    private SaveManager saveManager;
    private DecisionRunCompletionService finalBattleCompletion;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    private Player playerInstance;
    private bool isPlayerViewActive = true;
    private readonly PlayerFactory playerFactory = new PlayerFactory();

    private void Awake()
    {
        if (instance == null) { instance = this; DontDestroyOnLoad(gameObject); 
            currentState = GameState.Main; saveManager = new SaveManager();
            LocalStoryRuntimeService.PrepareConfiguredModel();
            LocalStoryRuntimeService.TryResumePending();
        }
        else if (instance != this) { Destroy(gameObject); }
    }

    public DecisionStartData StartGame()
    {
        Debug.Log($"[GameManager] {saveManager.CurrentRun}회차 시작");

        OmnibusData currentOmnibusData =
            SaveIOService.Instance.LoadResourceData<OmnibusData>("Omnibus_01");

        var (chapterIndex, episodeIndex, storyIndex, playerStats) = LoadData();
        var player = ActivatePlayer();
        Vector3? savedPlayerPosition = saveManager.LoadPlayerPosition();
        Quaternion? savedPlayerRotation = saveManager.LoadPlayerRotation();
        var session = new DecisionSession(
            currentOmnibusData,
            saveManager.CurrentRun,
            saveManager.RunSeed,
            chapterIndex,
            episodeIndex,
            storyIndex,
            saveManager.LoadPendingEpisodes());

        return new DecisionStartData(
            session,
            player,
            playerStats,
            saveManager,
            savedPlayerPosition,
            savedPlayerRotation,
            saveManager.LoadRemainingCoins(),
            saveManager.LoadInventoryState());
    }

    private (int, int, int, PlayerStats) LoadData()
    {
        var savedStats = saveManager.LoadStats();
        var progress = saveManager.LoadProgress();
        int chapterIndex = 0;
        int episodeIndex = 0;
        int storyIndex = 0;
        if (progress != null)
        {
            chapterIndex = Mathf.Max(0, progress.chapterIndex);
            episodeIndex = Mathf.Max(0, progress.episodeIndex);
            storyIndex = Mathf.Max(0, progress.storyIndex);

            Debug.Log($"[Load] 저장된 지점에서 재시작: Chapter {chapterIndex}, Episode {episodeIndex}, Story {storyIndex}");
        }

        return (chapterIndex, episodeIndex, storyIndex, savedStats);
    }

    private Player ActivatePlayer()
    {
        if (playerInstance == null)
            playerInstance = playerFactory.Create(playerPrefab, isPlayerViewActive);

        return playerInstance;
    }

    /// <summary>
    /// 다음 회차용 빈 저장 데이터를 만들고 활성 회차를 변경합니다.
    /// 마지막 에피소드가 끝난 뒤 메인 메뉴로 돌아가기 전에 호출합니다.
    /// </summary>
    public int PrepareNextPlaythrough()
    {
        if (saveManager == null)
            saveManager = new SaveManager();

        return saveManager.StartNextRun();
    }

    public void EnterFinalBattle(int stage = 1)
    {
        saveManager ??= new SaveManager();
        saveManager.BeginFinalBattle(stage);
        finalBattleCompletion = null;
        LoadFinalBattle();
    }

    private void LoadFinalBattle()
    {
        var progress = saveManager.LoadProgress();
        var stats = saveManager.LoadStats()?.stats;
        int Trait(int index) => stats?.Length == 4 ? stats[index] : 1;
        new SceneTransitionService().LoadFinalBattleScene(new OneWay.Battle3D.Battle3DStartData(
            System.Math.Max(1, progress.finalBattleStage), Trait(0), Trait(1), Trait(2), Trait(3)));
    }

    public bool ResumeFinalBattle()
    {
        saveManager ??= new SaveManager();
        var progress = saveManager.LoadProgress();
        if (!progress.finalBattlePending) return false;
        // A crash between completion and Profile update must not replay the boss or skip a run.
        if (progress.isCompleted) FinishFinalBattle(progress.finalBattleWon);
        else LoadFinalBattle();
        return true;
    }

    public static bool TryFinishFinalBattle(bool won)
    {
        if (instance == null || instance.saveManager?.LoadProgress()?.finalBattlePending != true) return false;
        instance.FinishFinalBattle(won);
        return true;
    }

    private void FinishFinalBattle(bool won)
    {
        if (finalBattleCompletion == null)
        {
            int sourceRun = saveManager.CurrentRun;
            finalBattleCompletion = new DecisionRunCompletionService(
                () => {
                    saveManager.RecordFinalBattleOutcome(won);
                    saveManager.CompleteCurrentRun();
                    try { LocalStoryDialogueQueue.FinalizeRun(SaveIOService.Instance, sourceRun); }
                    catch (System.Exception e) { LocalStoryLog.Write(LocalStoryLogStage.Failed, "전투 종료 기억 조건 확인: " + e.Message); }
                },
                () => { int next = PrepareNextPlaythrough(); LocalStoryRuntimeService.TryResumePending(); return next; },
                () => {
                    new SceneTransitionService().LoadMainMenuScene();
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                });
        }
        finalBattleCompletion.Complete();
    }
}
