using System;
using System.Collections.Generic;

[Serializable]
public class ScenarioData
{
    public List<Dialogue> MainStory;
    //public List<Dialogue> SideStory;
}

[Serializable]
public class Dialogue
{
    public int id;
    public string change;
    public string type;
    public bool isTransition;
    public string background;
    public string character;
    public string text;
    public string[] option;
    public string npcEmotion;
    public int[] figure;
}
