using Unity.VisualScripting;
using UnityEngine;

public class ViewUi : ParentUi
{
    [SerializeField] private RectTransform view;

    private bool turnOn_OffCamera = true;
    public bool TurnOn_OffCamera { get { return turnOn_OffCamera; } }

    public void ChangeScreen3DView()
    {
        Debug.Log("ChangeScreen3DView 호출됨");

        Player player = FindFirstObjectByType<Player>();
        if (player == null) { Debug.Log("Player 찾지 못함."); return; }

        Camera cam = player.gameObject.GetComponentInChildren<Camera>(true);
        if (cam == null) { Debug.Log("카메라를 찾지 못함"); return; }

        cam.enabled = turnOn_OffCamera;

        turnOn_OffCamera = !turnOn_OffCamera;
    }

    public override void SetActivateUi(bool turn)
    {
        view.gameObject.SetActive(turn);
    }
}
