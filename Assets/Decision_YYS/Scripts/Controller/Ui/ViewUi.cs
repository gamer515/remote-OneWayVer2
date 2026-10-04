using UnityEngine;
using UnityEngine.UI;

public class ViewUi : ParentUi
{
    [SerializeField] private RectTransform view;
    private UnityEngine.UI.Image hitFlash;
    private float hitFlashRemaining;
    private const float HitFlashSeconds = 0.2f;

    public void FlashPlayerHit()
    {
        if (view == null) return;
        if (hitFlash == null)
        {
            var overlay = new GameObject("DuelHitFlash", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            var rect = overlay.GetComponent<RectTransform>();
            rect.SetParent(view, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            hitFlash = overlay.GetComponent<UnityEngine.UI.Image>();
            hitFlash.raycastTarget = false;
        }
        hitFlash.rectTransform.SetAsLastSibling();
        hitFlashRemaining = HitFlashSeconds;
        hitFlash.enabled = true;
        hitFlash.color = new Color(1f, 0.04f, 0.02f, 0.28f);
    }

    public void ClearPlayerHitFlash()
    {
        hitFlashRemaining = 0f;
        if (hitFlash != null) hitFlash.enabled = false;
    }

    private void Update()
    {
        if (hitFlash == null || hitFlashRemaining <= 0f) return;
        hitFlashRemaining = Mathf.Max(0f, hitFlashRemaining - Time.unscaledDeltaTime);
        var color = hitFlash.color;
        color.a = 0.28f * hitFlashRemaining / HitFlashSeconds;
        hitFlash.color = color;
        if (hitFlashRemaining == 0f) hitFlash.enabled = false;
    }

    private void OnDisable() => ClearPlayerHitFlash();

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
