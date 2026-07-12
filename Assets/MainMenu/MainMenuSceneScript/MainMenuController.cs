using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("패널 연결")]
    public GameObject mainMenuPanel;
    public GameObject biographyPanel;
    public GameObject settingsPanel;

    [Header("시작 연출 설정")]
    public GameObject burstImage; // 반짝이는 이펙트 이미지
    public BiographyController biographyController;

    private bool isAnimating = false;

    // 1. 시작 버튼 클릭
    public void OnClickStart()
    {
        if (isAnimating) return;
        StartCoroutine(StartSequence());
    }

    private IEnumerator StartSequence()
    {
        isAnimating = true;

        // 반짝이는 이펙트 재생 (사용자가 작성한 연출 활용)
        yield return StartCoroutine(PlayBurstEffect());

        // 씬 전환
        SceneManager.LoadScene("DecisionScene");
    }

    private IEnumerator PlayBurstEffect()
    {
        if (burstImage != null)
        {
            burstImage.SetActive(true);
            Image burstImgComp = burstImage.GetComponent<Image>();
            RectTransform burstRect = burstImage.GetComponent<RectTransform>();

            float duration = 0.5f; // 반짝이는 시간
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // 이미지가 확 커지면서 투명해지는 연출
                burstRect.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 5f, t);
                burstImgComp.color = new Color(1, 1, 1, Mathf.Lerp(1f, 0f, t));

                yield return null;
            }
            burstImage.SetActive(false);
        }
    }

    // 2. 기록(Biography) 버튼 클릭
    public void OnClickBiography()
    {
        mainMenuPanel.SetActive(false);
        biographyPanel.SetActive(true);
        biographyController.OpenBiography();
    }

    // 3. 설정(Settings) 버튼 클릭
    public void OnClickSettings()
    {
        settingsPanel.SetActive(true);
    }

    // 4. 나가기(Quit) 버튼 클릭
    public void OnClickQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}