using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

public partial class MainStoryUi
{
    private GameObject readingPanel;
    private TextMeshProUGUI readingTitle, readingBody;
    private TextMeshProUGUI[] readingLabels;
    private UnityEngine.UI.Button[] readingButtons;
    private JObject displayedBook;
    private string displayedResult;
    private MineLearningStation readingStation;
    private GameObject comboGuideCanvas, comboGuidePanel, comboGuideToggle;
    private UnityEngine.UI.ScrollRect comboGuideScroll;
    private TextMeshProUGUI comboGuideText;
    private int comboGuideInputFrame = -1, guideStudyCount = -1;
    internal bool IsReadingOpen => readingPanel != null && readingPanel.activeInHierarchy;
    private bool IsComboGuideOpen => comboGuidePanel != null && comboGuidePanel.activeInHierarchy;
    internal bool IsComboGuideInputBlocked => IsComboGuideOpen || comboGuideInputFrame == Time.frameCount;

    internal void ShowReading(MineLearningStation station, bool available)
    {
        var book = station.PendingReading;
        string result = station.ReadingResult;
        UpdateComboGuide(station, available && book == null && result == null);
        if (IsComboGuideOpen || !available || (book == null && result == null))
        {
            if (readingPanel != null) readingPanel.SetActive(false);
            return;
        }
        readingStation = station;
        if (readingPanel == null) BuildReading();
        readingPanel.SetActive(true);
        if (displayedBook == book && displayedResult == result) return;
        displayedBook = book;
        displayedResult = result;
        readingTitle.text = result == null ? (string)book["parodyTitle"] : "독서 결과";
        readingBody.text = result ?? ((string)book["story"] + "\n\n" + (string)book["question"]);
        var choices = book?["choices"] as JArray;
        for (int i = 0; i < readingButtons.Length; i++)
        {
            bool showChoice = result == null && choices != null && i < choices.Count;
            readingButtons[i].gameObject.SetActive(showChoice || result != null && i == 0);
            if (showChoice) readingLabels[i].text = (string)choices[i]["text"];
        }
        if (result != null) readingLabels[0].text = "닫기";
    }

