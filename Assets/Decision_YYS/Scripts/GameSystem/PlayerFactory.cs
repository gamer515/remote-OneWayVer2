using UnityEngine;

/// <summary>
/// 플레이어 프리팹 생성과 초기 카메라 상태 설정을 담당합니다.
/// </summary>
public sealed class PlayerFactory
{
    public Player Create(GameObject playerPrefab, bool cameraEnabled)
    {
        if (playerPrefab == null)
            return null;

        GameObject instance = Object.Instantiate(playerPrefab);
        instance.SetActive(true);

        Player player = instance.GetComponent<Player>();
        Camera playerCamera = instance.GetComponentInChildren<Camera>();
        if (playerCamera != null)
            playerCamera.enabled = cameraEnabled;

        return player;
    }
}
