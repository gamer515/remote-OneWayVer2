using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuSoundManager : MonoBehaviour
{
    // 싱글톤 인스턴스
    public static MainMenuSoundManager Instance;

    [Header("BGM 설정")]
    public AudioSource bgmSource; // 인스펙터에 나타날 스피커 컴포넌트
    public AudioClip mainMenuBGM; // 인스펙터에 넣을 .wav 파일

    void Awake()
    {
        // 1. 싱글톤 및 씬 전환 유지 설정
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return; // 중복 생성된 경우 아래 코드를 실행하지 않음
        }

        // 2. AudioSource 자동 할당 (깜빡하고 인스펙터 연결 안 했을 때 방어)
        if (bgmSource == null)
        {
            bgmSource = GetComponent<AudioSource>();
        }

        // 3. 저장된 볼륨 불러오기
        LoadVolume();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start()
    {
        UpdateBgmForScene(SceneManager.GetActiveScene());
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => UpdateBgmForScene(scene);

    private void UpdateBgmForScene(Scene scene)
    {
        if (bgmSource == null) return;
        if (scene.name == "DecisionScene")
        {
            bgmSource.Stop();
            return;
        }
        if (scene.name != "MainMenuScene" || mainMenuBGM == null || bgmSource.isPlaying) return;
        bgmSource.clip = mainMenuBGM;
        bgmSource.loop = true;
        bgmSource.Play();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }


    // 볼륨 조절 및 PlayerPrefs에 저장
    public void SetBGMVolume(float volume)
    {
        if (bgmSource != null)
        {
            bgmSource.volume = volume;
        }
        PlayerPrefs.SetFloat("BGM_Volume", volume);
    }

    // 게임 시작 시 저장된 볼륨 값 가져오기
    private void LoadVolume()
    {
        float savedVol = PlayerPrefs.GetFloat("BGM_Volume", 0.5f); // 기본값 0.5
        SetBGMVolume(savedVol);
    }
}