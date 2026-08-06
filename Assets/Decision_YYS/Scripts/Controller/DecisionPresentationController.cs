using UnityEngine;
using static Constants;

/// <summary>
/// DecisionScene의 UI 표현 규칙을 한곳에서 관리합니다.
/// </summary>
public sealed class DecisionPresentationController
{
    private const string ChoicePrompt = "선택지를 선택하세요.";

    private readonly UiController uiController;
    private readonly GameObject playerViewUI;

    public DecisionPresentationController(UiController uiController, GameObject playerViewUI)
    {
        this.uiController = uiController;
        this.playerViewUI = playerViewUI;
    }

    public void ShowPlayerViewControl()
    {
        if (playerViewUI != null)
            playerViewUI.SetActive(true);
    }

    public void ShowDialogue(Dialogue dialogue)
    {
        if (uiController == null || dialogue == null)
            return;

        uiController.ChangeUiText(TextTarget.FrontDialogue, dialogue);
        uiController.ChangeBackground(dialogue.background);
    }

    public void EnterChoice(string optionText = null)
    {
        if (uiController == null)
            return;

        uiController.ActiveOptionTextUi(true);
        ShowOption(string.IsNullOrEmpty(optionText) ? ChoicePrompt : optionText);
    }

    public void ExitChoice()
    {
        uiController?.ActiveOptionTextUi(false);
    }

    public void ShowOption(string optionText)
    {
        if (uiController == null)
            return;

        uiController.ChangeUiText(
            TextTarget.Option,
            text: string.IsNullOrEmpty(optionText) ? ChoicePrompt : optionText);
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
