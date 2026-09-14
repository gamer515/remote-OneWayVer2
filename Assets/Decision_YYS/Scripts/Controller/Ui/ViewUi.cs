using UnityEngine;

public class ViewUi : ParentUi
{
    [SerializeField] private RectTransform view;

    public override void SetActivateUi(bool isActive)
    {
        // 이 RawImage는 플레이어 자식 카메라의 전용 RenderTexture를 이미 참조합니다.
        // 가운데 맵 화면을 복사하지 않고, 이동할 때 이 화면만 켜고 끕니다.
        if (view != null)
            view.gameObject.SetActive(isActive);
    }
}
