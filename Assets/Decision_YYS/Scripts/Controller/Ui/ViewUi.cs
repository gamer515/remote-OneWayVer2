using UnityEngine;
using UnityEngine.UI;

public class ViewUi : ParentUi
{
    [SerializeField] private RectTransform view;

    public override void SetActivateUi(bool isActive)
    {
        // 탐험과 이벤트 모두 플레이어 시점인 Walking_View 위에서 표현합니다.
        if (view == null)
        {
            Debug.LogWarning("ViewUi의 view 참조가 비어 있어 Walking_View를 표시할 수 없습니다.", this);
            return;
        }

        view.gameObject.SetActive(true);
        Vector3 position = view.localPosition;
        view.localPosition = new Vector3(position.x, position.y, 0f);
        view.SetAsLastSibling();

        if (view.GetComponent<RawImage>()?.texture == null)
            Debug.LogWarning("Walking_View RawImage에 RenderTexture가 연결되지 않았습니다.", this);
    }
}
