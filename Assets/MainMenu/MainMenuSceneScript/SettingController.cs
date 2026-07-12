using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class SettingController : CancelPanelController
{
    public TMP_Dropdown resolutionDropdown;
    public Toggle fullScreenToggle; // 전체화면 토글 연결
    List<Resolution> resolutions = new List<Resolution>();

    void Start()
    {
        InitResolution();
        LoadSettings(); // 저장된 설정 불러오기
    }

    void InitResolution()
    {
        Resolution[] allResolutions = Screen.resolutions;
        resolutionDropdown.ClearOptions();
        List<string> options = new List<string>();
        int currentResolutionIndex = 0;

        for (int i = 0; i < allResolutions.Length; i++)
        {
            string option = allResolutions[i].width + " x " + allResolutions[i].height;
            options.Add(option);
            if (allResolutions[i].width == Screen.width && allResolutions[i].height == Screen.height)
                currentResolutionIndex = i;

            resolutions.Add(allResolutions[i]);
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.RefreshShownValue();
    }

    void LoadSettings()
    {
        // 1. 전체화면 설정 불러오기 (기본값 1 = true)
        bool isFull = PlayerPrefs.GetInt("FullScreen", 1) == 1;
        fullScreenToggle.isOn = isFull;
        Screen.fullScreen = isFull;

        // 2. 해상도 설정 불러오기
        int savedResIndex = PlayerPrefs.GetInt("ResolutionIndex", resolutions.Count - 1);
        resolutionDropdown.value = savedResIndex;

        ApplyResolution(savedResIndex, isFull);
    }

    public void OnResolutionChanged(int index)
    {
        // Screen.fullScreen 대신, 현재 내 눈에 보이는 토글의 체크 상태를 보냅니다.
        if (fullScreenToggle != null)
        {
            ApplyResolution(index, fullScreenToggle.isOn);
        }
        else
        {
            // 토글 연결 안 됐을 때를 대비한 안전장치
            ApplyResolution(index, Screen.fullScreen);
        }

        PlayerPrefs.SetInt("ResolutionIndex", index);
    }

    public void OnFullScreenToggle(bool isFull)
    {
        Screen.fullScreen = isFull;
        // 토글 상태를 숫자로 저장 (1: true, 0: false)
        PlayerPrefs.SetInt("FullScreen", isFull ? 1 : 0);

        // 해상도도 다시 한번 확인 적용
        ApplyResolution(resolutionDropdown.value, isFull);
    }

    void ApplyResolution(int index, bool isFull)
    {
        if (index < 0 || index >= resolutions.Count) return;
        Resolution res = resolutions[index];

        // [중요] 빌드에서는 이 한 줄이 핵심입니다.
        Screen.SetResolution(res.width, res.height, isFull);
        Debug.Log($"적용된 해상도: {res.width}x{res.height}, 전체화면: {isFull}");
    }

    //-------------------------------------------------------

    public Slider bgmSlider;
    void OnEnable()
    {
        if (bgmSlider != null)
            bgmSlider.value = PlayerPrefs.GetFloat("BGM_Volume", 0.5f);
    }
    public void OnBGMVolumeChanged(float value)
    {
        if (MainMenuSoundManager.Instance != null)
            MainMenuSoundManager.Instance.SetBGMVolume(value);
    }
}

