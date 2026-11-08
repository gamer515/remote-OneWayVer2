using System.Linq;
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
        if (options == null || options.Length < 2)
        {
            uiController.ActiveOptionTextUi(false);
            return;
        }

        bool selected = selectedIndex >= 0 && selectedIndex < options.Length;
        const int visibleOptions = 4;
        int first = selected ? System.Math.Max(0, selectedIndex - visibleOptions + 1) : 0;
        int last = System.Math.Min(options.Length, first + visibleOptions);
        string text = string.Join("\n", options.Select((option, index) =>
            (selected && index == selectedIndex ? "▶ " : "   ") + (index + 1) + ". " + option).Skip(first).Take(last - first));
        string overflow = (first > 0 ? "↑" : "") + (last < options.Length ? "↓" : "");
        bool canConfirm = selected && !(options[selectedIndex] ?? "").EndsWith(" (잠김)", System.StringComparison.Ordinal);
        uiController.PreviewChoice(text, overflow, canConfirm);
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