    private void BuildReading()
    {
        var root = ReadingRect(transform, "OpenReadingBook", Vector2.zero, Vector2.one);
        root.SetParent(null, false);
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        readingPanel = root.gameObject;
        var overlay = root.gameObject.AddComponent<UnityEngine.UI.Image>();
        overlay.color = new Color(.09f, .06f, .035f, .53f);
        var book = ReadingRect(root, "BookCover", new Vector2(.16f, .21f), new Vector2(.84f, .81f));
        book.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.32f, .17f, .09f);
        var left = ReadingPage(book, "LeftPage", .015f, .494f);
        var right = ReadingPage(book, "RightPage", .506f, .985f);
        readingTitle = ReadingText(left, "BookTitle", 37, new Vector2(.075f, .79f), new Vector2(.925f, .94f));
        readingBody = ReadingText(left, "BookText", 30, new Vector2(.075f, .07f), new Vector2(.925f, .765f));
        readingButtons = new UnityEngine.UI.Button[4];
        readingLabels = new TextMeshProUGUI[4];
        for (int i = 0; i < readingButtons.Length; i++)
        {
            float top = .88f - i * .20f;
            var row = ReadingRect(right, "ReadingChoice" + (i + 1), new Vector2(.075f, top - .155f), new Vector2(.925f, top));
            var image = row.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(.83f, .70f, .49f);
            var button = row.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, .91f, .73f);
            button.colors = colors;
            int choice = i;
            button.onClick.AddListener(() =>
            {
                if (readingStation.ReadingResult != null) readingStation.CloseReadingResult();
                else readingStation.SelectReadingChoice(choice);
                ShowReading(readingStation, true);
            });
            readingButtons[i] = button;
            readingLabels[i] = ReadingText(row, "Label", 27, new Vector2(.06f, .08f), new Vector2(.94f, .92f));
            readingLabels[i].alignment = TextAlignmentOptions.Center;
        }
        book.localScale = new Vector3(.96f, 1f, 1f);
        root.SetAsLastSibling();
        root.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (readingPanel != null) Destroy(readingPanel);
        if (comboGuideCanvas != null) Destroy(comboGuideCanvas);
    }

    private void UpdateComboGuide(MineLearningStation station, bool canOpen)
    {
        if (comboGuideCanvas == null)
        {
            if (!canOpen || station.ReadingCombos.Count == 0) return;
            BuildComboGuide();
        }
        comboGuideToggle.SetActive(canOpen && !IsComboGuideOpen);
        if (!IsComboGuideOpen) return;
        int studyCount = station.Progress.successfulStudies;
        if (guideStudyCount == studyCount) return;
        guideStudyCount = studyCount;

        string[] colors = { "#2557A8", "#AA3538", "#845800", "#15746C" };
        var text = new StringBuilder();
        foreach (var combo in station.ReadingCombos)
        {
            string type = (string)combo["type"] switch { "author" => "작가", "mainStat" => "주 능력", _ => "주제" };
            var condition = combo["condition"];
            var ids = condition["bookIds"].Values<string>().Distinct().ToArray();
            string comboId = (string)combo["id"];
            var credits = station.FindReadingComboProgress(comboId)?.unusedBooks;
            int done = ids.Count(id => credits?.Contains(id) == true);
            int required = (int)condition["minCompleted"];
            text.Append("<b>").Append(((string)combo["name"]).Replace('·', '/')).Append("</b>  /  ").Append(type)
                .Append("  /  현재 조합 ").Append(done).Append('/').Append(required)
                .Append("  /  누적 지급 ").Append(station.ReadingComboPayouts(comboId)).AppendLine("회");
            foreach (string id in ids)
            {
                int ability = station.ReadingAbility(id);
                text.Append("  <color=").Append(colors[ability]).Append('>')
                    .Append(station.ReadingTitle(id)).Append("</color>  ")
                    .AppendLine(credits?.Contains(id) == true ? "완료" : "미완료");
                text.Append("    독서 보상  ").AppendLine(FormatGuideRewards(station.ReadingChoiceRewards(id).ToArray()));
            }
            text.Append("  <b>콤보 추가 보상</b>  ").AppendLine(FormatGuideRewards(combo["reward"])).AppendLine();
        }
        comboGuideText.text = text.ToString();
        float width = comboGuideScroll.viewport.rect.width;
        if (width <= 0f)
        {
            guideStudyCount = -1;
            return;
        }
        var content = comboGuideScroll.content;
        content.sizeDelta = new Vector2(0f, comboGuideText.GetPreferredValues(comboGuideText.text, width, Mathf.Infinity).y + 18f);
    }

    private static string FormatGuideRewards(params JToken[] rewards)
    {
        if (rewards.Length == 0) return "없음";
        var amounts = new System.Collections.Generic.List<string>();
        for (int ability = 0; ability < MineLearningStation.AbilityKeys.Length; ability++)
        {
            string key = MineLearningStation.AbilityKeys[ability];
            int minimum = rewards.Min(reward => (int?)reward?[key] ?? 0);
            int maximum = rewards.Max(reward => (int?)reward?[key] ?? 0);
            if (maximum <= 0) continue;
            string quantity = minimum == maximum ? $"+{maximum}" : $"{minimum}~{maximum}";
            amounts.Add($"{MineLearningStation.AbilityName(ability)} {quantity}");
        }
        return amounts.Count == 0 ? "없음" : string.Join("  /  ", amounts);
    }

    private void SetComboGuideOpen(bool open)
    {
        comboGuideInputFrame = Time.frameCount;
        comboGuidePanel.SetActive(open);
        comboGuideToggle.SetActive(!open);
        if (open)
        {
            guideStudyCount = -1;
            comboGuideScroll.content.anchoredPosition = Vector2.zero;
        }
    }

    private void BuildComboGuide()
    {
        var root = ReadingRect(null, "ReadingComboGuide", Vector2.zero, Vector2.one);
        comboGuideCanvas = root.gameObject;
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1010;
        var scaler = root.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        comboGuideToggle = ComboGuideButton(root, "콤보 안내", new Vector2(.435f, .065f), new Vector2(.545f, .102f), true);

        var shade = ReadingRect(root, "ComboGuideModal", Vector2.zero, Vector2.one);
        comboGuidePanel = shade.gameObject;
        shade.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.09f, .06f, .035f, .62f);
        var body = ReadingPage(shade, "ComboGuidePage", .20f, .80f);
        body.anchorMin = new Vector2(.20f, .12f);
        body.anchorMax = new Vector2(.80f, .88f);
        var title = ReadingText(body, "ComboGuideTitle", 38f, new Vector2(.05f, .88f), new Vector2(.75f, .96f));
        title.text = "콤보 안내";
        var rewardHint = ReadingText(body, "ComboGuideRewardHint", 22f, new Vector2(.05f, .825f), new Vector2(.75f, .875f));
        rewardHint.text = "독서 보상은 선택지별 최소~최대치입니다.";
        ComboGuideButton(body, "닫기", new Vector2(.80f, .87f), new Vector2(.95f, .96f), false);

        var viewport = ReadingRect(body, "ComboGuideViewport", new Vector2(.05f, .06f), new Vector2(.95f, .81f));
        viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(1f, 1f, 1f, 0f);
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        comboGuideScroll = viewport.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        comboGuideScroll.viewport = viewport;
        comboGuideScroll.horizontal = false;
        comboGuideScroll.scrollSensitivity = 48f;
        var content = ReadingRect(viewport, "ComboGuideContent", Vector2.up, Vector2.one);
        content.pivot = new Vector2(.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        comboGuideScroll.content = content;
        comboGuideText = ReadingText(content, "ComboGuideRows", 24f, Vector2.zero, Vector2.one);
        comboGuideText.enableAutoSizing = false;
        comboGuideText.richText = true;
        comboGuidePanel.SetActive(false);
    }

    private GameObject ComboGuideButton(RectTransform parent, string text, Vector2 min, Vector2 max, bool open)
    {
        var rect = ReadingRect(parent, open ? "OpenComboGuide" : "CloseComboGuide", min, max);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(.83f, .70f, .49f);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => SetComboGuideOpen(open));
        var label = ReadingText(rect, "Label", 23f, Vector2.zero, Vector2.one);
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        return rect.gameObject;
    }

    private static RectTransform ReadingRect(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static RectTransform ReadingPage(Transform parent, string name, float left, float right)
    {
        var page = ReadingRect(parent, name, new Vector2(left, .025f), new Vector2(right, .975f));
        page.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.96f, .89f, .74f);
        return page;
    }

    private TextMeshProUGUI ReadingText(Transform parent, string name, float size, Vector2 min, Vector2 max)
    {
        var label = ReadingRect(parent, name, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = front_Dialogue_Text != null ? front_Dialogue_Text.font : option_Text.font;
        label.color = new Color(.22f, .13f, .08f);
        label.fontSize = label.fontSizeMax = size;
        label.fontSizeMin = size * .58f;
        label.enableAutoSizing = true;
        label.richText = false;
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.TopLeft;
        return label;
    }
}
