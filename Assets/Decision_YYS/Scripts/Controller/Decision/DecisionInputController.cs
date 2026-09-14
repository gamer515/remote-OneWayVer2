using System;

/// <summary>
/// 기어에서 선택한 코인 종류만 베팅 시스템에 전달합니다.
/// </summary>
public sealed class DecisionInputController
{
    private readonly JoystickLikeGear gearController;
    private readonly UiController uiController;
    private bool isEnabled;
    private bool isBlocked;

    public event Action<int> CoinTypeChanged;

    public int SelectedCoinIndex => gearController != null
        ? gearController.SelectedCoinIndex
        : -1;

    public DecisionInputController(JoystickLikeGear gearController, UiController uiController)
    {
        this.gearController = gearController;
        this.uiController = uiController;
    }

    public void Enable()
    {
        if (isEnabled || gearController == null)
            return;

        gearController.OnCoinTypeChanged += HandleCoinTypeChanged;
        if (uiController != null)
            uiController.OnPlayerViewChanged += HandlePlayerViewChanged;
        isEnabled = true;
    }

    public void Disable()
    {
        if (!isEnabled || gearController == null)
            return;

        gearController.OnCoinTypeChanged -= HandleCoinTypeChanged;
        if (uiController != null)
            uiController.OnPlayerViewChanged -= HandlePlayerViewChanged;
        isBlocked = false;
        isEnabled = false;
    }

    private void HandleCoinTypeChanged(int coinIndex)
    {
        if (!isBlocked) CoinTypeChanged?.Invoke(coinIndex);
    }

    private void HandlePlayerViewChanged(bool playerViewActive)
    {
        // 이동 화면에서는 코인 선택 전달을 막고, 이벤트 화면으로 돌아오면 다시 허용합니다.
        isBlocked = playerViewActive;
    }
}
