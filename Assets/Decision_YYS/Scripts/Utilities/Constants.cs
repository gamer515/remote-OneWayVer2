public static class Constants
{
    public enum GameState { Main, Story, Battle }
    public enum StoryState { ShowingStory, WaitingForChoice, Transitioning }
    public enum Chapter { Initial, MartialArts, Wisdom };
    public enum StatType { Wealth, Strength, Wisdom, Charm }
    public enum TextTarget { FrontDialogue, BackDialogue, Option }
    public enum Gear { Neutral, EvilGood, EvilBad, GoodGood, GoodBad };
}
