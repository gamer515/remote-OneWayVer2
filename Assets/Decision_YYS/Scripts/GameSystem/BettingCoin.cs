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
    /// 물리 동작을 멈추고 Exit_Point까지 미끄러진 뒤 아래로 빠지는 이동을 재생합니다.
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
        Vector3 exitPosition = coinExit.position;
        float slideDuration = Mathf.Max(duration * 0.8f, 0.01f);
        float sinkDuration = Mathf.Max(duration - slideDuration, 0.01f);
        float elapsed = 0f;

        // 코인의 크기를 유지한 채 테이블을 따라 실제 Exit_Point까지 미끄러집니다.
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / slideDuration);
            float easedTime = Mathf.SmoothStep(0f, 1f, normalizedTime);

            transform.position = Vector3.Lerp(startPosition, exitPosition, easedTime);
            yield return null;
        }

        transform.position = exitPosition;

        // 출구에 도착한 뒤 아래로 빠지면서 사라져, 중앙으로 축소되는 느낌을 없앱니다.
        elapsed = 0f;
        Vector3 sinkPosition = exitPosition + Vector3.down * Mathf.Max(startScale.y, 0.25f);
        while (elapsed < sinkDuration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / sinkDuration);
            float easedTime = normalizedTime * normalizedTime;

            transform.position = Vector3.Lerp(exitPosition, sinkPosition, easedTime);
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, easedTime);
            yield return null;
        }

        transform.position = sinkPosition;
        transform.localScale = Vector3.zero;
    }
}
