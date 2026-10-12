using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class BattlePatternDebugPanel
{
    public static bool IsSelectable(BattleStepDefinition step)
    {
        return step != null && (step.kind == BattleStepKind.Pattern || step.kind == BattleStepKind.Slash);
    }

    public static void Create(BattleSceneController owner, BattleDebugController debug)
    {
        var root = new GameObject("패턴 선택", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(owner.Context.RuntimeRoot, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0f;

        // 전체 실행 버튼 하나와 패턴별 버튼 두 개를 표시한다.
        int count = 1;

        foreach (var step in owner.ActiveStage.flow.steps)
        {
            if (IsSelectable(step))
                count += 2;
        }
        int columns = Mathf.Min(6, count);
        int rows = Mathf.CeilToInt((float)count / columns);
        var panel = new GameObject("Buttons", typeof(RectTransform), typeof(Image), typeof(GridLayoutGroup));
        panel.transform.SetParent(root.transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(12f, -12f);
        rect.sizeDelta = new Vector2(columns * 106f + 10f, rows * 38f + 10f);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
        var layout = panel.GetComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(100f, 32f);
        layout.spacing = new Vector2(6f, 6f);
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = columns;

        MakeButton(
    panel.transform,
    owner,
    debug,
    owner.StartData.DisplayName + " 전체",
    -1);
        var definitions = owner.ActiveStage.flow.steps;
        for (int i = 0; i < definitions.Count; i++)
        {
            var step = definitions[i];
            if (!IsSelectable(step)) continue;
            string label;

            if (step.kind == BattleStepKind.Slash)
            {
                label = "베기";
            }
            else if (step.pattern.kind == BattlePatternKind.B2)
            {
                label = "패턴 " + step.pattern.b2PatternId;
            }
            else if (step.pattern.kind == BattlePatternKind.BD)
            {
                label = "패턴 " + step.pattern.bdPatternId;
            }
            else
            {
                label = string.IsNullOrEmpty(step.label)
                    ? "패턴 " + (i + 1)
                    : step.label;
            }
            MakeButton( panel.transform, owner, debug, label + " 단독", i, false);

            MakeButton(panel.transform, owner, debug, label + "부터", i, true);
        }
    }

    private static void MakeButton(
    Transform parent,
    BattleSceneController owner,
    BattleDebugController debug,
    string label,
    int stepIndex,
    bool continueFromHere = false)
    {
        var go = new GameObject(
    label,
    typeof(RectTransform),
    typeof(Image),
    typeof(Button));

        go.transform.SetParent(parent, false);

        var background = go.GetComponent<Image>();

        // 현재 선택한 패턴과 실행 방식이 일치하는지 확인한다.
        bool selected =
            owner.DebugPatternStepIndex == stepIndex &&
            (stepIndex < 0 ||
             owner.DebugContinueFromHere == continueFromHere);

        background.color = selected
            ? new Color(0.18f, 0.4f, 0.7f, 1f)
            : new Color(0.2f, 0.2f, 0.2f, 1f);

        // 버튼 변수를 먼저 선언한 다음 클릭 동작을 등록한다.
        var button = go.GetComponent<Button>();
        button.targetGraphic = background;
        button.navigation = new Navigation
        {
            mode = Navigation.Mode.None
        };

        button.onClick.AddListener(() =>
        {
            debug.SelectPattern(
                owner.ActiveStage.stageIndex,
                stepIndex,
                continueFromHere);
        });

        var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(go.transform, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = owner.Context.Message.font;
        text.text = label;
        text.fontSize = 18f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 10f;
        text.fontSizeMax = 18f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(4f, 2f);
        rect.offsetMax = new Vector2(-4f, -2f);
    }
}