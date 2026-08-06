using System;

/// <summary>
/// JoystickLikeGear의 입력 이벤트와 수명주기를 Decision 흐름에 맞게 변환합니다.
/// </summary>
public sealed class DecisionInputController
{
    private readonly JoystickLikeGear gearController;
    private readonly UiController uiController;
    private bool isEnabled;
    private bool isBlocked;

    public event Action<int> GearChanged;
    public event Action<int> GearConfirmed;
    public event Action ScreenClicked;

    public int CurrentGear => gearController != null
        ? gearController.GetCurrentGearDirectly()
        : 0;

    public DecisionInputController(JoystickLikeGear gearController, UiController uiController)
    {
        this.gearController = gearController;
        this.uiController = uiController;
    }

    public void Enable()
    {
        if (isEnabled || gearController == null)
            return;

        gearController.OnGearChanged += HandleGearChanged;
        gearController.OnGearConfirmed += HandleGearConfirmed;
        gearController.OnScreenClicked += HandleScreenClicked;
        if (uiController != null)
            uiController.OnPlayerViewChanged += HandlePlayerViewChanged;
        isEnabled = true;
    }

    public void Disable()
    {
        if (!isEnabled || gearController == null)
            return;

        gearController.OnGearChanged -= HandleGearChanged;
        gearController.OnGearConfirmed -= HandleGearConfirmed;
        gearController.OnScreenClicked -= HandleScreenClicked;
        if (uiController != null)
            uiController.OnPlayerViewChanged -= HandlePlayerViewChanged;
        isBlocked = false;
        isEnabled = false;
    }

    private void HandleGearChanged(int gear)
    {
        if (!isBlocked) GearChanged?.Invoke(gear);
    }

    private void HandleGearConfirmed(int gear)
    {
        if (!isBlocked) GearConfirmed?.Invoke(gear);
    }

    private void HandleScreenClicked()
    {
        if (!isBlocked) ScreenClicked?.Invoke();
    }

    private void HandlePlayerViewChanged(bool playerViewActive)
    {
        // 3D 시점에서는 기어를 둘러보기 용도로만 사용하고 스토리 입력은 전달하지 않습니다.
        isBlocked = playerViewActive;
    }
}
