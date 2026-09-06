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

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    private Player playerInstance;
    private bool isPlayerViewActive = false;
    private readonly PlayerFactory playerFactory = new PlayerFactory();

    private void Awake()
    {
        if (instance == null) { instance = this; DontDestroyOnLoad(gameObject); 
            currentState = GameState.Main; saveManager = new SaveManager();
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
        var session = new DecisionSession(
            currentOmnibusData,
            saveManager.CurrentRun,
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
            saveManager.LoadRemainingCoins());
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
}
