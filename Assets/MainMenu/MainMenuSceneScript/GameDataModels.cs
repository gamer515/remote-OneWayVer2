using System;
using System.Collections.Generic;

[Serializable]
public class GameRecord
{
    public int selectionId;
    public string question;
    public string playerAnswer; // ¼±Âù, ¼±¹Ý, ¾ÇÂù, ¾Ç¹Ý µî
    public string statChanges;
}

[Serializable]
public class RoundData
{
    public int roundNumber;
    public string playDate;
    public List<GameRecord> logs;
}

[Serializable]
public class AllPlayHistory
{
    public List<RoundData> allRounds;
}