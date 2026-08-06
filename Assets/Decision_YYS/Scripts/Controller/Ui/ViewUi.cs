using UnityEngine;

public class ViewUi : ParentUi
{
    [SerializeField] private RectTransform view;

    private bool willEnablePlayerView = true;
    public bool WillEnablePlayerView => willEnablePlayerView;

    public bool ApplyPlayerViewToggle()
    {
        Player player = FindFirstObjectByType<Player>();
        if (player == null)
        {
            Debug.LogWarning("플레이어를 찾을 수 없습니다.");
            return false;
        }

        Camera playerCamera = player.GetComponentInChildren<Camera>(true);
        if (playerCamera == null)
        {
            Debug.LogWarning("플레이어 카메라를 찾을 수 없습니다.");
            return false;
        }

        playerCamera.enabled = willEnablePlayerView;
        willEnablePlayerView = !willEnablePlayerView;
        return true;
    }

    public override void SetActivateUi(bool isActive)
    {
        if (view != null)
            view.gameObject.SetActive(isActive);
    }
}
