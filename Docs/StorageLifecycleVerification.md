# 재생성·중복 저장·실패 재생 검증

2026-10-26, Unity Editor 6000.3.12f1. 사용자 요청에 따라 완료 결과를 유지하고 실패 문구를 다음 회차에서 확인하도록 변경했다. 기존 실행 파일은 재빌드하지 않았다.

## 수정

- 같은 원본 회차·이야기 경로는 대화 종료 시 한 번만 요청한다. 회차 완료, 이후 선택, 다음 회차 진입, 정책 해시 변경으로 개정하거나 재생성하지 않는다.
- 완료/실패 결과가 이미 있는 옛 Pending 항목은 모델 호출 없이 재사용한다. 실제 미완료·취소 작업은 완성 구간 이후부터 이어간다.
- 일반 로그에서 개정 번호를 제거했다. revision 필드는 옛 저장 호환과 작업 식별용으로 남긴다.
- 결과 JSON v4는 표시용 draft/correction 사본을 저장하지 않고 읽을 때 구성한다. canonical 합본 교정은 초안과 달라진 카드만 저장한다. 깨진 원출력은 원문 그대로 보존한다.
- application과 최상위에 중복되던 적용 정보를 한 곳에 저장한다. 큐의 전체 플레이 이력 사본과 Features의 중복 speechChange 필드를 제거했다.
- 실패 문구는 ReviewRequired로 다음 회차에 반영한다. incompleteSections는 구조가 읽히는 카드만 복원하고, 나머지는 원문을 유지한다. 성공으로 표시하지 않는다.
- 기존 완료 결과를 복구하면 실제 사용한 보고서와 연결하고 옛 취소 메시지를 제거한다.

## 실제 저장소

아래 수치와 다음 회차 로더 검증은 사용자가 저장 데이터를 직접 삭제하기 **전**의 기록이다. 이후 사용자가 Unity Editor 테스트 중 직접 초기화했음을 확인했다. 초기화된 현재 저장 상태는 복원하거나 덮어쓰지 않았다.

게임 저장 파일 35개를 먼저 전체 백업했고, 결과 보고서 9개를 v3에서 v4로 변환했다. 원출력·교정 출력·원문 연결 정보는 변환 전후 동일하며, 이전 파일 바이트는 Legacy.zip에도 보관했다. 서로 다른 실제 시도 9개와 Edit 파일은 별도 이력/편집물로 유지했다.

Profile, Runs, Records, Edits 총 21개 파일은 작업 시작 시점과 비교해 바이트 단위로 동일했다. 보고서 합계는 178,312 → 171,190바이트다. 실제 재생성 대기는 0건이고, 정리 과정의 모델 호출은 0회다.

| 다음 회차 게임 로더 확인 | 결과 |
|---|---|
| 로키 도입 | ReviewRequired, 생성된 5/9카드 문구 + 나머지 원문 |
| 표식 대화 | ReviewRequired, 생성된 1/2카드 문구 + 나머지 원문 |
| 작별·정체 질문·반복 배분 | 기존 완료 결과 재사용 |

위 다섯 이야기 모두 실제 2회차용 EncounterContentRepository로 읽었고 저장된 적용 문구와 일치했다. 실제 장면을 수동으로 플레이한 검사는 아니다. 현재 열려 있는 대화 snapshot은 기존대로 유지되므로 새 만남에서 결과를 확인한다.

첫 전체 백업: `tmp/StorageLifecycle/ActualBackup/20261025-224000/`

추가 상태 정리 후 백업: `tmp/StorageLifecycle/ActualBackup/20261025-224247/`

검증 자료: `tmp/StorageLifecycle/audit-before.json`, `actual-result.json`과 위 백업. `audit-after.json`은 사용자 초기화 후 다시 조회된 상태로, 이전 파일과의 차이는 해당 초기화를 포함한다.

## 회귀 검사

| 검사 | 통과 수 |
|---|---:|
| StorageLifecycleCheck.Regression | 20 |
| LocalStoryDialogueQueueCheck.Regression | 51 |
| NarrativeStoreCheck.SectionsRegression | 50 |
| NotebookCleanupCheck.ArchiveRegression | 17 |
| FailedStoryPlaybackCheck.Regression | 41 |
| NarrativeStoreCheck.Regression | 80 |
| PromptCompositionCheck.StrictRuntime | 5 |
| 합계 | 264 |

새 Editor 세션에서 두 스크립트의 압축 라이브러리 참조가 처음에 로드되지 않아 검사가 실행되지 않았으며, 라이브러리 로드 후 재실행하여 통과했다. Unity 프로젝트 C# 컴파일 오류는 없다.

## 남은 품질 작업

이 변경은 저장·재생 동작을 수정한 것이며 모델의 주체 혼동·사건 누락이 해결됐다는 뜻이 아니다. 두 장면·두 시드·예시 유무 8회 비교 및 후속 문법 검증을 마쳤다. 실제 문장 품질은 8회 모두 미달이며, 세부 결과는 [PromptConditionComparison.md](PromptConditionComparison.md)에 기록했다.
