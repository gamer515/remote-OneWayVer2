using System.Collections;
using UnityEngine;

/// <summary>
/// 생성된 코인이 어떤 기어 위치에서 선택되었는지 보관합니다.
/// 이후 베팅 점수 계산과 Coin_Exit 회수 단계에서 사용합니다.
/// </summary>
public sealed class BettingCoin : MonoBehaviour
{
    public int CoinTypeIndex { get; private set; }

    public void Initialize(int coinTypeIndex)
    {
        CoinTypeIndex = coinTypeIndex;
    }

    /// <summary>
    /// 물리 동작을 멈추고 Coin_Exit로 빨려 들어가는 이동을 재생합니다.
    /// </summary>
    public IEnumerator MoveToExit(Transform coinExit, float duration)
    {
        if (coinExit == null)
            yield break;

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        Collider[] colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Vector3 startPosition = transform.position;
        Vector3 startScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / Mathf.Max(duration, 0.01f));
            float easedTime = Mathf.SmoothStep(0f, 1f, normalizedTime);

            transform.position = Vector3.Lerp(startPosition, coinExit.position, easedTime);
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, easedTime);
            yield return null;
        }

        transform.position = coinExit.position;
        transform.localScale = Vector3.zero;
    }
}
