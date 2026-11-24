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
    private bool displayedPreview;
    private int displayedBookNumber;
    private MineLearningStation readingStation;
    private GameObject comboGuideCanvas, comboGuidePanel;
    private TextMeshProUGUI comboGuideTitle, comboGuideHint;
    private bool showBookCatalogue;
    private UnityEngine.UI.ScrollRect comboGuideScroll;
    private TextMeshProUGUI comboGuideText;
    private TextMeshProUGUI comboPageNumber;
    private UnityEngine.UI.Button previousComboPage, nextComboPage, comboDiscoveries;
    private MineLearningStation comboGuideStation;
    private (string Title, string Body)[] comboPages;
    private int comboPage;
    private int comboGuideInputFrame = -1, guideStudyCount = -1;
    private bool hideReadingForReward;
    internal bool IsReadingOpen => hideReadingForReward || (readingPanel != null && readingPanel.activeInHierarchy);
    private bool IsComboGuideOpen => comboGuidePanel != null && comboGuidePanel.activeInHierarchy;
    internal bool IsComboGuideInputBlocked => IsComboGuideOpen || comboGuideInputFrame == Time.frameCount;

    internal void ShowReading(MineLearningStation station, bool available)
    {
        var book = station.PendingReading;
        string result = station.ReadingResult;
        if (!available || IsComboGuideOpen || book != null || result != null) station.CloseReadingPreview();
        bool preview = station.ReadingPreview != null;
        int number = preview ? station.ReadingPreviewNumber : 0;
        if (preview) book = station.ReadingPreview;
        hideReadingForReward = available && !IsComboGuideOpen && result != null && HasLearningRewardPresentation;
        if (IsComboGuideOpen) UpdateComboGuide(station);
        if (IsComboGuideOpen || !available || (book == null && result == null) || hideReadingForReward)
        {
            if (readingPanel != null) readingPanel.SetActive(false);
            return;
        }
        readingStation = station;
        if (readingPanel == null) BuildReading();
        readingPanel.SetActive(true);
        if (displayedBook == book && displayedResult == result && displayedPreview == preview &&
            displayedBookNumber == number) return;
        displayedBook = book;
        displayedResult = result;
        displayedPreview = preview;
        displayedBookNumber = number;
        readingTitle.text = result == null ? (string)book["parodyTitle"] : "독서 결과";
        if (number > 0) readingTitle.text += $" · {number}번";
        readingBody.text = result ?? ((string)book["story"] + "\n\n" + (string)book["question"]);
        var choices = book?["choices"] as JArray;
        for (int i = 0; i < readingButtons.Length; i++)
        {
            bool showChoice = !preview && result == null && choices != null && i < choices.Count;
            readingButtons[i].gameObject.SetActive(showChoice || (preview || result != null) && i == 0);
            if (showChoice) readingLabels[i].text = (string)choices[i]["text"];
        }
        if (preview || result != null) readingLabels[0].text = "닫기";
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
            UiInteractionAudio.Bind(button);
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, .91f, .73f);
            button.colors = colors;
            int choice = i;
            button.onClick.AddListener(() =>
            {
                if (displayedPreview) readingStation.CloseReadingPreview();
                else if (readingStation.ReadingResult != null) readingStation.CloseReadingResult();
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
        if (coinGainCanvas != null) Destroy(coinGainCanvas);
        if (learningRewardView != null) Destroy(learningRewardView.transform.parent.gameObject);
    }

    internal void OpenStudyCodex(MineLearningStation station, bool bookCatalogue)
    {
        if (IsReadingOpen || IsComboGuideOpen) return;
        if (comboGuideCanvas == null) BuildComboGuide();
        showBookCatalogue = bookCatalogue;
        comboGuideStation = station;
        comboPages = bookCatalogue ? null : station.ComboReferencePages().ToArray();
        comboPage = 0;
        comboGuideTitle.text = bookCatalogue ? "발견한 책" : "조합표";
        previousComboPage.gameObject.SetActive(!bookCatalogue);
        nextComboPage.gameObject.SetActive(!bookCatalogue);
        comboDiscoveries.gameObject.SetActive(!bookCatalogue);
        comboPageNumber.gameObject.SetActive(!bookCatalogue);
        SetComboGuideOpen(true);
        UpdateComboGuide(station);
    }

    private void UpdateComboGuide(MineLearningStation station)
    {
        int studyCount = showBookCatalogue ? station.Progress.discoveredBookIds.Count : station.Progress.successfulStudies;
        if (guideStudyCount == studyCount) return;
        guideStudyCount = studyCount;

        if (!showBookCatalogue)
        {
            previousComboPage.interactable = comboPage > 0;
            nextComboPage.interactable = comboPage < comboPages.Length;
            comboPageNumber.text = $"{comboPage + 1} / {comboPages.Length + 1}";
            if (comboPage < comboPages.Length)
            {
                var page = comboPages[comboPage];
                comboGuideHint.text = page.Title;
                SetComboGuideText(page.Body + "\n\n빨강 힘  ·  파랑 속도  ·  노랑 지능  ·  청록 매력");
                return;
            }
        }
        comboGuideHint.text = showBookCatalogue ? "지금까지 얻은 책" : "발견한 콤보와 추가 보상";
        string[] colors = { "#2557A8", "#AA3538", "#845800", "#15746C" };
        var text = new StringBuilder();
        if (showBookCatalogue)
        {
            foreach (var book in station.DiscoveredBooks)
            {
                text.Append("<b>").Append((string)book["parodyTitle"]).Append("</b>");
                if ((string)book["author"] is { Length: > 0 } author) text.Append("  /  ").Append(author);
                text.AppendLine();
            }
        }
        else
        {
            foreach (var (reward, condition) in station.DiscoveredStackCombos)
                text.Append("<b>").Append(reward.Name).Append("</b>  /  ").AppendLine(condition)
                    .Append("  <b>콤보 추가 보상</b>  ").AppendLine(FormatGuideRewards(reward.Coins)).AppendLine();
        }
        foreach (var combo in showBookCatalogue ? station.ReadingCombos.Take(0) : station.ReadingCombos)
        {
            if (station.ReadingComboPayouts((string)combo["id"]) <= 0) continue;
            if (MineLearningStation.IsAbilityCombo(combo))
            {
                int length = (int)combo["condition"]["minCompleted"];
                text.Append("<b>").Append((string)combo["name"]).Append("</b>  /  ")
                    .Append(length >= 3 ? $"같은 능력의 오름차순 연속 번호 {length}권" : "기존 발견")
                    .Append("  /  누적 지급 ").Append(station.ReadingComboPayouts((string)combo["id"])).AppendLine("회");
                text.Append("  <b>콤보 추가 보상</b>  ").AppendLine(FormatGuideRewards(station.ExistingComboCoins(combo))).AppendLine();
                continue;
            }
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
            text.Append("  <b>콤보 추가 보상</b>  ").AppendLine(FormatGuideRewards(station.ExistingComboCoins(combo))).AppendLine();
        }
        if (text.Length == 0)
            text.AppendLine(showBookCatalogue ? "아직 발견한 책이 없습니다." : "아직 발견한 콤보가 없습니다.");
        if (!showBookCatalogue && station.ComboGuideHints.Any())
        {
            text.AppendLine().AppendLine("<b>조합 힌트</b>");
            foreach (var hint in station.ComboGuideHints) text.Append("• ").AppendLine(hint);
        }
        SetComboGuideText(text.ToString());
    }

    private void SetComboGuideText(string text)
    {
        comboGuideText.text = text;
        float width = comboGuideScroll.viewport.rect.width;
        if (width <= 0f)
        {
            guideStudyCount = -1;
            return;
        }
        var content = comboGuideScroll.content;
        content.sizeDelta = new Vector2(0f, comboGuideText.GetPreferredValues(comboGuideText.text, width, Mathf.Infinity).y + 18f);
    }

    private void TurnComboPage(int direction)
    {
        ShowComboPage(comboPage + direction);
    }

    private void ShowComboPage(int page)
    {
        comboPage = Mathf.Clamp(page, 0, comboPages.Length);
        comboGuideInputFrame = Time.frameCount;
        guideStudyCount = -1;
        comboGuideScroll.StopMovement();
        comboGuideScroll.content.anchoredPosition = Vector2.zero;
        UpdateComboGuide(comboGuideStation);
    }

    private static string FormatGuideRewards(params int[][] rewards)
    {
        if (rewards.Length == 0) return "없음";
        var amounts = new System.Collections.Generic.List<string>();
        for (int ability = 0; ability < MineLearningStation.AbilityKeys.Length; ability++)
        {
            int minimum = rewards.Min(reward => reward[ability]);
            int maximum = rewards.Max(reward => reward[ability]);
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

        var shade = ReadingRect(root, "ComboGuideModal", Vector2.zero, Vector2.one);
        comboGuidePanel = shade.gameObject;
        shade.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.09f, .06f, .035f, .62f);
        var binding = ReadingRect(shade, "CodexBinding", new Vector2(.19f, .097f), new Vector2(.81f, .881f));
        binding.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.30f, .16f, .09f);
        var leaves = ReadingRect(shade, "CodexPaperEdges", new Vector2(.198f, .107f), new Vector2(.802f, .876f));
        leaves.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.77f, .63f, .40f);
        var body = ReadingPage(shade, "ComboGuidePage", .20f, .80f);
        body.anchorMin = new Vector2(.20f, .12f);
        body.anchorMax = new Vector2(.80f, .88f);
        var title = comboGuideTitle = ReadingText(body, "ComboGuideTitle", 38f, new Vector2(.05f, .88f), new Vector2(.75f, .96f));
        title.text = "콤보 안내";
        var rewardHint = comboGuideHint = ReadingText(body, "ComboGuideRewardHint", 22f, new Vector2(.05f, .825f), new Vector2(.75f, .875f));
        rewardHint.text = "독서 보상은 선택지별 최소~최대치입니다.";
        AddCloseCodexButton(body);
        comboDiscoveries = CreateCodexButton(body, "ComboDiscoveries", "발견 기록", .60f, .78f, .87f, .96f);
        comboDiscoveries.onClick.AddListener(() => ShowComboPage(comboPages.Length));

        var viewport = ReadingRect(body, "ComboGuideViewport", new Vector2(.07f, .13f), new Vector2(.93f, .79f));
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
        previousComboPage = CreateCodexButton(body, "PreviousComboPage", "이전 장", .07f, .23f, .035f, .10f);
        nextComboPage = CreateCodexButton(body, "NextComboPage", "다음 장", .77f, .93f, .035f, .10f);
        previousComboPage.onClick.AddListener(() => TurnComboPage(-1));
        nextComboPage.onClick.AddListener(() => TurnComboPage(1));
        comboPageNumber = ReadingText(body, "ComboPageNumber", 22f, new Vector2(.38f, .035f), new Vector2(.62f, .10f));
        comboPageNumber.alignment = TextAlignmentOptions.Center;
        comboGuidePanel.SetActive(false);
    }

    private void AddCloseCodexButton(RectTransform parent) =>
        CreateCodexButton(parent, "CloseComboGuide", "닫기", .80f, .95f, .87f, .96f)
            .onClick.AddListener(() => SetComboGuideOpen(false));

    private UnityEngine.UI.Button CreateCodexButton(RectTransform parent, string name, string caption,
        float left, float right, float bottom, float top)
    {
        var rect = ReadingRect(parent, name, new Vector2(left, bottom), new Vector2(right, top));
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = new Color(.83f, .70f, .49f);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        UiInteractionAudio.Bind(button);
        button.targetGraphic = image;
        var label = ReadingText(rect, "Label", 23f, Vector2.zero, Vector2.one);
        label.text = caption;
        label.alignment = TextAlignmentOptions.Center;
        return button;
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
