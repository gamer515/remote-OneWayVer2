using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class BattleStateMachine : MonoBehaviour
{
    public BattleSceneController sequenceController;
    public enum BattleState { Intro, EnemyTurn, PlayerTurn, MidDialogue, End }
    public BattleState currentState;

    public BattleStartData StartData { get; private set; }
    = BattleStartData.CreateDefault();

    public int StageIndex => StartData.StageIndex;

    public void Initialize(BattleStartData data)
    {
        StartData = data
            ?? throw new System.ArgumentNullException(nameof(data));
    }

    //  [추가] 인스펙터에서 보기 좋게 묶어줄 '대사 세트' 구조체입니다.
    [System.Serializable]
    public struct BattleDialogueSet
    {
        [TextArea(2, 3)] public string introText; // 시작 대사
        [TextArea(2, 3)] public string midText;   // 중간 대사
        [TextArea(2, 3)] public string endText;   // 끝(마무리) 대사
    }

    [Header("References")]
    public BattleSceneBattleBoxController boxController;
    public PlayerController player;
    public AttackPatternManager attackManager;
    public TypewriterEffect typewriter;
    public AttackGaugeManager gaugeManager;

    //  [삭제] 기존에 있던 introText, randomTexts, winText 변수 3개는 지워주세요!

    //  [추가] B1, B2, B3 대사를 담을 3칸짜리 배열을 만듭니다.
    [Header("Stage Dialogues")]
    public BattleDialogueSet[] stageDialogues = new BattleDialogueSet[3];

    [Header("Enemy Status")]
    public float enemyHp = 3f;

    private float inputTimer = 0f;



    void Start()
    {
        // 현재 BattleScene은 BattleSceneController가 초기화합니다.
        if (sequenceController != null)
            return;

        // 이전 방식의 전투를 단독 실행하는 경우에는 기본값 사용.
        if (attackManager != null)
            attackManager.Initialize(StartData);

        Debug.Log(
            $"[Battle System] 현재 진입한 전투 스테이지: B{StageIndex}");

        ChangeState(BattleState.Intro);
    }

    void Update()
    {
        if (sequenceController != null) return;
        if (inputTimer > 0) inputTimer -= Time.deltaTime;

        if (IsDialogueState() && !typewriter.IsTyping && inputTimer <= 0)
        {
            if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Space))
            {
                AdvanceFromDialogue();
            }
        }
    }

    private bool IsDialogueState() => currentState == BattleState.Intro || currentState == BattleState.MidDialogue || currentState == BattleState.End;

    private BattleDialogueSet GetCurrentDialogue()
    {
        // BattleIndex는 1부터 시작하므로, 배열 인덱스(0, 1, 2)에 맞추기 위해 1을 빼줍니다.
        // Mathf.Clamp를 써서 실수로 인덱스가 범위를 벗어나도 에러가 나지 않게 방어합니다.
        int index = Mathf.Clamp(StageIndex - 1, 0, stageDialogues.Length - 1);
        return stageDialogues[index];
    }

    public void ChangeState(BattleState newState)
    {
        if (sequenceController != null) return;
        currentState = newState;

        if (IsDialogueState()) inputTimer = 1.0f;
        else inputTimer = 0.2f;

        // 현재 배틀 스테이지(B1, B2, B3)에 맞는 대사 꾸러미를 불러옵니다.
        BattleDialogueSet currentDiag = GetCurrentDialogue();

        switch (currentState)
        {
            case BattleState.Intro:
                player.SetVisible(false);
                // 수정: 현재 스테이지의 시작 대사 사용
                boxController.SetDialogueMode(0.5f, currentDiag.introText);
                break;

            case BattleState.EnemyTurn:
                typewriter.StopAndClear();
                player.SetVisible(true);
                attackManager.StartEnemyTurnSequence();
                break;

            case BattleState.PlayerTurn:
                typewriter.StopAndClear();
                player.SetVisible(false);
                boxController.SetGaugeMode(0.3f);
                gaugeManager.StartGauge();
                break;

            case BattleState.MidDialogue:
                player.SetVisible(false);
                // 수정: 랜덤 대신 현재 스테이지의 중간 대사 사용
                boxController.SetDialogueMode(0.3f, currentDiag.midText);
                break;

            case BattleState.End:
                player.SetVisible(false);
                // 수정: 현재 스테이지의 마무리 대사 사용
                boxController.SetDialogueMode(0.5f, currentDiag.endText);
                break;
        }
    }

    private void AdvanceFromDialogue()
    {
        if (currentState == BattleState.Intro || currentState == BattleState.MidDialogue)
        {
            ChangeState(BattleState.EnemyTurn);
        }
        else if (currentState == BattleState.End)
        {
            // [수정] 마무리 대사가 끝나면 코루틴을 통해 이야기 씬으로 복귀합니다.
            StartCoroutine(ReturnToDecisionScene());
        }
    }

    // [추가] 전투 종료 후 복귀 및 스테이지 카운트 증가 로직
    private IEnumerator ReturnToDecisionScene()
    {
        Debug.Log("전투 승리! 1.5초 후 이야기 씬으로 돌아갑니다.");

        yield return new WaitForSeconds(1.5f);

        SceneManager.LoadScene("DecisionScene");
    }

    public void OnPlayerAttackComplete(float damage)
    {
        enemyHp -= damage;
        if (enemyHp <= 0)
        {
            ChangeState(BattleState.End);
        }
        else
        {
            ChangeState(BattleState.MidDialogue);
        }
    }
}
