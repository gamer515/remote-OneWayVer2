using UnityEngine;

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
    }

    void Start()
    {
        // 시작할 때 BGM 파일이 잘 들어있으면 재생
        if (mainMenuBGM != null && bgmSource != null)
        {
            bgmSource.clip = mainMenuBGM;
            bgmSource.loop = true; // 배경음악이므로 반복 재생
            bgmSource.Play();
        }
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