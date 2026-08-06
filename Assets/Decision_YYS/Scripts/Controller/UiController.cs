using System;
using TMPro;
using UnityEngine;
using static Constants;

public class UiController : MonoBehaviour
{
    [SerializeField] private MainStoryUi mainStoryUi;
    [SerializeField] private ViewUi viewUi;

    public event Action<bool> OnPlayerViewChanged;

    #region MainStoryUi
    public void ActiveOptionTextUi(bool isActive)
    {
        if (mainStoryUi != null)
            mainStoryUi.SetActiveTextUi(isActive);
    }

    public void ChangeUiText(TextTarget target, Dialogue dialogue = null, string text = null)
    {
        if (mainStoryUi == null)
            return;

        if (dialogue != null) 
        { 
            mainStoryUi.WriteText(target, SanitizeText(dialogue.text));
        }
        else
        {
            mainStoryUi.WriteText(target, text);
        }
    }

    public void ChangeUiImage(Dialogue dialogue, Action onCompleted = null)
    {
        if (dialogue == null)
            return;

        mainStoryUi.StartSwapStoryScreen(
            dialogue,
            SanitizeText(dialogue.text),
            onCompleted);
    }

    public void ChangeBackground(string bgData)
    {
        if (mainStoryUi != null)
            mainStoryUi.ApplyBackground(bgData);
    }

    private string SanitizeText(string text)
    {
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("{", "").Replace("}", "");
    }
    #endregion

    #region MapUi
    public void TurnOn_Off3DView()
    {
        if (viewUi == null)
            return;

        bool playerViewActive = viewUi.WillEnablePlayerView;
        if (!viewUi.ApplyPlayerViewToggle())
            return;

        ActiveMapOrStoryView(playerViewActive);
        OnPlayerViewChanged?.Invoke(playerViewActive);
    }
    #endregion

    public void ActiveMapOrStoryView(bool turn)
    {
        if (viewUi != null) viewUi.SetActivateUi(turn);
        if (mainStoryUi != null) mainStoryUi.SetActivateUi(!turn);
    }
}
