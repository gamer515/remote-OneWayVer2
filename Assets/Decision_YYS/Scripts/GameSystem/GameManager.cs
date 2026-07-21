using UnityEngine;
using static Constants;
using System.Collections;

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

    [Header("Systems")]
    [SerializeField] private StatContainer statContainer;
    private SaveManager saveManager;

    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    private Player playerInstance;
    private Camera playerCamera;
    private bool isPlayerViewActive = false;

    private void Awake()
    {
        if (instance == null) { instance = this; DontDestroyOnLoad(gameObject); 
            currentState = GameState.Main; saveManager = new SaveManager();
        }
        else if (instance != this) { Destroy(gameObject); }
    }

    //버튼 누르면 OmnibusData를 받아서 GameManager에 저장하고, GameManager에서 DecisionManager로 전달
    public (OmnibusData, int, int ,int, Player, PlayerStats, SaveManager) StartGame()
    {
        OmnibusData currentOmnibusData = SaveIOService.Instance.LoadData<OmnibusData>("Omnibus_01");

        var (chapterIndex, episodeIndex, storyIndex, playerStats) = LoadData();
        var player = ActivatePlayer();

        return (currentOmnibusData, chapterIndex, episodeIndex, storyIndex, player, playerStats, saveManager);
    }

    private (int, int, int, PlayerStats) LoadData()
    {
        // 1. 스탯 복구
        var savedStats = saveManager.LoadStats();

        // 2. 진행도 복구
        var progress = saveManager.LoadProgress();
        int chapterIndex = 0;
        int episodeIndex = 0;
        int storyIndex = 0;
        if (progress != null)
        {
            chapterIndex = progress.chapterIndex;
            episodeIndex = progress.episodeIndex;
            storyIndex = progress.storyIndex;

            Debug.Log($"[Load] 저장된 지점에서 재시작: Chapter {chapterIndex}, Episode {episodeIndex}, Story {storyIndex}");
        }

        return (chapterIndex, episodeIndex, storyIndex, savedStats);
    }

    private Player ActivatePlayer()
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
        }

        return playerInstance;
    }
}
