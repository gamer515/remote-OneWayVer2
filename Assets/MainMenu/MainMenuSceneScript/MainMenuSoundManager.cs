using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuSoundManager : MonoBehaviour
{
    private const string VolumeKey = "BGM_Volume";
    private const string MineChapterId = "Chapter_1";
    private const float MaxBgmGain = .4f;

    private enum Music { Silent, Menu, Mine }

    public static MainMenuSoundManager Instance { get; private set; }

    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioClip mainMenuBGM;
    [SerializeField] private AudioClip caveTheme1;
    [SerializeField] private AudioClip caveTheme2;

    private readonly System.Random trackRandom = new System.Random();
    private Music music;
    private float volume;
    private float trackGain = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (bgmSource == null)
            bgmSource = GetComponent<AudioSource>();

        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, .5f));
        ApplyVolume();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        SetMusic(scene.name == "MainMenuScene" ? Music.Menu : Music.Silent);
    }

    public void SetDecisionChapter(string chapterId)
    {
        if (SceneManager.GetActiveScene().name != "DecisionScene") return;
        SetMusic(chapterId == MineChapterId ? Music.Mine : Music.Silent);
    }

    private void Update()
    {
        if (music == Music.Mine && bgmSource != null && !AudioListener.pause && !bgmSource.isPlaying)
            PlayCaveTrack();
    }

    private void SetMusic(Music next)
    {
        if (next == music || bgmSource == null) return;

        music = next;
        bgmSource.Stop();
        bgmSource.clip = null;

        if (next == Music.Menu)
            Play(mainMenuBGM, true, .8f);
        else if (next == Music.Mine)
            PlayCaveTrack();
    }

    private void PlayCaveTrack()
    {
        bool first = caveTheme2 == null || (caveTheme1 != null && trackRandom.Next(2) == 0);
        Play(first ? caveTheme1 : caveTheme2, false, first ? 1f : .7f);
    }

    private void Play(AudioClip clip, bool loop, float gain)
    {
        if (clip == null) return;

        trackGain = gain;
        ApplyVolume();
        bgmSource.clip = clip;
        bgmSource.loop = loop;
        bgmSource.Play();
    }

    public void SetBGMVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        ApplyVolume();
        PlayerPrefs.SetFloat(VolumeKey, volume);
    }

    private void ApplyVolume()
    {
        if (bgmSource != null)
            bgmSource.volume = volume * MaxBgmGain * trackGain;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }
}
