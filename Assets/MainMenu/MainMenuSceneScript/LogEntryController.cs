using UnityEngine;

//biography에서 보여주는 로그 모음
public class LogEntryController : MonoBehaviour
{
    public TMPro.TextMeshProUGUI idText, questionText, answerText, statText;
    public void SetLogData(GameRecord log)
    {
        idText.text = log.selectionId.ToString();
        questionText.text = log.question;
        answerText.text = log.playerAnswer;
        statText.text = log.statChanges;
    }
}