public static class Constants
{
    public enum GameState { Main, Story, Battle }
    public enum StoryState { ShowingStory, WaitingForChoice, MovingToEncounter, Transitioning }
    public enum Chapter { Initial, Combat, Knowledge };
    public enum TextTarget { FrontDialogue, Option }
    public enum Gear { Neutral, EvilGood, EvilBad, GoodGood, GoodBad };
}
