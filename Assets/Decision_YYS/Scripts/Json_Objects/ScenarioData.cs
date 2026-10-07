using System;
using System.Collections.Generic;

public enum DialogueType
{
    Unknown,
    Next,
    Choice,
    End
}

[Serializable]
public class ScenarioData
{
    public List<Dialogue> MainStory;
}

[Serializable]
public class Dialogue
{
    public int id;
    public string type;
    public string text;
    public string npcEmotion;
    public string[] options;
    public StoryChoiceAction[] choiceActions;
    public string startAction;
    public int duelHitTarget;
    public string winStoryPath;
    public string loseStoryPath;
    public string errorStoryPath;
    public string eventIdStable;
    public string speakerId;
    public string semanticText;
    public string nextStoryPath;
    public GuideCoinAction guideAction;

    public DialogueType Type
    {
        get
        {
            // JSON 호환을 위해 원본 string 필드는 유지하고 게임 로직에서만 enum으로 해석합니다.
            return Enum.TryParse(type, true, out DialogueType parsedType) &&
                   Enum.IsDefined(typeof(DialogueType), parsedType)
                ? parsedType
                : DialogueType.Unknown;
        }
    }

    public bool IsChoice => Type == DialogueType.Choice;
    public bool IsEnd => Type == DialogueType.End;
}

[Serializable]
public sealed class StoryChoiceAction
{
    public string choiceId;
    // continue: 다음 카드, story: 별도 Story 폴더로 분기, skip: 조우 종료.
    public string action;
    public string storyPath;
    public string[] requiresUnlocks;
    public string[] grantsUnlocks;
    public string relationshipId;
    public int relationshipDelta;
}
