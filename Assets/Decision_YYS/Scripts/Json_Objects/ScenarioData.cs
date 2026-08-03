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
    public string change; // 제거 or 수정
    public string type;
    public bool isTransition; // 제거 or 수정
    public string background; // 제거 or 수정
    public string character; // 제거 or 수정
    public string text;
    public string[] option;
    public string npcEmotion;
    public int[] figure;
}
