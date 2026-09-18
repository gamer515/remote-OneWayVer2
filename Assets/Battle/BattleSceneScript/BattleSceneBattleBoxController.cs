using UnityEngine;
using System.Collections;

public partial class BattleSceneBattleBoxController : MonoBehaviour
{
    
    [Header("Wall Transforms")]
    public Transform topWall;
    public Transform bottomWall;
    public Transform leftWall;
    public Transform rightWall;

    [Header("Box Configurations")]
    // 스크린샷의 샌즈 대화창과 유사한 기본값 (유니티 유닛 단위)
    public Vector2 dialogueSize = new Vector2(14f, 4f);
    public Vector2 dialoguePos = new Vector2(0f, -2.5f);
    public float wallThickness = 0.15f;

    [Header("UI Reference")]
    public GameObject dialogueContent; // 대화 텍스트가 담긴 UI 오브젝트
    private Coroutine resizeCoroutine;
    public TypewriterEffect typewriter; // 새로 추가: 인스펙터에서 연결해주세요!
    public string textToSay = "* ??????\n  ???????"; // 테스트용 텍스트

    // 대화창 모드로 전환하는 함수
    
    public void SetDialogueMode(float duration, string text) // text 파라미터 추가
    {
        CancelAnimations();
        SetWalls(true, false);
        textToSay = text;
        if (resizeCoroutine != null) StopCoroutine(resizeCoroutine);

        // 대화창 텍스트를 활성화
        if (dialogueContent != null) dialogueContent.SetActive(true);

        // 게이지 UI가 켜져 있다면 꺼줌
        if (gaugeUI != null) gaugeUI.SetActive(false);

        // 추가: 캔버스가 켜지자마자 예전 글자를 즉시 비워줍니다!
        if (typewriter != null) typewriter.ClearText();

        // 1. 상자 크기 조절 시작
        resizeCoroutine = StartCoroutine(AnimateBox(dialogueSize, dialoguePos, duration));

        // 2. 상자가 어느 정도 커졌을 때 텍스트를 활성화 (약간의 딜레이)
        StartCoroutine(ShowTextDelayed(duration * 0.8f));
    }

    private IEnumerator ShowTextDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (dialogueContent != null)
        {
            dialogueContent.SetActive(true);
        }

