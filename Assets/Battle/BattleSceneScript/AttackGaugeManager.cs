using UnityEngine;

public class AttackGaugeManager : MonoBehaviour
{
    public BattleStateMachine stateMachine;
    public RectTransform striker;
    public RectTransform targetCenter;
    public float moveSpeed = 10f;
    public float limitX = 600f;

    [Header("Judgment Settings")]
    public float perfectDistance = 5f;  // 기존 50f -> 20f (더 좁게!)
    public float goodDistance = 5f;     // 기존 200f -> 80f (더 좁게!)


    private bool isMoving = false;
    private int direction = 1;

    public void StartGauge()
    {
        // gameObject.SetActive(true); <-- 삭제! (BattleManager는 항상 켜져있어야 함)
        striker.anchoredPosition = new Vector2(-limitX, 0);
        isMoving = true;
    }

    void Update()
    {
        if (!isMoving) return;

        float currentX = striker.anchoredPosition.x;
        currentX += moveSpeed * direction * Time.deltaTime * 100f;

        if (currentX > limitX) direction = -1;
        else if (currentX < -limitX) direction = 1;

        striker.anchoredPosition = new Vector2(currentX, 0);

        if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Space))
        {
            StopAndCalculate();
        }
    }

    private void StopAndCalculate()
    {
        isMoving = false;

        float distance = Mathf.Abs(striker.anchoredPosition.x - targetCenter.anchoredPosition.x);
        float damage = 0;

        if (distance < perfectDistance)
        {
            Debug.Log("판정: Perfect! 1 데미지");
            damage = 1f;
        }
        else if (distance < goodDistance)
        {
            Debug.Log("판정: Good! 1 데미지");
            damage = 1f;
        }
        else
        {
            Debug.Log("판정: Miss! 데미지 없음");
            damage = 0f;
        }

        // gameObject.SetActive(false); <-- 삭제! (여기서 끄면 게임 멈춤)

        // 결과 전달
        stateMachine.OnPlayerAttackComplete(damage);
    }
}