using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 여러 셔터 날개를 화면 중앙으로 모았다가 펼치는 이야기 전환 연출입니다.
/// </summary>
public sealed class StoryShutterTransition : MonoBehaviour
{
    [Header("Shutter References")]
    [SerializeField] private RectTransform shutterRoot;
    [SerializeField] private RectTransform[] shutterBlades;

    [Header("Animation Settings")]
    [SerializeField, Min(0f)] private float openOffset = 1500f;
    [SerializeField] private float openRotationOffset = -25f;
    [SerializeField, Min(0.01f)] private float closeDuration = 1f;
    [SerializeField, Min(0f)] private float closedHoldDuration = 0.04f;
    [SerializeField, Min(0.01f)] private float openDuration = 2f;

    [Header("Sound")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip shutterSound;

    private bool isPlaying;

    public void CreateRuntimeShutter(RectTransform parent)
    {
        if (shutterRoot != null || parent == null)
            return;

        // 별도 UI 에셋이 없어도 효과를 확인할 수 있도록 기본 6날 셔터를 런타임에 구성합니다.
        GameObject rootObject = new GameObject(
            "StoryShutterOverlay",
            typeof(RectTransform),
            typeof(RectMask2D));
        shutterRoot = rootObject.GetComponent<RectTransform>();
        shutterRoot.SetParent(parent, false);
        shutterRoot.anchorMin = Vector2.zero;
        shutterRoot.anchorMax = Vector2.one;
        shutterRoot.offsetMin = Vector2.zero;
        shutterRoot.offsetMax = Vector2.zero;
        shutterRoot.SetAsLastSibling();

        // Front_Background의 사각형 밖으로 이동한 셔터 날개는 화면에 표시하지 않습니다.
        Vector2 panelSize = parent.rect.size;
        float panelDiagonal = Mathf.Sqrt(
            panelSize.x * panelSize.x + panelSize.y * panelSize.y);
        openOffset = panelDiagonal * 1.2f;

        shutterBlades = new RectTransform[6];
        for (int index = 0; index < shutterBlades.Length; index++)
        {
            GameObject bladeObject = new GameObject(
                $"ShutterBlade_{index}",
                typeof(RectTransform),
                typeof(Image));

            RectTransform blade = bladeObject.GetComponent<RectTransform>();
            blade.SetParent(shutterRoot, false);
            blade.anchorMin = new Vector2(0.5f, 0.5f);
            blade.anchorMax = new Vector2(0.5f, 0.5f);
            blade.pivot = new Vector2(0.5f, 0.5f);
            // 패널 크기를 기준으로 날개를 만들어 다른 Canvas 해상도에서도 같은 비율을 유지합니다.
            blade.sizeDelta = new Vector2(
                panelDiagonal * 1.5f,
                panelDiagonal * 0.75f);

            Image bladeImage = bladeObject.GetComponent<Image>();
            bladeImage.color = new Color(0.025f, 0.025f, 0.025f, 1f);
            bladeImage.raycastTarget = true;
            shutterBlades[index] = blade;
        }

        ApplyProgress(0f);
        shutterRoot.gameObject.SetActive(false);
    }

    private void Awake()
    {
        ApplyProgress(0f);

        if (shutterRoot != null)
            shutterRoot.gameObject.SetActive(false);
    }

    public void Play(Action onClosed, Action onCompleted)
    {
        if (isPlaying)
            return;

        if (!HasValidReferences())
        {
            // 셔터 UI가 아직 연결되지 않았어도 이야기 진행이 멈추지 않게 즉시 완료합니다.
            onClosed?.Invoke();
            onCompleted?.Invoke();
            return;
        }

        StartCoroutine(PlayRoutine(onClosed, onCompleted));
    }

    private IEnumerator PlayRoutine(Action onClosed, Action onCompleted)
    {
        isPlaying = true;
        shutterRoot.gameObject.SetActive(true);

        yield return Animate(0f, 1f, closeDuration);

        // 화면이 완전히 가려진 동안 다음 이야기로 교체해 변경 장면이 보이지 않게 합니다.
        onClosed?.Invoke();

        if (audioSource != null && shutterSound != null)
            audioSource.PlayOneShot(shutterSound);

        if (closedHoldDuration > 0f)
            yield return new WaitForSecondsRealtime(closedHoldDuration);

        yield return Animate(1f, 0f, openDuration);

        shutterRoot.gameObject.SetActive(false);
        isPlaying = false;
        onCompleted?.Invoke();
    }

    private IEnumerator Animate(float startProgress, float endProgress, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float time = Mathf.Clamp01(elapsed / duration);
            float easedTime = time * time * (3f - 2f * time);

            ApplyProgress(Mathf.Lerp(startProgress, endProgress, easedTime));
            yield return null;
        }

        ApplyProgress(endProgress);
    }

    private void ApplyProgress(float progress)
    {
        if (shutterBlades == null || shutterBlades.Length == 0)
            return;

        float angleStep = 360f / shutterBlades.Length;
        for (int index = 0; index < shutterBlades.Length; index++)
        {
            RectTransform blade = shutterBlades[index];
            if (blade == null)
                continue;

            float baseAngle = angleStep * index;
            float radians = baseAngle * Mathf.Deg2Rad;
            Vector2 openDirection = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));

            blade.anchoredPosition = openDirection * Mathf.Lerp(openOffset, 0f, progress);
            blade.localRotation = Quaternion.Euler(
                0f, 0f, baseAngle + Mathf.Lerp(openRotationOffset, 0f, progress));
        }
    }

    private bool HasValidReferences()
    {
        if (shutterRoot == null || shutterBlades == null || shutterBlades.Length == 0)
        {
            Debug.LogWarning("StoryShutterTransition의 셔터 UI 참조가 연결되지 않았습니다.", this);
            return false;
        }

        return true;
    }
}
