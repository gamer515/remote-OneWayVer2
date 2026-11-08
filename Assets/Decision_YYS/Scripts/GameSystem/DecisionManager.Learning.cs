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
        progress.learning ??= new MineLearningProgress();
        learningStation.Initialize(progress.learning,session.RunSeed,()=>statContainer.stats,
            (ability,cost)=>
            {
                var stats=statContainer.stats;if(stats[ability]<cost) return false;
                stats[ability]-=cost;statContainer.SetStats(stats);guideProgress.committedStats=stats;SyncAbilityWallet();SaveGuideCheckpoint();
                if(stats[0]>0) return true;
                FinishRunAndReturnToMainMenu("학습 비용 지불 후 힘 0");return false;
            },
            (ability,reward)=>
            {
                var stats=statContainer.stats;stats[ability]+=reward;statContainer.SetStats(stats);guideProgress.committedStats=stats;SyncAbilityWallet();
            },SaveGuideCheckpointForLearning);
    }
    private void SaveGuideCheckpointForLearning() => SaveGuideCheckpoint();
    private void TickLearning()
    {
        bool busy=collectingTax || mine?.activityRemaining>0 || currentState==Constants.StoryState.Duel || currentState==Constants.StoryState.Transitioning ||
            (duelMiniGame!=null && duelMiniGame.IsRunning) || (tutorialMiniGames!=null && tutorialMiniGames.CurrentKnightTarget!=null);
        learningStation?.Tick(Time.deltaTime,busy);
    }
}
