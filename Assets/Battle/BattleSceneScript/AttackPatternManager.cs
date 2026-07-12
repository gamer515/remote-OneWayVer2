using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AttackPatternManager : MonoBehaviour
{
    public BattleStateMachine stateMachine;
    public BattleSceneBattleBoxController boxController;
    public PlayerController player;

    [Header("Enemy Reference")]
    public GameObject enemyObject;

    [Header("Bullet Prefabs")]
    public GameObject bulletA_Prefab;
    public GameObject bulletA_Straight_Prefab;
    public GameObject bulletB_Prefab;
    public GameObject bulletC_Prefab;
    public GameObject bulletC_Static_Prefab;
    public void StartEnemyTurnSequence()
    {
        StartCoroutine(RunThreePatternsSequence());
    }

    private IEnumerator RunThreePatternsSequence()
    {
        List<int> patternPool = new List<int>();
        int currentBattle = BattleStateMachine.BattleIndex;

        if (currentBattle == 1) patternPool.AddRange(new int[] { 1, 2, 3 }); // Bullet A 위주
        else if (currentBattle == 2) patternPool.AddRange(new int[] { 4, 5, 6 }); // Bullet C 위주
        else patternPool.AddRange(new int[] { 7, 8, 9 }); // Bullet B 위주

        for (int i = 0; i < 3; i++)
        {
            int randIndex = Random.Range(0, patternPool.Count);
            int selectedPattern = patternPool[randIndex];
            patternPool.RemoveAt(randIndex);

            // 수정: 첫 번째 패턴일 때 강제로 (0, -4) 중앙으로 옮기는 로직
            // 단, 4번 패턴(가시)처럼 알아서 플레이어 위치를 옮기는 기믹이 있는 패턴은 제외합니다.
            // (만약 5, 6번도 중앙 이동이 필요 없다면 selectedPattern != 4 && selectedPattern != 5 ... 식으로 추가하면 됩니다.)
            if (i == 0 && selectedPattern != 4)
            {
                player.transform.position = new Vector2(0f, -4f);
                Rigidbody2D pRb = player.GetComponent<Rigidbody2D>();
                if (pRb != null) pRb.linearVelocity = Vector2.zero;
            }

            yield return StartCoroutine(ExecutePattern(selectedPattern));

            // 패턴과 패턴 사이의 짧은 휴식 시간
            yield return new WaitForSeconds(1f);
        }

        // 3번의 패턴이 끝나면 내 공격 턴으로 전환
        stateMachine.ChangeState(BattleStateMachine.BattleState.PlayerTurn);
    }

    private IEnumerator ExecutePattern(int patternID)
    {
        Debug.Log($"[Pattern System] 패턴 {patternID}번 작동 시작!");

        switch (patternID)
        {
            case 1: // 4방향 동시 발사 (새로운 중심점 기준)
                Vector2 boxCenter1 = new Vector2(0f, -3f); // 현재 박스의 중심
                boxController.ChangeBox(new Vector2(8f, 8f), boxCenter1, 0.5f);
                player.SetMovementMode(PlayerController.MovementMode.Free);
                yield return new WaitForSeconds(0.5f);

                float offset = 1.2f;

                // 1. 왼쪽에서 생성 (Y좌표를 boxCenter 기준 보정)
                for (int i = 0; i < 6; i++)
                {
                    Vector2 pos = new Vector2(boxController.leftWall.position.x - 1f, boxCenter1.y + 3f - (offset * i));
                    Instantiate(bulletA_Prefab, pos, Quaternion.identity);
                }
                yield return new WaitForSeconds(2f);

                // 2. 아래에서 생성 (X좌표를 boxCenter 기준 보정)
                for (int i = 0; i < 6; i++)
                {
                    Vector2 pos = new Vector2(boxCenter1.x - 3f + (offset * i), boxController.bottomWall.position.y - 1f);
                    Instantiate(bulletA_Prefab, pos, Quaternion.identity);
                }
                yield return new WaitForSeconds(2f);

                // 3. 오른쪽에서 생성
                for (int i = 0; i < 6; i++)
                {
                    Vector2 pos = new Vector2(boxController.rightWall.position.x + 1f, boxCenter1.y + 3f - (offset * i));
                    Instantiate(bulletA_Prefab, pos, Quaternion.identity);
                }
                yield return new WaitForSeconds(2f);

                // 4. 위에서 생성
                for (int i = 0; i < 6; i++)
                {
                    Vector2 pos = new Vector2(boxCenter1.x - 3f + (offset * i), boxController.topWall.position.y + 1f);
                    Instantiate(bulletA_Prefab, pos, Quaternion.identity);
                }
                yield return new WaitForSeconds(2f);
                break;

            case 2: // 왼쪽에서 오른쪽으로 오는 지그재그 웨이브 (중간 이빨 빠짐)
                // 1. 상자 크기 조절 (높이 8 기준)
                boxController.ChangeBox(new Vector2(8f, 8f), new Vector2(0f, -3f), 0.5f);
                yield return new WaitForSeconds(0.5f);

                int waveCount = 4; // 총 2번 왕복 (약 6초 소요)
                float spawnInterval = 0.12f;
                float startX = -15f; // 왼쪽 고정 X 좌표

                // 2. 1번(Top)부터 6번(Bottom)까지의 Y좌표 6개 미리 계산
                // 상자 중심이 (0, -4)이고 높이가 8이므로, 내부 Y좌표는 대략 -0.5 ~ -7.5
                float[] yPos = new float[6];
                float topY = 0.5f;
                float botY = -6.5f;
                for (int i = 0; i < 6; i++)
                {
                    // i가 0이면 1번(Top), i가 5면 6번(Bottom)
                    yPos[i] = Mathf.Lerp(topY, botY, i / 5f);
                }

                // 3. 지그재그 패턴 시작
                for (int w = 0; w < waveCount; w++)
                {
                    // [1] 1번(Top) 단일 생성
                    SpawnWaveBullet(startX, yPos[0]);
                    yield return new WaitForSeconds(spawnInterval);

                    // [2] 2~5번 내려가면서 생성 (이 중 1개는 랜덤으로 생성 생략)
                    int skipDown = Random.Range(1, 5); // 1, 2, 3, 4 중 하나 랜덤 뽑기
                    for (int i = 1; i <= 4; i++)
                    {
                        if (i != skipDown) SpawnWaveBullet(startX, yPos[i]);
                        yield return new WaitForSeconds(spawnInterval); // 쏘든 안 쏘든 0.3초 대기는 동일
                    }

                    // [3] 6번(Bottom) 단일 생성
                    SpawnWaveBullet(startX, yPos[5]);
                    yield return new WaitForSeconds(spawnInterval);

                    // [4] 5~2번 올라가면서 생성 (이 중 1개는 랜덤으로 생성 생략)
                    int skipUp = Random.Range(1, 5);
                    for (int i = 4; i >= 1; i--)
                    {
                        if (i != skipUp) SpawnWaveBullet(startX, yPos[i]);
                        yield return new WaitForSeconds(spawnInterval);
                    }
                }

                // 패턴 종료 후 총알이 화면 밖으로 다 나갈 때까지 대기
                yield return new WaitForSeconds(2.5f);
                break;

            case 3: // 위에서 아래로 내려오는 벽 (2칸 빈 공간이 좌우로 이동)
                // 시작 시 적 숨기기
                if (enemyObject != null) enemyObject.SetActive(false);

                boxController.ChangeBox(new Vector2(8f, 8f), new Vector2(0f, -3f), 0.5f);
                yield return new WaitForSeconds(0.5f);

                int rowCount = 12; // 총 12번(줄) 쏘기
                float spawnDelay = 0.2f; // 0.3초 간격
                float startY = 8f; // 위쪽 고정 Y 좌표

                // 1. 1번(Left)부터 6번(Right)까지의 X좌표 6개 미리 계산
                // 상자 중심이 0이고 너비가 8이므로, 내부 X좌표는 여유 있게 -3.5 ~ 3.5로 잡습니다.
                float[] xPos = new float[6];
                float leftX = -3.5f;
                float rightX = 3.5f;
                for (int i = 0; i < 6; i++)
                {
                    xPos[i] = Mathf.Lerp(leftX, rightX, i / 5f);
                }

                // 2. 빠지는 번호(구멍)의 시작 인덱스 배열 (0부터 시작)
                // 1은 (2,3번 빠짐), 2는 (3,4번 빠짐), 3은 (4,5번 빠짐)을 의미합니다.
                int[] holeSequence = { 1, 2, 3, 2 };

                // 3. 패턴 시작
                for (int r = 0; r < rowCount; r++)
                {
                    // 이번 줄에서 구멍이 시작될 위치를 배열에서 순서대로 가져옵니다.
                    int currentHoleStart = holeSequence[r % holeSequence.Length];

                    for (int i = 0; i < 6; i++)
                    {
                        // 현재 자리가 구멍 시작점이거나 그 다음 점(총 2칸)이면 건너뜁니다!
                        if (i == currentHoleStart || i == currentHoleStart + 1)
                            continue;

                        // 빈 공간이 아니면 총알 생성
                        SpawnVerticalBullet(xPos[i], startY);
                    }

                    // 한 줄을 다 만들고 0.3초 대기
                    yield return new WaitForSeconds(spawnDelay);
                }

                // 패턴 종료 후 총알이 화면 밖으로 다 나갈 때까지 대기
                yield return new WaitForSeconds(2.5f);

                // 패턴 종료 후 적 다시 나타나기
                if (enemyObject != null) enemyObject.SetActive(true);
              
                break;

            case 4: // 가로로 긴 상자 & 부드러운 일직선 이동 & 8개 묶음 가시
                // 1. 상자 크기 조절 시작
                    boxController.ChangeBox(new Vector2(16f, 4f), new Vector2(0, -3f), 0.5f);
                    yield return new WaitForSeconds(0.5f); // 상자가 다 변할 때까지 대기

                    // 2. 플레이어 강제 이동 시작 (조작 잠금)
                    player.isControlLocked = true; // 컨트롤 끄기

                    Rigidbody2D pRb = player.GetComponent<Rigidbody2D>();
                    pRb.linearVelocity = Vector2.zero; // 가던 힘 없애기
                    pRb.gravityScale = 0f; // 이동 중에 바닥으로 떨어지지 않게 중력 잠깐 무시

                    Vector2 startPos = player.transform.position; // 현재 위치
                    Vector2 targetPos = new Vector2(boxController.leftWall.position.x + 1.5f, boxController.bottomWall.position.y + 0.8f); // 목표 위치 (왼쪽 아래)

                    float moveDuration = 0.4f; // 0.4초 동안 슉! 하고 이동
                    float elapsed = 0f;

                    // 목표 위치로 부드럽게 당기기
                    while (elapsed < moveDuration)
                    {
                        elapsed += Time.deltaTime;
                        // Vector2.Lerp로 시작점과 끝점을 시간에 따라 부드럽게 이어줍니다.
                        player.transform.position = Vector2.Lerp(startPos, targetPos, elapsed / moveDuration);
                        yield return null; // 다음 프레임까지 대기
                    }

                    player.transform.position = targetPos; // 오차 없이 최종 위치에 딱 맞춤

                    // 3. 이동 완료! 다시 중력 모드 켜고 조작 잠금 해제
                    player.SetMovementMode(PlayerController.MovementMode.Gravity);
                    player.isControlLocked = false;

                    yield return new WaitForSeconds(0.2f); // 가시 나오기 전 잠깐의 눈치 게임 시간

                    // 4. 가시 순차적 출현 (업데이트됨)
                    int spikeCount = 8;

                    // 시작점: 오른쪽 벽 위치로 고정
                    float case4StartX = boxController.rightWall.position.x;
                    float case4StartY = boxController.bottomWall.position.y + 0.5f;

                    GameObject lastSpike = null;

                    for (int i = 0; i < spikeCount; i++)
                    {
                        // X좌표 이동 계산 삭제: 항상 같은 자리(startX)에서 생성됩니다.
                        Vector2 spawnPos = new Vector2(case4StartX, case4StartY);

                        // 가시 생성 및 마지막 가시 갱신
                        lastSpike = Instantiate(bulletC_Prefab, spawnPos, Quaternion.identity);

                        // 0.05초 간격으로 빠르게 연속 생성
                        yield return new WaitForSeconds(0.05f);
                    }

                    // 8개가 모두 생성된 이후, 
                    // 마지막 가시(lastSpike)가 왼쪽 벽에 닿아 사라질 때까지 무한 대기
                    while (lastSpike != null)
                    {
                        yield return null;
                    }

                    break;

            case 5: // 끝없이 떨어지는 함정 (양쪽 벽 가시 상승 -> 지정 범위 바닥 가시 배치)
                {
                    //시작 시 적 숨기기
                    if (enemyObject != null) enemyObject.SetActive(false);

                    // 1. [수정] 상자 크기 조절 (너비 8, 높이 10, 중심 0, -2)
                    boxController.ChangeBox(new Vector2(9f, 12f), new Vector2(0f, -1f), 0.5f);
                    yield return new WaitForSeconds(0.5f);

                    // 2. 플레이어를 위쪽 허공으로 순간 이동 (자유 조작 및 무중력)
                    player.transform.position = new Vector2(0f, 1f);
                    player.isControlLocked = false;
                    player.SetMovementMode(PlayerController.MovementMode.Free);

                    Rigidbody2D case5Rb = player.GetComponent<Rigidbody2D>();
                    if (case5Rb != null)
                    {
                        case5Rb.gravityScale = 0f;
                        case5Rb.linearVelocity = Vector2.zero;
                    }

                    // 3. 3초 동안 양쪽 벽에서 가시가 생성되어 위로 올라갑니다.
                    float fallDuration = 3.0f;
                    float wallSpawnDelay = 0.1f;
                    float elapsedFall = 0f;
                    Vector2 upVelocity = new Vector2(0f, 8f);

                    while (elapsedFall < fallDuration)
                    {
                        // 왼쪽 벽 가시
                        Vector2 case5LeftPos = new Vector2(boxController.leftWall.position.x + 0.5f, boxController.bottomWall.position.y - 0f);
                        SpawnMovingSpike(case5LeftPos, -90f, upVelocity);

                        // 오른쪽 벽 가시
                        Vector2 case5RightPos = new Vector2(boxController.rightWall.position.x - 0.5f, boxController.bottomWall.position.y - 0f);
                        SpawnMovingSpike(case5RightPos, 90f, upVelocity);

                        elapsedFall += wallSpawnDelay;
                        yield return new WaitForSeconds(wallSpawnDelay);
                    }

                    // 4. 3초 종료! 벽 가시들을 멈추고 4초 뒤 파괴 예약
                    foreach (GameObject spike in activeWallSpikes)
                    {
                        if (spike != null)
                        {
                            Rigidbody2D spikeRb = spike.GetComponent<Rigidbody2D>();
                            if (spikeRb != null)
                            {
                                spikeRb.linearVelocity = Vector2.zero;
                            }
                            Destroy(spike, 2.0f);
                        }
                    }
                    activeWallSpikes.Clear();

                    // 5. 중력 모드를 다시 켜서 바닥으로 추락시킵니다.
                    if (case5Rb != null)
                    {
                        case5Rb.gravityScale = 1.5f;
                    }
                    player.SetMovementMode(PlayerController.MovementMode.Gravity);
                    player.isControlLocked = false;

                    // 6. [요청 사항 반영] 제한된 6f 구역 내에 가시 4개와 빈 공간 1개 배치
                    int totalSlots = 4; // 가시 4개 + 빈 공간 1개 = 총 5칸
                    int safeHoleIndex = Random.Range(0, totalSlots); // 0~4 중 랜덤으로 안전지대 설정

                    float case5StartX = -3f; // 양끝 1f를 제외한 내부 6f 구역의 시작 X좌표 (-4f + 1f)
                    float spacing = 2f; // 6f 구역을 5개 칸으로 나누는 정밀 간격 (6f / 4)

                    for (int i = 0; i < totalSlots; i++)
                    {
                        // 랜덤 선택된 인덱스는 빈 공간이므로 가시를 생성하지 않고 패스!
                        if (i == safeHoleIndex) continue;

                        // 바닥에 고정된 가시 생성
                        Vector2 bottomPos = new Vector2(case5StartX + (i * spacing), boxController.bottomWall.position.y + 0.5f);
                        GameObject staticSpike = Instantiate(bulletC_Static_Prefab, bottomPos, Quaternion.identity);

                        // 4초 뒤 자동 파괴
                        Destroy(staticSpike, 2.0f);
                    }

                    // 다음 패턴 전환 대기 시간
                    yield return new WaitForSeconds(2.2f);

                    //  패턴 종료 후 적 다시 나타나기
                    if (enemyObject != null) enemyObject.SetActive(true);

                    break;
                }

            case 6: // 중력 조작 (연속 벽쾅 패턴)
                {
                    // 1. 상자 크기 조절 (너비 10, 높이 8)
                    boxController.ChangeBox(new Vector2(10f, 8f), new Vector2(0f, -3f), 0.5f);
                    yield return new WaitForSeconds(0.5f);

                    //[요청 반영] 패턴 시작 시 자유 조작 및 무중력(Free) 모드로 변환
                    player.isControlLocked = false;
                    player.SetMovementMode(PlayerController.MovementMode.Free);

                    Rigidbody2D case6Rb = player.GetComponent<Rigidbody2D>();
                    if (case6Rb != null)
                    {
                        case6Rb.gravityScale = 0f; // 중력 끄기
                        case6Rb.linearVelocity = Vector2.zero; // 기존 속도 초기화
                    }

                    int dashCount = 3;           // 총 3번 던지기
                    float centerDashTime = 0.2f; // 첫 중앙 이동 시간
                    float wallDashTime = 0.15f;  // 벽으로 밀쳐지는 시간 (매우 빠름)

                    for (int i = 0; i < dashCount; i++)
                    {
                        // [요청 반영] 오직 '첫 번째(i == 0)' 루프일 때만 플레이어를 중앙으로 이동시킵니다.
                        // 두 번째, 세 번째 루프에서는 이 단계를 건너뛰고 바로 다음 벽으로 이동합니다.
                        if (i == 0)
                        {
                            player.isControlLocked = true; // 이동 중 조작 잠금
                            yield return StartCoroutine(MovePlayerTo(new Vector2(0f, -4f), centerDashTime));
                            yield return new WaitForSeconds(0.1f); // 튕기기 전 짧은 정적
                        }

                        // 2. 랜덤 벽 선택 (0: Top, 1: Bottom, 2: Left, 3: Right)
                        int wallIndex = Random.Range(0, 4);
                        Vector2 case6TargetPos = Vector2.zero;
                        float case6Offset = 0.8f; // 벽을 뚫지 않기 위한 여백

                        if (wallIndex == 0) case6TargetPos = new Vector2(player.transform.position.x, boxController.topWall.position.y - case6Offset);
                        else if (wallIndex == 1) case6TargetPos = new Vector2(player.transform.position.x, boxController.bottomWall.position.y + case6Offset);
                        else if (wallIndex == 2) case6TargetPos = new Vector2(boxController.leftWall.position.x + case6Offset, player.transform.position.y);
                        else if (wallIndex == 3) case6TargetPos = new Vector2(boxController.rightWall.position.x - case6Offset, player.transform.position.y);

                        // 3. 쾅! 현재 위치에서 해당 벽 방향으로 곧바로 내동댕이
                        player.isControlLocked = true; // 밀려나는 동안 조작 잠금
                        yield return StartCoroutine(MovePlayerTo(case6TargetPos, wallDashTime));

                        // 4. 벽에 닿자마자 즉시 조작을 돌려줌 (0.2초 동안 탈출해야 함!)
                        player.isControlLocked = false;

                        // 5. 약속된 0.2초의 반응(도망) 시간
                        yield return new WaitForSeconds(0.5f);

                        // 6. 가만히 있는 가시(Static)들이 해당 벽에서 일제히 돌출
                        List<GameObject> wallSpikes = new List<GameObject>();
                        int spikeAmount = 7; // 벽 한 면에 생성될 가시 개수

                        if (wallIndex == 0) // 위쪽 벽 (아래를 향해 튐)
                        {
                            float case6StartX = boxController.leftWall.position.x + 0.5f;
                            float spacing = (boxController.rightWall.position.x - boxController.leftWall.position.x - 1f) / (spikeAmount - 1);
                            for (int j = 0; j < spikeAmount; j++)
                            {
                                Vector2 pos = new Vector2(case6StartX + (j * spacing), boxController.topWall.position.y - 0.5f);
                                wallSpikes.Add(Instantiate(bulletC_Static_Prefab, pos, Quaternion.Euler(0, 0, 180f)));
                            }
                        }
                        else if (wallIndex == 1) // 아래쪽 벽 (위를 향해 튐)
                        {
                            float case6StartX = boxController.leftWall.position.x + 0.5f;
                            float spacing = (boxController.rightWall.position.x - boxController.leftWall.position.x - 1f) / (spikeAmount - 1);
                            for (int j = 0; j < spikeAmount; j++)
                            {
                                Vector2 pos = new Vector2(case6StartX + (j * spacing), boxController.bottomWall.position.y + 0.5f);
                                wallSpikes.Add(Instantiate(bulletC_Static_Prefab, pos, Quaternion.identity));
                            }
                        }
                        else if (wallIndex == 2) // 왼쪽 벽 (오른쪽을 향해 튐)
                        {
                            float case6StartY = boxController.bottomWall.position.y + 0.5f;
                            float spacing = (boxController.topWall.position.y - boxController.bottomWall.position.y - 1f) / (spikeAmount - 1);
                            for (int j = 0; j < spikeAmount; j++)
                            {
                                Vector2 pos = new Vector2(boxController.leftWall.position.x + 0.5f, case6StartY + (j * spacing));
                                wallSpikes.Add(Instantiate(bulletC_Static_Prefab, pos, Quaternion.Euler(0, 0, -90f)));
                            }
                        }
                        else if (wallIndex == 3) // 오른쪽 벽 (왼쪽을 향해 튐)
                        {
                            float case6StartY = boxController.bottomWall.position.y + 0.5f;
                            float spacing = (boxController.topWall.position.y - boxController.bottomWall.position.y - 1f) / (spikeAmount - 1);
                            for (int j = 0; j < spikeAmount; j++)
                            {
                                Vector2 pos = new Vector2(boxController.rightWall.position.x - 0.5f, case6StartY + (j * spacing));
                                wallSpikes.Add(Instantiate(bulletC_Static_Prefab, pos, Quaternion.Euler(0, 0, 90f)));
                            }
                        }

                        // 7. 가시가 유지되는 시간 (도망쳐서 버텨야 하는 시간)
                        yield return new WaitForSeconds(0.6f);

                        // 8. 깔끔하게 가시 회수(삭제)
                        foreach (GameObject spike in wallSpikes)
                        {
                            if (spike != null) Destroy(spike);
                        }

                        // 다음 벽으로 쳐내기 전의 아주 짧은 휴식 템포
                        yield return new WaitForSeconds(0.2f);
                    }

                    // 모든 연속 벽쾅이 끝나고 다음 패턴으로 넘어가기 전 안전 대기 시간
                    yield return new WaitForSeconds(1.5f);
                    break;
                }

            case 7: // 반시계 레이저 (새로운 중심점 조준)
                Vector2 boxCenter2 = new Vector2(0f, -3f);
                boxController.ChangeBox(new Vector2(8f, 8f), boxCenter2, 0.5f);
                player.SetMovementMode(PlayerController.MovementMode.Free);
                yield return new WaitForSeconds(0.5f);

                float angle = 60f;
                for (int i = 0; i < 24; i++)
                {
                    // 1. Vector2.zero 대신 boxCenter2를 기준으로 원형 좌표 계산
                    Vector2 spawnPos = GetPosOnCircle(boxCenter2, 6f, angle);

                    // 2. Vector2.zero 대신 boxCenter2를 바라보도록 방향 벡터 계산
                    Vector2 dirToCenter = boxCenter2 - spawnPos;

                    float rotZ = Mathf.Atan2(dirToCenter.y, dirToCenter.x) * Mathf.Rad2Deg;
                    Instantiate(bulletB_Prefab, spawnPos, Quaternion.Euler(0, 0, rotZ));

                    angle += 15f;
                    yield return new WaitForSeconds(0.1f);
                }
                yield return new WaitForSeconds(3f);
                break;
            case 8: // 양방향 동시 레이저 (새로운 중심점 기준)
                Vector2 boxCenter3 = new Vector2(0f, -3f);
                boxController.ChangeBox(new Vector2(8f, 8f), boxCenter3, 0.5f);
                player.SetMovementMode(PlayerController.MovementMode.Free);
                yield return new WaitForSeconds(0.5f);

                // 중심점에서 좌측 상단/우측 상단으로 오프셋을 더해 위치를 잡습니다.
                Vector2 leftPos = boxCenter3 + new Vector2(-5f, 5f);
                Vector2 rightPos = boxCenter3 + new Vector2(5f, 5f);

                Instantiate(bulletB_Prefab, leftPos, Quaternion.Euler(0, 0, -45f));
                Instantiate(bulletB_Prefab, rightPos, Quaternion.Euler(0, 0, -135f));

                yield return new WaitForSeconds(2f);
                break;
            case 9: // 가로 레이저(BulletB) 4연사 패턴 (자유 모드)
                {
                    // 1. 상자 크기 조절 (기본 크기: 너비 8, 높이 8)
                    boxController.ChangeBox(new Vector2(8f, 8f), new Vector2(0f, -3f), 0.5f);
                    yield return new WaitForSeconds(0.5f);

                    // 2. 플레이어를 자유 조작(Free) 및 무중력 상태로 변환
                    player.isControlLocked = false;
                    player.SetMovementMode(PlayerController.MovementMode.Free);

                    Rigidbody2D case9Rb = player.GetComponent<Rigidbody2D>();
                    if (case9Rb != null)
                    {
                        case9Rb.gravityScale = 0f; // 중력 끄기
                        case9Rb.linearVelocity = Vector2.zero; // 기존에 받던 힘 초기화
                    }

                    // 3. 패턴 설정값
                    int repeatCount = 4; // 총 4번 생성
                    float case9SpawnDelay = 0.6f; // 생성 간격 0.6초
                    float spawnX = -17f; // X좌표는 왼쪽 끝으로 고정

                    // 4개의 지정된 Y좌표 배열
                    float[] possibleYPos = { -2f, -3f, -4f, -5f, -6f, -7f };

                    // 4. 레이저 연속 생성
                    for (int i = 0; i < repeatCount; i++)
                    {
                        // 지정된 4개의 Y좌표 중 하나를 랜덤으로 뽑습니다.
                        int randomIndex = Random.Range(0, possibleYPos.Length);
                        float spawnY = possibleYPos[randomIndex];

                        Vector2 spawnPos = new Vector2(spawnX, spawnY);

                        // BulletB 생성 (오른쪽을 바라보도록 회전값 0도인 Quaternion.identity 사용)
                        // 만약 레이저가 위를 보고 있다면 Quaternion.Euler(0, 0, -90f) 로 수정해주세요!
                        Instantiate(bulletB_Prefab, spawnPos, Quaternion.identity);

                        // 다음 생성까지 0.6초 대기
                        yield return new WaitForSeconds(case9SpawnDelay);
                    }

                    // 패턴 종료 후 모든 레이저가 끝날 때까지 넉넉히 대기
                    yield return new WaitForSeconds(2.0f);
                    break;
                }
        }
    }

    // 원형 좌표 도우미 함수 유지
    private Vector2 GetPosOnCircle(Vector2 center, float radius, float angleInDegrees)
    {
        float ptX = center.x + radius * Mathf.Cos(angleInDegrees * Mathf.Deg2Rad);
        float ptY = center.y + radius * Mathf.Sin(angleInDegrees * Mathf.Deg2Rad);
        return new Vector2(ptX, ptY);
    }


    // 2번 패턴 전용 총알 생성 함수
    private void SpawnWaveBullet(float x, float y)
    {
        // 지정된 위치에 총알 생성
        GameObject bullet = Instantiate(bulletA_Straight_Prefab, new Vector2(x, y), Quaternion.identity);

        // 중요: 만약 bulletA_Prefab 자체에 플레이어를 따라가는(Homing) 스크립트가 켜져있다면, 
        // 여기서 그 스크립트를 꺼주는 코드가 필요할 수 있습니다. 
        // 예: bullet.GetComponent<HomingScript>().enabled = false;

        // 왼쪽에서 오른쪽으로만 직진하도록 속도 강제 부여
        Rigidbody2D rb = bullet.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(8f, 0f); // 8의 속도로 오른쪽 이동 (테스트 후 입맛에 맞게 조절하세요)
        }
    }

    // 3번 패턴 전용
    private void SpawnVerticalBullet(float x, float y)
    {
        GameObject bullet = Instantiate(bulletA_Straight_Prefab, new Vector2(x, y), Quaternion.identity);

        // 왼쪽->오른쪽 때와 마찬가지로 Homing 스크립트가 있다면 비활성화 해주세요.
        // 예: bullet.GetComponent<HomingScript>().enabled = false;

        Rigidbody2D rb = bullet.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            // X축 이동은 없고, Y축으로 -8f의 속도로 떨어지게 합니다.
            rb.linearVelocity = new Vector2(0f, -8f);
        }
    }


    // 5번 패턴 전용
    //  [수정] 현재 화면에 생성되어 날아가고 있는 벽 가시들을 관리하는 리스트
    private List<GameObject> activeWallSpikes = new List<GameObject>();

    private void SpawnMovingSpike(Vector2 pos, float rotZ, Vector2 velocity)
    {
        GameObject spike = Instantiate(bulletC_Static_Prefab, pos, Quaternion.Euler(0, 0, rotZ));

        Rigidbody2D rb = spike.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = velocity;
        }

        //  생성된 가시를 관리 리스트에 추가합니다.
        activeWallSpikes.Add(spike);

        StartCoroutine(DestroySpikeAtTopWall(spike));
    }
    //5번 패턴 전용
    private IEnumerator DestroySpikeAtTopWall(GameObject spike)
    {
        while (spike != null)
        {
            if (spike.transform.position.y >= boxController.topWall.position.y)
            {
                //  파괴되기 전에 리스트에서 안전하게 제거합니다.
                if (activeWallSpikes.Contains(spike))
                {
                    activeWallSpikes.Remove(spike);
                }

                Destroy(spike);
                yield break;
            }

            yield return null;
        }
    }


    // 6번 패턴 전용: 지정된 목표 위치까지 부드럽게 끌어당기는 함수
    private IEnumerator MovePlayerTo(Vector2 targetPosition, float duration)
    {
        Vector2 startPosition = player.transform.position;
        float elapsed = 0f;

        // 끌려가는 동안에는 중력이나 다른 조작 힘이 방해하지 않도록 속도를 0으로 묶습니다.
        Rigidbody2D pRb = player.GetComponent<Rigidbody2D>();
        if (pRb != null)
        {
            pRb.linearVelocity = Vector2.zero;
        }

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            // Vector2.Lerp를 이용해 시작점과 끝점 사이를 연속적으로 계산합니다.
            player.transform.position = Vector2.Lerp(startPosition, targetPosition, elapsed / duration);
            yield return null;
        }

        // 마지막엔 목표 위치에 오차 없이 정확히 맞춥니다.
        player.transform.position = targetPosition;
    }
}