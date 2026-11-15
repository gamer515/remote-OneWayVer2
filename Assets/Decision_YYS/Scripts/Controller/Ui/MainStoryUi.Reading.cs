using Newtonsoft.Json.Linq;
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
    internal bool IsReadingOpen => readingPanel != null && readingPanel.activeInHierarchy;

    internal void ShowReading(MineLearningStation station, bool available)
    {
        var book = station.PendingReading;
        string result = station.ReadingResult;
        if (!available || (book == null && result == null))
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
        for (int i = 0; i < readingButtons.Length; i++)
        {
            readingButtons[i].gameObject.SetActive(result == null || i == 0);
            if (result == null) readingLabels[i].text = (string)book["choices"][i]["text"];
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
