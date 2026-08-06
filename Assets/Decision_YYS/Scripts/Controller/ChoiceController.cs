using static Constants;

public readonly struct ChoiceResult
{
    public bool IsValid { get; }
    public int OptionIndex { get; }
    public int StatChange { get; }
    public string OptionText { get; }

    public ChoiceResult(bool isValid, int optionIndex, int statChange, string optionText)
    {
        IsValid = isValid;
        OptionIndex = optionIndex;
        StatChange = statChange;
        OptionText = optionText;
    }
}

/// <summary>
/// 기어 입력을 지문의 선택 결과로 변환합니다.
/// </summary>
public sealed class ChoiceController
{
    public ChoiceResult Resolve(Dialogue dialogue, int gear)
    {
        int optionIndex = GetOptionIndex(gear);
        if (dialogue == null || !dialogue.IsChoice || optionIndex < 0)
            return default;

        if (dialogue.option == null || dialogue.figure == null ||
            optionIndex >= dialogue.option.Length || optionIndex >= dialogue.figure.Length)
        {
            return default;
        }

        return new ChoiceResult(
            true,
            optionIndex,
            dialogue.figure[optionIndex],
            dialogue.option[optionIndex]);
    }

    public string GetOptionText(Dialogue dialogue, int gear)
    {
        int optionIndex = GetOptionIndex(gear);
        if (dialogue == null || !dialogue.IsChoice || dialogue.option == null ||
            optionIndex < 0 || optionIndex >= dialogue.option.Length)
            return null;

        return dialogue.option[optionIndex];
    }

    private static int GetOptionIndex(int gear)
    {
        if (gear == (int)Gear.EvilGood) return 0;
        if (gear == (int)Gear.EvilBad) return 1;
        if (gear == (int)Gear.GoodGood) return 2;
        if (gear == (int)Gear.GoodBad) return 3;
        return -1;
    }
}
