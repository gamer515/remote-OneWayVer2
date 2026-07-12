using UnityEngine;

public class DynamicFaceController : MonoBehaviour
{
    [Header("Face Parts")]
    [SerializeField] private Transform leftEye;
    [SerializeField] private Transform rightEye;
    [SerializeField] private Transform leftBrow;
    [SerializeField] private Transform rightBrow;
    [SerializeField] private SpriteRenderer mouthNeutral; // 네모
    [SerializeField] private SpriteRenderer mouthHappy;   // 세모

    [Header("Expression Sensitivity")]
    [Tooltip("부위들이 이동할 최대 범위 (UI 내부 Unit/Local 단위)")]
    [SerializeField] private float moveRange = 0.05f;      
    [Tooltip("화날 때 눈썹이 꺾이는 최대 각도")]
    [SerializeField] private float browRotationAngle = 20f;  
    [Tooltip("악(Bad) 선택 시 입술 세로 확장 배율")]
    [SerializeField] private float mouthStretchY = 1.4f; 
    [Tooltip("악(Bad) 선택 시 입술 가로 축소 배율")]
    [SerializeField] private float mouthShrinkX = 0.8f;  
    [SerializeField] private float smoothTime = 0.12f;

    // 초기 상태 저장용
    private Vector3 leBasePos, reBasePos, lbBasePos, rbBasePos, mnBasePos, mhBasePos;
    private Quaternion lbBaseRot, rbBaseRot;
    private Vector3 mnBaseScale, mhBaseScale;

    // 실시간 보간용
    private Vector2 currentInput;
    private Vector2 targetInput;
    private Vector2 inputVel;

    private void Start()
    {
        // 1. 모든 부위의 '기준' 상태(에디터 설정값)를 완벽하게 저장
        if (leftEye) leBasePos = leftEye.localPosition;
        if (rightEye) reBasePos = rightEye.localPosition;
        
        if (leftBrow) {
            lbBasePos = leftBrow.localPosition;
            lbBaseRot = leftBrow.localRotation;
        }
        if (rightBrow) {
            rbBasePos = rightBrow.localPosition;
            rbBaseRot = rightBrow.localRotation;
        }

        if (mouthNeutral) {
            mnBasePos = mouthNeutral.transform.localPosition;
            mnBaseScale = mouthNeutral.transform.localScale;
        }
        if (mouthHappy) {
            mhBasePos = mouthHappy.transform.localPosition;
            mhBaseScale = mouthHappy.transform.localScale;
            SetAlpha(mouthHappy, 0f); // 처음엔 숨김
        }
    }

    private void Update()
    {
        // 1. 기어 입력값 보간 (0,0 ~ -1,1 등)
        currentInput = Vector2.SmoothDamp(currentInput, targetInput, ref inputVel, smoothTime);

        // 2. 눈 이동 (시선 처리)
        if (leftEye) leftEye.localPosition = leBasePos + (Vector3)currentInput * moveRange;
        if (rightEye) rightEye.localPosition = reBasePos + (Vector3)currentInput * moveRange;

        // 3. 눈썹 변형 (V자 꺾기 + 미간 모으기)
        UpdateBrows();

        // 4. 입술 변형 (방향 틀기 + 직사각형 모핑)
        UpdateMouths();
    }

    private void UpdateBrows()
    {
        if (!leftBrow || !rightBrow) return;

        // '악(Bad)' 성향 가중치 계산 (기어가 위쪽(+Y)으로 갈수록 표정 변화 시작)
        float badWeight = Mathf.Clamp01(currentInput.y); 

        // 미간으로 모이는 효과 + 기어 방향 이동
        Vector3 browShift = (Vector3)currentInput * (moveRange * 0.8f);
        leftBrow.localPosition = lbBasePos + browShift + new Vector3(badWeight * (moveRange * 0.5f), 0, 0);
        rightBrow.localPosition = rbBasePos + browShift + new Vector3(-badWeight * (moveRange * 0.5f), 0, 0);

        // [추가] 좌우 방향에 따른 눈썹 각도 차별화
        // currentInput.x가 음수(왼쪽, 악반)이면 V자 (\/), 양수(오른쪽, 악찬)이면 역 V자 (/\)
        // x의 부호에 따라 회전 방향을 결정합니다.
        float sideWeight = currentInput.x; // -1 (왼쪽) ~ 1 (오른쪽)
        
        // 왼쪽 눈썹 회전: 왼쪽일 때 -, 오른쪽일 때 +
        float lRot = sideWeight * browRotationAngle * badWeight;
        // 오른쪽 눈썹 회전: 왼쪽일 때 +, 오른쪽일 때 -
        float rRot = -sideWeight * browRotationAngle * badWeight;

        leftBrow.localRotation = lbBaseRot * Quaternion.Euler(0, 0, lRot);
        rightBrow.localRotation = rbBaseRot * Quaternion.Euler(0, 0, rRot);
    }

    private void UpdateMouths()
    {
        if (!mouthNeutral) return;

        // 행복(Happy) 가중치 (왼쪽 아래)
        float happyWeight = 0f;
        if (currentInput.x < 0 && currentInput.y < 0)
            happyWeight = Mathf.Clamp01(new Vector2(-currentInput.x, -currentInput.y).magnitude);

        // 악(Bad) 가중치 (위쪽)
        float badWeight = Mathf.Clamp01(currentInput.y);

        // 입술 위치 이동 (기어 방향으로 쏠림)
        Vector3 mouthShift = (Vector3)currentInput * moveRange;
        mouthNeutral.transform.localPosition = mnBasePos + mouthShift;

        // 입술 모양 변형 (BadWeight에 따라 가로는 좁아지고 세로는 길어짐)
        float sX = Mathf.Lerp(1f, mouthShrinkX, badWeight);
        float sY = Mathf.Lerp(1f, mouthStretchY, badWeight);
        mouthNeutral.transform.localScale = new Vector3(mnBaseScale.x * sX, mnBaseScale.y * sY, mnBaseScale.z);

        // 행복할 때 세모 입술로 교체 (투명도 모핑)
        SetAlpha(mouthNeutral, 1f - happyWeight);
        if (mouthHappy) {
            SetAlpha(mouthHappy, happyWeight);
            mouthHappy.transform.localPosition = mhBasePos + mouthShift;
            // 세모 입술도 행복할 때만 원래 크기로 커짐
            mouthHappy.transform.localScale = mhBaseScale * happyWeight;
        }
    }

    private void SetAlpha(SpriteRenderer sr, float a)
    {
        if (!sr) return;
        Color c = sr.color;
        c.a = a;
        sr.color = c;
    }

    public void SetGearRatio(float x, float y)
    {
        targetInput = new Vector2(x, y);
    }
}
