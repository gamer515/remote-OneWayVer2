using UnityEngine;
using UnityEngine.UI;

public class ViewUi : ParentUi
{
    [SerializeField] private RectTransform view;

    public override void SetActivateUi(bool isActive)
    {
        // 이 RawImage는 플레이어 자식 카메라의 전용 RenderTexture를 이미 참조합니다.
        // 가운데 맵 화면을 복사하지 않고, 이동할 때 이 화면만 켜고 끕니다.
        if (view != null)
        {
            view.gameObject.SetActive(isActive);
            if (isActive)
            {
                // Screen Space Camera에서 이벤트 카드보다 확실히 앞에 그려지도록 합니다.
                Vector3 position = view.localPosition;
                view.localPosition = new Vector3(position.x, position.y, 0f);
                view.SetAsLastSibling();
                if (view.GetComponent<RawImage>()?.texture == null)
                    Debug.LogWarning("Walking_View RawImage에 RenderTexture가 연결되지 않았습니다.", this);
            }
        }
        else if (isActive)
            Debug.LogWarning("ViewUi의 view 참조가 비어 있어 Walking_View를 표시할 수 없습니다.", this);
    }
}
