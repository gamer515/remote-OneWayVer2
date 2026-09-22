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
        uiController?.ShowEventView();
    }

    public void ShowWalkingView() => uiController?.ShowWalkingView();

    public void ShowEncounterPrompt(string prompt, string[] options, int selectedIndex)
    {
        if (uiController == null)
            return;

        uiController.ShowEventView();
        uiController.ChangeUiText(TextTarget.FrontDialogue, text: prompt);
        ShowOptions(options, selectedIndex);
    }

    public void ShowOptions(string[] options, int selectedIndex)
    {
        if (uiController == null)
            return;

        if (options == null || options.Length != 4 || selectedIndex < 0 || selectedIndex >= 4)
        {
            uiController.ActiveOptionTextUi(false);
            return;
        }

        string[] directions = { "좌상", "좌하", "우상", "우하" };
        uiController.ChangeUiText(TextTarget.Option, text: $"▶ {directions[selectedIndex]}: {options[selectedIndex]}");
        uiController.ActiveOptionTextUi(true);
    }

    public void ShowDialogue(Dialogue dialogue)
    {
        if (uiController == null || dialogue == null)
            return;

        uiController.ShowEventView();
        uiController.ChangeUiText(TextTarget.FrontDialogue, dialogue);
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
