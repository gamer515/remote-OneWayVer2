using UnityEngine;

public partial class DecisionManager
{
    [SerializeField] private MineLearningStation learningStation;
    private void InitializeLearning()
    {
        if (learningStation == null || guideProgress == null) return;
        var progress = guideProgress.mineChapter ??= new MineChapterProgress();
        string folder = ChapterContentCatalog.ChapterFolder(currentOmnibus.chapters[chapterIndex].chapterId);
        learningStation.ConfigureContent(
            ChapterWorkbookContent.Get(folder + "/Books")?.ToObject<MineLearningStation.BookCatalog>(),
            ChapterWorkbookContent.Get(folder + "/Learning")?.ToObject<MineLearningStation.LearningSettings>());
        var books = ChapterWorkbookContent.Get("LearningSystem/Books")?.ToObject<MineLearningStation.BookCatalog>();
        var rules = ChapterWorkbookContent.Get("LearningSystem/Rules")?.ToObject<MineLearningStation.StudyRules>();
        if (books != null && rules != null)
        {
            float calendarDay = mineRules?.dayCycle?.DayLength ?? 0;
            learningStation.ConfigureStudy(books, rules, calendarDay,
                calendarDay > 0 ? progress.elapsedSeconds / calendarDay : 0);
        }
        progress.learning ??= new MineLearningProgress();
        learningStation.DisplayCoinCounts = journeyCoinSupply?.IsReady == true
            ? journeyCoinSupply.AvailableCoinCounts : null;
        learningStation.Initialize(progress.learning,session.RunSeed,()=>statContainer.stats,
            (ability,cost)=>
            {
                var stats=statContainer.stats;if(stats[ability]<cost) return false;
                stats[ability]-=cost;statContainer.SetStats(stats);guideProgress.committedStats=stats;SyncAbilityWallet();
                if(stats[0]>0) return true;
                FinishRunAndReturnToMainMenu("학습 비용 지불 후 힘 0");return false;
            },
            (ability,reward)=>
            {
                var stats=statContainer.stats;stats[ability]+=reward;statContainer.SetStats(stats);guideProgress.committedStats=stats;SyncAbilityWallet();
            },SaveGuideCheckpointForLearning);
        backpackInventory?.BindStudyBooks(learningStation);
    }
    private void SaveGuideCheckpointForLearning() => SaveGuideCheckpoint();
    private void OnApplicationQuit()
    {
        if (learningStation?.Progress != null) SaveGuideCheckpointForLearning();
    }
    private void TickLearning()
    {
        bool busy=collectingTax || mine?.activityRemaining>0 || currentState==Constants.StoryState.Duel || currentState==Constants.StoryState.Transitioning ||
            (duelMiniGame!=null && duelMiniGame.IsRunning) || (tutorialMiniGames!=null && tutorialMiniGames.CurrentKnightTarget!=null);
        learningStation?.Tick(Time.deltaTime,busy);
    }
}