        // 텍스트 UI가 켜진 직후에 타이핑 효과 시작!
        if (typewriter != null)
        {
            typewriter.PlayText(textToSay);
        }
    }

    private IEnumerator AnimateBox(Vector2 targetSize, Vector2 targetPos, float duration)
    {
        Vector2 startSize = GetCurrentSize();
        Vector2 startPos = transform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0, 1, elapsed / duration);

            Vector2 currentSize = Vector2.Lerp(startSize, targetSize, t);
            transform.position = Vector2.Lerp(startPos, targetPos, t);

            UpdateWalls(currentSize);
            yield return null;
        }

        UpdateWalls(targetSize);
        transform.position = targetPos;
    }

    private void UpdateWalls(Vector2 size)
    {
        float halfW = size.x / 2f;
        float halfH = size.y / 2f;

        topWall.localPosition = new Vector3(0, halfH, 0);
        topWall.localScale = new Vector3(size.x + wallThickness, wallThickness, 1);

        bottomWall.localPosition = new Vector3(0, -halfH, 0);
        bottomWall.localScale = new Vector3(size.x + wallThickness, wallThickness, 1);

        leftWall.localPosition = new Vector3(-halfW, 0, 0);
        leftWall.localScale = new Vector3(wallThickness, size.y + wallThickness, 1);

        rightWall.localPosition = new Vector3(halfW, 0, 0);
        rightWall.localScale = new Vector3(wallThickness, size.y + wallThickness, 1);
    }

    private Vector2 GetCurrentSize()
    {
        return new Vector2(rightWall.localPosition.x * 2f, topWall.localPosition.y * 2f);
    }

   /* public void ChangeBox(Vector2 targetSize, Vector2 targetPos, float duration)
    {
        if (resizeCoroutine != null) StopCoroutine(resizeCoroutine);

        // 대화 내용이 있다면 상자가 바뀔 때 꺼줍니다. (전투 모드로 전환 대비)
        if (dialogueContent != null) dialogueContent.SetActive(false);

        resizeCoroutine = StartCoroutine(AnimateBox(targetSize, targetPos, duration));
    }*/

    public void ChangeBox(Vector2 targetSize, Vector2 targetCenter, float duration)
    {
        CancelAnimations();
        HideUI();
        SetWalls(true, true);
        // 새로 추가: 상자 크기가 변하기 시작한다는 건 대화가 끝났다는 뜻이므로 글자를 날려버립니다.
        if (typewriter != null)
        {
            typewriter.StopAndClear();
        }

        resizeCoroutine = StartCoroutine(AnimateBox(targetSize, targetCenter, duration));
    }

    [Header("Gauge Configurations")]
    public Vector2 gaugeSize = new Vector2(14f, 3f); // 게이지용 상자 크기
    public Vector2 gaugePos = new Vector2(0f, -2.5f); // 게이지용 상자 위치
    public GameObject gaugeUI; // 위에 만든 GaugeUI 오브젝트 연결

    public void SetGaugeMode(float duration = 0.3f)
    {
        CancelAnimations();
        SetWalls(true, false);
        if (resizeCoroutine != null) StopCoroutine(resizeCoroutine);

        // 대화창 끄고 게이지 UI 켜기
        if (dialogueContent != null) dialogueContent.SetActive(false);
        if (gaugeUI != null) gaugeUI.SetActive(true); // 여기서 시각적 UI를 켭니다!

        resizeCoroutine = StartCoroutine(AnimateBox(gaugeSize, gaugePos, duration));
    }

    public Transform[] Walls => new[] { topWall, leftWall, bottomWall, rightWall };
    public void CancelAnimations() { StopAllCoroutines(); resizeCoroutine = null; }
    public void HideUI()
    {
        if (typewriter != null) typewriter.StopAndClear();
        if (dialogueContent != null) dialogueContent.SetActive(false);
        if (gaugeUI != null) gaugeUI.SetActive(false);
    }
    public void SetWalls(bool visible, bool collision)
    {
        foreach (var wall in Walls) SetWall(wall, visible, collision);
    }
    public void SetWall(Transform wall, bool visible, bool collision)
    {
        wall.gameObject.SetActive(true);
        var renderer = wall.GetComponent<Renderer>();
        if (renderer != null) renderer.enabled = visible;
        var collider = wall.GetComponent<Collider2D>();
        if (collider != null) collider.enabled = collision;
    }
    public sealed class Snapshot
    {
        public Vector3 center;
        public Vector3[] positions = new Vector3[4], scales = new Vector3[4];
        public bool[] visible = new bool[4], collision = new bool[4];
    }
    public Snapshot Capture()
    {
        var state = new Snapshot { center = transform.position };
        var walls = Walls;
        for (int i = 0; i < 4; i++)
        {
            state.positions[i] = walls[i].localPosition;
            state.scales[i] = walls[i].localScale;
            state.visible[i] = walls[i].GetComponent<Renderer>().enabled;
            state.collision[i] = walls[i].GetComponent<Collider2D>().enabled;
        }
        return state;
    }
    public void Restore(Snapshot state)
    {
        CancelAnimations();
        transform.position = state.center;
        var walls = Walls;
        for (int i = 0; i < 4; i++)
        {
            walls[i].localPosition = state.positions[i];
            walls[i].localScale = state.scales[i];
            SetWall(walls[i], state.visible[i], state.collision[i]);
        }
    }
    public IEnumerator OpenCorridor(Rect bounds, float duration)
    {
        CancelAnimations(); HideUI(); SetWalls(true, false);
        Vector3 a = topWall.localScale, b = bottomWall.localScale;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float p = Mathf.SmoothStep(0, 1, t / duration);
            topWall.localScale = new Vector3(Mathf.Lerp(a.x, 0, p), a.y, a.z);
            bottomWall.localScale = new Vector3(Mathf.Lerp(b.x, 0, p), b.y, b.z);
            yield return null;
        }
        SetWall(topWall, false, false); SetWall(bottomWall, false, false);
        Vector3 left = leftWall.position, right = rightWall.position;
        Vector3 leftScale = leftWall.localScale, rightScale = rightWall.localScale;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float p = Mathf.SmoothStep(0, 1, t / duration);
            leftWall.position = Vector3.Lerp(left, new Vector3(bounds.xMin, bounds.center.y, 0), p);
            rightWall.position = Vector3.Lerp(right, new Vector3(bounds.xMax, bounds.center.y, 0), p);
            leftWall.localScale = Vector3.Lerp(leftScale, new Vector3(wallThickness, bounds.height, 1), p);
            rightWall.localScale = Vector3.Lerp(rightScale, new Vector3(wallThickness, bounds.height, 1), p);
            yield return null;
        }
        leftWall.position = new Vector3(bounds.xMin, bounds.center.y, 0);
        rightWall.position = new Vector3(bounds.xMax, bounds.center.y, 0);
        leftWall.localScale = rightWall.localScale = new Vector3(wallThickness, bounds.height, 1);
    }
    public IEnumerator BuildSequential(Vector2 size, Vector2 center, float duration)
    {
        CancelAnimations(); HideUI();
        transform.position = center; UpdateWalls(size); SetWalls(false, false);
        foreach (var wall in Walls)
        {
            Vector3 target = wall.localScale;
            bool horizontal = wall == topWall || wall == bottomWall;
            Vector3 start = horizontal ? new Vector3(0, target.y, 1) : new Vector3(target.x, 0, 1);
            wall.localScale = start; SetWall(wall, true, false);
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                wall.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0, 1, t / duration));
                yield return null;
            }
            wall.localScale = target;
        }
        SetWalls(true, true);
    }
}
