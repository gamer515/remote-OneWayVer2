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
    // AI 통합본과의 기존 호환을 위해 런타임 Dialogue에서는 문자열을 유지합니다.
    public string change;
    public string type;
    public bool isTransition; // 제거 or 수정
    public string background; // 제거 or 수정
    public string character; // 제거 or 수정
    public string eventId;
    public string text;
    public string npcEmotion;
    public int[] statWeights;
    public string[] options;

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

    public bool ShouldRelay => bool.TryParse(change, out bool result) && result;
    public bool IsChoice => Type == DialogueType.Choice;
    public bool IsEnd => Type == DialogueType.End;
}
