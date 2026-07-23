using System;
using TMPro;
using UnityEngine;


public class UiController : MonoBehaviour
{
    [SerializeField] private MainStoryUi mainStoryUi;
    //public StatUi statUi;

    public void ActiveOptionTextUi(bool isActive)
    {
        mainStoryUi.SetActiveTextUi(isActive);
    }

    public void ChangeUiText(string text)
    {
        mainStoryUi.WriteText(text);
    }

    public void ChangeUiImage(Dialogue dialogue)
    {
        Dialogue reviseDialogue = SanitizeText(dialogue);
        mainStoryUi.StartSwapStoryScreen(reviseDialogue);
    }

    private Dialogue SanitizeText(Dialogue dialogue)
    {
        if (dialogue == null || string.IsNullOrEmpty(dialogue.text)) return null;
        dialogue.text = dialogue.text.Replace("{", "").Replace("}", "");
        return dialogue;
    }
}
