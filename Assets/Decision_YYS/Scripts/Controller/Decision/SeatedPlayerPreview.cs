using UnityEngine;

public sealed class SeatedPlayerPreview : MonoBehaviour
{
    [SerializeField] private GameObject seatedPlayer;
    [SerializeField] private DuelMiniGameBridge duel;
    [SerializeField] private TutorialMiniGameController miniGames;
    private void LateUpdate()
    {
        if(seatedPlayer==null)return;
        bool visible=(duel==null||!duel.IsRunning)&&(miniGames==null||miniGames.CurrentKnightTarget==null);
        if(seatedPlayer.activeSelf!=visible)seatedPlayer.SetActive(visible);
    }
}
