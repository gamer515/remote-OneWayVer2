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

    public void ShowOptions(string[] options, int selectedIndex)
    {
        if (uiController == null) return;
        if (options == null || options.Length != 4)
        {
            uiController.ActiveOptionTextUi(false);
            return;
        }
        string[] directions = { "좌상", "좌하", "우상", "우하" };
        bool selected = selectedIndex >= 0 && selectedIndex < 4;
        string text = selected ? options[selectedIndex] ?? string.Empty : string.Empty;
        bool canConfirm = selected && !text.EndsWith(" (잠김)", System.StringComparison.Ordinal);
        uiController.PreviewChoice(text, selected ? directions[selectedIndex] : null, canConfirm);
        uiController.ActiveOptionTextUi(true);
    }

    public void ShowDialogue(Dialogue dialogue)
    {
        if (uiController == null || dialogue == null)
            return;

        uiController.ActiveOptionTextUi(false);
        uiController.ShowEventView();
        uiController.ChangeUiText(TextTarget.FrontDialogue, dialogue);
    }

    public void ConfirmChoice(string text) => uiController?.AppendPlayerChoice(text);
    public void ShowInteraction(string text, string characterId) => uiController?.AppendInteraction(text, characterId);

    public void ExitChoice()
    {
        uiController?.ActiveOptionTextUi(false);
    }

    public void ShowGuideAllocation(string instructions, string summary)
    {
        uiController?.ShowEventView();
        uiController?.ChangeUiText(TextTarget.Option, text: instructions + "\n" + summary);
        uiController?.ActiveOptionTextUi(true);
    }

}
