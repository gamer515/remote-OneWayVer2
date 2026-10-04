public static class Constants
{
    public const int StartingCoinsPerType = 10;
    public enum GameState { Main, Story, Battle }
    public enum StoryState { ShowingStory, WaitingForChoice, Exploring, EncounterPrompt, MovingToEncounter, Transitioning, TutorialResult, Duel }
    public enum Chapter { Initial, Chapter_1, Chapter_2 };
    public enum TextTarget { FrontDialogue, Option }
    public enum Gear { Neutral, EvilGood, EvilBad, GoodGood, GoodBad };
}
