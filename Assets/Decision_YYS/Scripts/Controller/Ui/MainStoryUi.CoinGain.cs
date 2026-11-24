using System.Collections;
using System.Collections.Generic;
using System;
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
    private readonly Queue<(string Text, Action StartLearningReward)> coinGainMessages = new();
    private GameObject coinGainCanvas;
    private Coroutine coinGainRoutine;
    private ChatBubbleGraphic coinGainPanel;
    private TextMeshProUGUI coinGainLabel;
    private CanvasGroup coinGainOpacity;
    private LearningCoinRewardView learningRewardView;
    private int waitingLearningRewards;
    private bool HasLearningRewardPresentation => waitingLearningRewards > 0 ||
        (learningRewardView != null && learningRewardView.IsPlaying);

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
        if (lines.Count > 0) QueueCoinGainPopup(string.Join("\n", lines));
    }

    internal void ShowLearningReward(int[] before, int[] after, IReadOnlyList<LearningComboReward> combos,
        Func<Vector2> storyCenter, Func<float> storyWidth, Action<Vector2> recoil,
        Camera boardCamera, Collider board, CoinDropController audio, JourneyCoinSupplyController supply = null)
    {
        if (!isActiveAndEnabled) return;
        var amounts = new int[AbilityCoinPresentation.Names.Length];
        for (int i = 0; i < amounts.Length; i++) amounts[i] = Mathf.Max(0, after[i] - before[i]);
        string popup = FormatLearningCoins(amounts);
        if (string.IsNullOrEmpty(popup)) return;
        if (combos != null)
            foreach (var combo in combos)
            {
                string bonus = FormatLearningCoins(combo.Coins);
                if (!string.IsNullOrEmpty(bonus))
                    popup += $"\n<b>{combo.Name}</b>\n{bonus}";
            }
        if (learningRewardView == null) learningRewardView = LearningCoinRewardView.Create(gameObject.scene);
        void ShowEffects() => learningRewardView.Enqueue(amounts, storyCenter, storyWidth, recoil, boardCamera, board, audio, supply);
        if (DialogueFont == null)
        {
            ShowEffects();
            return;
        }
        waitingLearningRewards++;
        QueueCoinGainPopup(popup, () =>
        {
            waitingLearningRewards--;
            ShowEffects();
        });
    }

    private static string FormatLearningCoins(int[] coins)
    {
        var lines = new List<string>();
        for (int ability = 0; ability < coins.Length; ability++)
        {
            if (coins[ability] <= 0) continue;
            string color = ColorUtility.ToHtmlStringRGB(LearningCoinRewardView.CoinColors[ability]);
            lines.Add($"<color=#{color}>{AbilityCoinPresentation.Names[ability]} +{coins[ability]}</color>");
        }
        return string.Join("  ", lines);
    }

    private void QueueCoinGainPopup(string message, Action startLearningReward = null)
    {
        coinGainMessages.Enqueue((message, startLearningReward));
        if (coinGainRoutine == null)
            coinGainRoutine = StartCoroutine(PlayCoinGains());
    }

    private IEnumerator PlayCoinGains()
    {
        if (coinGainCanvas == null) CreateCoinGainCanvas();
        while (coinGainMessages.Count > 0)
        {
            var popup = coinGainMessages.Dequeue();
            coinGainLabel.text = popup.Text;
            coinGainPanel.FitNotice(coinGainLabel, 300f);
            coinGainOpacity.alpha = 1f;
            coinGainCanvas.SetActive(true);
            popup.StartLearningReward?.Invoke();
            yield return new WaitForSecondsRealtime(1.5f);
            while (popup.StartLearningReward != null && learningRewardView != null && learningRewardView.IsPlaying)
                yield return null;
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
        waitingLearningRewards = 0;
        if (coinGainCanvas != null) coinGainCanvas.SetActive(false);
        if (learningRewardView != null) learningRewardView.Clear();
    }
}
