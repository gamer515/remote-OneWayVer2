# 이야기 코드 정리와 1단계 테스트

## 이번 정리 범위

프로젝트 자체 이야기/UI/기록 경로를 점검했다. 외부 패키지, 원본 애니메이션, 게임 배치, 사용자가 수정한 폰트/기사 Animator는 정리 대상에서 제외했다. 프로젝트 전체의 모든 코드와 에셋이 미사용이라고 판정한 것은 아니다.

- Story JSON 69개, 카드 125개에서 `background`, `isTransition`, `statWeights` 속성 236개 제거. 모든 JSON 80개를 변경 전과 비교해 세 속성 외 문구/옵션/행동/경로는 보존했다.
- 카드 자료형/변환/생성 결과 검증에서도 세 속성을 제거. 구형 세이브의 알 수 없는 속성은 JsonUtility가 무시하며 기존 대사/선택지는 읽힌다.
- 전환 전용 UI 호출 3단계와 실제 글자만 바꾸던 중복 함수를 일반 대사 표시로 통합.
- 호출/에셋 참조가 없는 `BettingOutcomeCalculator`, `StoryProgressController`(+ `StoryAdvanceResult`), `DecisionInputController` 제거. Unity AssetDatabase로 해당 스크립트와 메타를 함께 삭제했으며 Git 기록에서 복원할 수 있다.
- 이전 `change`, `character`, `eventId`, `ShouldRelay` 제거. 현재 화자는 `speakerId`, 고정 사건은 `eventIdStable`, 다음 회차 대상은 실제 읽은 Encounter 기록으로 결정한다.
- 구형 가중치 베팅의 런타임 목록/Relay 전달/요약과 호출되지 않는 `SendPacket` 제거. `BettingDecisionRecord`/과거 에피소드의 `bettingDecisions`는 구형 세이브 보존용으로만 남겼다. 새 선택과 결과는 `StoryEventRecord`에 기록한다.
- `AIAPIClient` 자체와 Scene의 TempAiM 참조는 아직 남아 있다. 현재 Relay에서 호출하지 않으며 외부 요청을 수행하는 게임 진입 경로는 없다. 내부 모델 단계에서 컴포넌트/호환 테스트까지 교체할 대상으로 분리했다.

씬은 저장하지 않았고, 기존 플레이어 세이브와 원본 애니메이션은 수정하지 않았다.

## 자동 검사

Editor Play를 멈춘 상태에서 프로젝트 루트 터미널에서 실행한다.

```powershell
unity command set_autotick --enable true --format json
unity command recompile --format json
unity command recompile_status --format json
unity command run_script --file AgentScripts/StoryCleanupAudit.cs --entry StoryCleanupAudit.Schema --format json
unity command run_script --file AgentScripts/GuideStageOneCheck.cs --entry GuideStageOneCheck.Regression --format json
unity command run_script --file AgentScripts/GuideStageOneCheck.cs --entry GuideStageOneCheck.TextFits --format json
unity command run_script --file AgentScripts/InitialChapterLimitCheck.cs --entry InitialChapterLimitCheck.Check --format json
```

`recompile_status`가 completed/up_to_date이고 실패가 없는지 먼저 확인한다. Regression은 격리된 임시 저장소에서 검사하며 실제 플레이어 저장 파일을 지우지 않는다. `Schema`는 폐기 속성/함수 제거와 구형 지문 읽기를 검사한다. `TextFits`는 가이드/배분 UI 텍스트 크기를 계산하므로 실제 코인 이동·카메라·입력의 시각 검증을 대체하지 않는다. Initial 검사는 챕터 상한, 다음 회차 준비 중복 방지, 생성 지문의 구조 보호를 메모리에서 검사하며 외부 API를 호출하지 않는다.

이번 실행 결과: 재컴파일 completed/오류 0, 회귀 검사 124개 통과, 스키마 검사 69개 Story/125개 카드 통과, 가이드·배분 텍스트 넘침 없음, Initial 상한/메인 복귀 콜백/생성 결과 구조 보호 검사 통과. 에셋 조사 당시 열린 씬의 Missing Script는 0개였다. 실제 Main부터 마우스로 끝까지 진행하는 수동 Play는 아직 별도 확인이 필요하다.

## 실제 플레이 순서

1. `MainMenuScene`에서 Play → 기존 게임 시작 버튼 → 가이드와 만난다. 현재 회차에서 가이드를 이미 해결했다면 끝까지 진행한 다음 새 회차에서 확인한다. 기존 저장 파일을 삭제할 필요가 없다.
2. 로키의 지식 5개 회수/복원과 알아듣기 어려운 대사를 확인한다. 신 소개의 1/2번은 정상 분기, 4번은 코인 감소→채움→복원(체력 1)과 기존 kneel 재생을 확인한다. 각 분기는 별도 회차에서 비교한다.
3. 배분에서 기어 위=추가, 아래=빼기 → 코인통 버튼으로 1개씩 조절 → 20개를 전부 배분하고 노란 버튼으로 확정. 추가/환불/남은 수량과 통별 최대 20개를 확인한다.
4. 정상 배분 예시는 Health/Speed/Intelligence/Charm 순서로 `5/5/5/5`. 다른 회차에서 `5/5/0/10`(지식 0), `5/5/10/0`(매력 0), `10/10/0/0`(둘 다 0)을 비교한다. 능력치별 전투 성능/보상 수량 조정은 아직 후속 단계다.
5. `0/10/5/5`는 확정 후 사망/메인 복귀, `10/0/5/5`는 재배분이다. 배분 중 임시 0은 사망하지 않는다. 가이드 진행 도중 Play 중단/이어하기로 효과가 중복 적용되지 않는지도 확인한다.
6. 결투·도박의 결과가 설명문이 아닌 NPC 대화로 나오며 선택/결과가 회차 기록에 저장되는지 확인한다. Initial 마지막 에피소드 종료 후 Main으로 돌아가는지도 확인한다.

