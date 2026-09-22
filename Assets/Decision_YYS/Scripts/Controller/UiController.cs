using System;
using TMPro;
using UnityEngine;
using static Constants;

public class UiController : MonoBehaviour
{
    public enum DisplayMode { Event, Walking }

    public DisplayMode CurrentDisplayMode { get; private set; } = DisplayMode.Event;

    [SerializeField] private MainStoryUi mainStoryUi;
    [SerializeField] private ViewUi viewUi;

    public event Action<bool> OnPlayerViewChanged;

    public void ActiveOptionTextUi(bool isActive)
    {
        if (mainStoryUi != null)
            mainStoryUi.SetActiveTextUi(isActive);
    }

    public void ChangeUiText(TextTarget target, Dialogue dialogue = null, string text = null)
    {
        if (mainStoryUi == null)
            return;

        mainStoryUi.WriteText(target, dialogue != null ? SanitizeText(dialogue.text) : text);
    }

    public void ChangeUiImage(Dialogue dialogue, Action onCompleted = null)
    {
        if (dialogue == null || mainStoryUi == null)
        {
            onCompleted?.Invoke();
            return;
        }

        ShowEventView();
        mainStoryUi.StartSwapStoryScreen(dialogue, SanitizeText(dialogue.text), onCompleted);
    }

    private string SanitizeText(string text)
    {
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("{", "").Replace("}", "");
    }

    public void TurnOn_Off3DView()
    {
        ActiveMapOrStoryView(CurrentDisplayMode != DisplayMode.Walking);
    }

    public void ActiveMapOrStoryView(bool turn)
    {
        SetDisplayMode(turn ? DisplayMode.Walking : DisplayMode.Event);
    }

    [ContextMenu("화면 확인/이동 화면")]
    public void ShowWalkingView() => SetDisplayMode(DisplayMode.Walking);

    [ContextMenu("화면 확인/이벤트 화면")]
    public void ShowEventView() => SetDisplayMode(DisplayMode.Event);

    private void SetDisplayMode(DisplayMode mode)
    {
        bool walking = mode == DisplayMode.Walking;

        // 플레이어 화면은 항상 유지하고 이벤트 상태에서 텍스트만 표시합니다.
        if (viewUi != null)
            viewUi.SetActivateUi(true);
        if (mainStoryUi != null)
            mainStoryUi.SetActivateUi(!walking);

        bool changed = CurrentDisplayMode != mode;
        CurrentDisplayMode = mode;
        if (changed)
            OnPlayerViewChanged?.Invoke(walking);
    }
}
