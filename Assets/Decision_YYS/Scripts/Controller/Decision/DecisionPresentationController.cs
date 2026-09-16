using static Constants;

/// <summary>
/// DecisionScene의 UI 표현 규칙을 한곳에서 관리합니다.
/// </summary>
public sealed class DecisionPresentationController
{
    private readonly UiController uiController;

    public DecisionPresentationController(UiController uiController)
    {
        this.uiController = uiController;
    }

    public void ShowPlayerViewControl()
    {
        // 현재 지문 기반 진행은 유지합니다. 이동 시작 연결은 조우 흐름 단계에서 수행합니다.
        uiController?.ShowEventView();
    }

    // 다음 단계의 이동 시작/이벤트 종료 시 사용할 표현 진입점입니다.
    public void ShowWalkingView() => uiController?.ShowWalkingView();

    public void ShowEncounterPrompt(string prompt, string[] options, int selectedIndex)
    {
        if (uiController == null) return;
        uiController.ShowEventView();
        uiController.ChangeUiText(
            TextTarget.FrontDialogue,
            text: prompt);
        ShowOptions(options, selectedIndex);
    }

    public void ShowOptions(string[] options, int selectedIndex)
    {
        if (uiController == null || options == null || options.Length != 4) return;
        string[] directions = { "좌상", "좌하", "우상", "우하" };
        string[] lines = new string[4];
        for (int i = 0; i < 4; i++)
            lines[i] = $"{(selectedIndex == i ? "▶ " : "  ")}{directions[i]}: {options[i]}";
        uiController.ChangeUiText(TextTarget.Option, text: string.Join("\n", lines));
        uiController.ActiveOptionTextUi(true);
    }

    public void ShowDialogue(Dialogue dialogue)
    {
        if (uiController == null || dialogue == null)
            return;

        uiController.ShowEventView();
        uiController.ChangeUiText(TextTarget.FrontDialogue, dialogue);
        uiController.ChangeBackground(dialogue.background);
    }

    public void ExitChoice()
    {
        uiController?.ActiveOptionTextUi(false);
    }

    public void PlayStoryTransition(Dialogue dialogue, System.Action onCompleted)
    {
        if (uiController == null || dialogue == null)
        {
            onCompleted?.Invoke();
            return;
        }

        ExitChoice();
        uiController.ChangeUiImage(dialogue, onCompleted);
    }
}