`Application.persistentDataPath/Saves/Runs/Run_####/Progress.json`에는 선택/관계/결과가, 같은 폴더 `LocalStoryRevisionRequest.json`에는 다음 회차 요청이 저장된다. 현재 요청 상태는 PendingLocalModel이며 아직 실제 LLM 생성은 하지 않는다. 따라서 다음 회차의 새 대사 품질은 아직 시험할 수 없다.

## 에셋 정리 후보 (삭제하지 않음)

2026-10-08 기준, 사용 중인 빌드 씬은 MainMenuScene/DecisionScene/BattleScene이다. 이 씬들의 의존성 + Resources 91개 + 사전 로딩 에셋을 기준으로 비교했다. 런타임 문자열 경로, Addressables, 기타 설정, 편집 원본으로의 사용은 별도 검토가 필요하므로 아래는 안전 삭제 확정 목록이 아니다.

| 후보 | 확인 결과 | 권장 처리 |
| --- | --- | --- |
| `Assets/Scenes/TempDecisionScene.unity`, `TempAttackScene.unity`, `MainMenuTestGameScene.unity` | 빌드 미포함, 다른 씬/프리팹 참조 없음 | 과거 테스트가 더 필요 없다면 보관/정리 후보 |
| V3/V4Baked/JourneyMapKit의 Preview/ApprovedLayout/ProjectLayout 씬 6개 | 위 게임 로딩 루트에서 미참조 | 편집용 미리보기이므로 사용하지 않을 때만 정리 |
| `Assets/Settings/SampleSceneProfile.asset` | 확인한 루트/씬/프리팹에서 미참조 | 다른 프로젝트 설정의 사용도 확인 후 판단 |
| `Assets/Decision_YYS/Scripts/TempDualMiniGame.cs` | 현재 참조 없음, 이벤트 선언만 있는 옛 빈 스크립트 | 테스트를 종료했다면 별도 제거 후보 |
| 옛 AangFace 원본의 `AangFace.glb`, `Preview.html`, `Assets/AangFace/Data/AangFaceMesh.json` | 확인한 게임 로딩 루트에서 미참조, 합계 약 48 MiB | 생성용 원본일 수 있으므로 프로젝트 밖 제작 자료로 보관하는 방안 검토 |
| `Assets/AiAsset/JourneyInterfaceV3/Meshes/GridBoard_Combined.asset` | 게임 로딩 루트에서 미참조, 약 9.8 MiB | V3 편집용 프리팹과의 관계 확인 후 보관 여부 결정 |

### 폴더째 지우면 안 되는 것

- JourneyInterfaceV3: 90개 중 39개가 게임 루트 의존성에 남아 있다. UIOverlay와 공통 메시/재질 등이 사용 중이다.
- JourneyInterfaceV4Baked: 134개 중 96개 사용 중. 현재 조작판/코인 등의 에셋이다.
- JourneyMapKit: 73개 중 45개 사용 중. 현재 기어/코인/UI의 공통 코드와 에셋이 포함된다.
- ChosunCentennial SDF 폰트(약 133.6 MiB), 생성된 AangFaceMesh(약 31.3 MiB), 메인 배경 음악(약 26.6 MiB)은 크지만 사용 중이다. 삭제가 아니라 별도 최적화 대상으로 봐야 한다.
- MiniGame_TestBar와 DecisionMiniGameTestLauncher는 현재 씬에서 활성화된 테스트 기능이다. 이후 LLM 테스트 기능과 구분해서 유지/분리 여부를 결정한다.

에셋 참조 재검사:

```powershell
unity command run_script --file AgentScripts/StoryCleanupAudit.cs --entry StoryCleanupAudit.Assets --timeout_ms 55000 --format json
```

## 다음 작업

우선 실제 플레이에서 위 조작/분기/저장 흐름을 확인한다. 다음 구현 단계는 **내장형 LLM의 최소 생성 테스트**다.

1. 대상 PC RAM/VRAM과 배포 조건을 확인하고 한국어 대화가 가능한 instruction-tuned 모델/양자화/내부 추론 런타임을 선정한다. 학습은 이후 품질 개선 단계다.
2. 원본 Story와 실제 선택/관계/지형/결과 기록에서 한 이야기만 입력으로 만든다. 변경 허용 필드는 대사/허용된 선택지 문구로 제한하고, ID/분기/보상/미니게임 규칙은 게임 코드가 유지한다.
3. 테스트 모드 UI에서 대상 이야기 선택 → 로컬 생성 → 원문/생성문 비교, 소요 시간, JSON 형식/고정 사실/분기 보존 결과를 표시한다. 자연스러움은 직접 읽고 평가하며 임의 점수를 객관적인 품질 점수처럼 제시하지 않는다.
4. 검증에 통과한 결과만 별도 GeneratedContent에 저장/다음 회차 적용한다. 실패하면 원본을 사용한다. 플레이 중 외부 모델 서버/API를 호출하지 않는다.

내부 모델이 연결되기 전에는 대규모 모델 다운로드나 파인튜닝을 이 정리 작업에서 시작하지 않는다. Excel 관리/내보내기와 능력치에 따른 전투·보상 계산도 별도 후속 단계로 유지한다.
