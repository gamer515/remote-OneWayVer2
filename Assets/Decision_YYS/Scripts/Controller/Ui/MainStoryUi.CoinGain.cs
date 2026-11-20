using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

internal static class AbilityCoinPresentation
{
    internal static readonly string[] Names = { "힘", "속도", "지능", "매력" };
    internal static readonly Color32[] Colors =
    {
        new Color32(255, 116, 116, 255), new Color32(121, 182, 255, 255),
        new Color32(255, 218, 131, 255), new Color32(136, 221, 212, 255)
    };
}

public partial class MainStoryUi
{
    private readonly Queue<string> coinGainMessages = new Queue<string>();
    private GameObject coinGainCanvas;
    private Coroutine coinGainRoutine;
    private ChatBubbleGraphic coinGainPanel;
    private TextMeshProUGUI coinGainLabel;
    private CanvasGroup coinGainOpacity;

    internal void ShowCoinGains(int[] before, int[] after)
    {
        if (!isActiveAndEnabled || DialogueFont == null || before == null || after == null) return;
        var lines = new List<string>();
        for (int i = 0; i < AbilityCoinPresentation.Names.Length && i < before.Length && i < after.Length; i++)
        {
            long gain = (long)after[i] - before[i];
            if (gain <= 0) continue;
            string color = ColorUtility.ToHtmlStringRGB(AbilityCoinPresentation.Colors[i]);
            lines.Add($"<color=#{color}>{AbilityCoinPresentation.Names[i]} +{gain}</color>");
        }
        if (lines.Count == 0) return;
        coinGainMessages.Enqueue(string.Join("\n", lines));
        if (coinGainRoutine == null)
            coinGainRoutine = StartCoroutine(PlayCoinGains());
    }

    private IEnumerator PlayCoinGains()
    {
        if (coinGainCanvas == null) CreateCoinGainCanvas();
        while (coinGainMessages.Count > 0)
        {
            coinGainLabel.text = coinGainMessages.Dequeue();
            coinGainPanel.FitNotice(coinGainLabel, 300f);
            coinGainOpacity.alpha = 1f;
            coinGainCanvas.SetActive(true);
            yield return new WaitForSecondsRealtime(1.5f);
            const float fadeTime = .25f;
            for (float elapsed = 0f; elapsed < fadeTime; elapsed += Time.unscaledDeltaTime)
            {
                coinGainOpacity.alpha = 1f - elapsed / fadeTime;
                yield return null;
            }
        }
        coinGainCanvas.SetActive(false);
        coinGainRoutine = null;
    }

    private void CreateCoinGainCanvas()
    {
        coinGainCanvas = new GameObject("AbilityCoinGain", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(coinGainCanvas, gameObject.scene);
        var canvas = coinGainCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 145;
        var scaler = coinGainCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        coinGainPanel = ChatBubbleGraphic.CreateNoticeBackdrop(canvas.transform, "CoinGainPanel", false);
        coinGainPanel.color = new Color32(36, 29, 23, 247);
        coinGainPanel.rectTransform.anchorMin = coinGainPanel.rectTransform.anchorMax = new Vector2(.68f, .52f);
        coinGainPanel.rectTransform.anchoredPosition = Vector2.zero;
        coinGainOpacity = coinGainPanel.gameObject.AddComponent<CanvasGroup>();
        coinGainOpacity.blocksRaycasts = false;
        coinGainOpacity.interactable = false;
        coinGainLabel = new GameObject("CoinGainLabel", typeof(RectTransform), typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>();
        coinGainLabel.rectTransform.SetParent(coinGainPanel.rectTransform, false);
        coinGainLabel.rectTransform.anchoredPosition = Vector2.zero;
        coinGainLabel.font = DialogueFont;
        coinGainLabel.fontSize = 34f;
        coinGainLabel.alignment = TextAlignmentOptions.Center;
        coinGainLabel.raycastTarget = false;
    }

    private void OnDisable()
    {
        if (coinGainRoutine != null) StopCoroutine(coinGainRoutine);
        coinGainRoutine = null;
        coinGainMessages.Clear();
        if (coinGainCanvas != null) coinGainCanvas.SetActive(false);
    }
}
