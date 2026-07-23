using System;
using TMPro;
using UnityEngine;
using static Constants;

public class UiController : MonoBehaviour
{
    [SerializeField] private MainStoryUi mainStoryUi;
    //public StatUi statUi;

    public void ActiveOptionTextUi(bool isActive)
    {
        mainStoryUi.SetActiveTextUi(isActive);
    }

    public void ChangeUiText(TextTarget target, Dialogue dialogue = null, string text = null)
    {
        if (dialogue != null) 
        { 
            Dialogue dialogueForText = SanitizeText(dialogue);
            mainStoryUi.WriteText(target, dialogueForText.text);
        }
        else
        {
            mainStoryUi.WriteText(target, text);
        }
    }

    public void ChangeUiImage(Dialogue dialogue)
    {
        Dialogue reviseDialogue = SanitizeText(dialogue);
        mainStoryUi.StartSwapStoryScreen(reviseDialogue);
    }

    public void ChangeBackground(string bgData)
    {
        mainStoryUi.ApplyBackground(bgData);
    }

    private Dialogue SanitizeText(Dialogue dialogue)
    {
        if (dialogue == null || string.IsNullOrEmpty(dialogue.text)) return null;
        dialogue.text = dialogue.text.Replace("{", "").Replace("}", "");
        return dialogue;
    }
}
