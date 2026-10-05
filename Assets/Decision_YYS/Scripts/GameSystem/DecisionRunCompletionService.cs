using System;

/// <summary>회차 완료 저장, 다음 회차 준비, 메뉴 이동을 중복 없이 순서대로 실행합니다.</summary>
public sealed class DecisionRunCompletionService
{
    private readonly Action completeCurrentRun;
    private readonly Func<int> prepareNextPlaythrough;
    private readonly Action loadMainMenu;
    private bool completionRequested;

    public DecisionRunCompletionService(
        Action completeCurrentRun, Func<int> prepareNextPlaythrough, Action loadMainMenu)
    {
        this.completeCurrentRun = completeCurrentRun ?? throw new ArgumentNullException(nameof(completeCurrentRun));
        this.prepareNextPlaythrough = prepareNextPlaythrough ?? throw new ArgumentNullException(nameof(prepareNextPlaythrough));
        this.loadMainMenu = loadMainMenu ?? throw new ArgumentNullException(nameof(loadMainMenu));
    }

    public void Complete()
    {
        if (completionRequested) return;
        completionRequested = true;
        completeCurrentRun();
        prepareNextPlaythrough();
        loadMainMenu();
    }
}
