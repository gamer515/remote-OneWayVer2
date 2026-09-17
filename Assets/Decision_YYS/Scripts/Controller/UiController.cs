using System;
using TMPro;
using UnityEngine;
using static Constants;

public class UiController : MonoBehaviour
{
    public enum DisplayMode { Event, Walking }

    public DisplayMode CurrentDisplayMode { get; private set; } = DisplayMode.Event;
    private bool isStoryTransitionPlaying;
    private DisplayMode? pendingDisplayMode;
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
        if (dialogue == null || mainStoryUi == null)
        {
            onCompleted?.Invoke();
            return;
        }

        ShowEventView();
        isStoryTransitionPlaying = true;
        mainStoryUi.StartSwapStoryScreen(
            dialogue,
            SanitizeText(dialogue.text),
            () =>
            {
                isStoryTransitionPlaying = false;
                if (pendingDisplayMode.HasValue)
                {
                    DisplayMode requestedMode = pendingDisplayMode.Value;
                    pendingDisplayMode = null;
                    SetDisplayMode(requestedMode);
                }
                onCompleted?.Invoke();
            });
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
        // 기존 버튼 연결은 유지하되, 카메라 토글 대신 명시적인 화면 상태를 사용합니다.
        ActiveMapOrStoryView(CurrentDisplayMode != DisplayMode.Walking);
    }
    #endregion

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
        // 셔터 재생 중 부모를 숨기면 Animator가 멈추므로, 완료 뒤 마지막 전환 요청을 적용합니다.
        if (isStoryTransitionPlaying)
        {
            pendingDisplayMode = mode;
            return;
        }
        // 왼쪽 화면만 전환합니다. 맵, 코인, 얼굴, 아이템 보관 영역은 건드리지 않습니다.
        bool turn = mode == DisplayMode.Walking;
        if (mainStoryUi != null) mainStoryUi.SetActivateUi(!turn);
        if (viewUi != null) viewUi.SetActivateUi(turn);
        bool changed = CurrentDisplayMode != mode;
        CurrentDisplayMode = mode;
        if (changed) OnPlayerViewChanged?.Invoke(turn);
    }
}
