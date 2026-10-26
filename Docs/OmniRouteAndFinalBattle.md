# OmniRoute 생성 경쟁과 최종 3D 전투

## 생성 설정

사용자가 제공한 `OmniRouteFreeStackConverter.cs`는 `Assets/Decision_YYS/Scripts/Ai/OmniRouteFreeStackConverter.cs`에 프로젝트용으로 적용했다. 바탕화면 원본은 변경하지 않았다. 원본의 단독 TMP 화면·중괄호 단어 교체 대신 현재 게임의 공통 프롬프트를 받고 cards JSON을 반환하는 통신 모듈로 바꿨다. 기존 테스트 패널과 실제 생성 큐가 이 파일을 호출하므로 별도 GameObject에 컴포넌트를 붙일 필요는 없다.

`Assets/Decision_YYS/Resources/LocalStory/GenerationSettings.json`에서 연결을 설정한다.

- `mode: FirstCompleted`: NPC에 설정된 내장형 모델과 OmniRoute가 같은 이야기 전체를 동시에 생성한다. 현재 적용 정책으로 사용할 수 있는 이야기를 먼저 완성한 쪽을 채택한다.
- `mode: Embedded`: NPC의 기존 생성 방식만 사용한다.
- `mode: OmniRoute`: OmniRoute만 사용한다.
- `apiUrl`: 기본 `http://127.0.0.1:20128/v1/chat/completions`.
- `combo`: 현재 사용자가 만든 `explain`.
- `timeoutSeconds`: HTTP 요청 한 번의 제한. 현재 120초이며 기존 이야기 전체 제한도 적용된다.

공통 PromptPolicy·PromptModules·Story·Explanation·인물·기억을 그대로 사용한다. Gemma의 메시지 역할 합치기만 기존 어댑터가 수행하고 OmniRoute는 공통 system/user 메시지를 전달한다. 반환 형식은 기존 `{cards:[{index,text,options}]}`다. 문장이나 생성 결과 자체가 두 모델에서 동일해야 한다는 의미는 아니다. 내장형의 GBNF는 HTTP에 전송하지 않으며 공통 프롬프트와 게임의 응답 검사를 사용한다.

## 먼저 완료한 이야기 적용

경쟁 단위는 Story 한 파일 전체다. 서로 다른 모델의 카드나 단락을 섞지 않는다. 각 생성기는 독립된 보고서와 체크포인트를 사용한다. 통신 실패나 적용 불가 결과는 다른 생성기를 기다리며, 먼저 완성된 적용 가능 결과만 기존 다음 회차 저장 경로로 들어간다. 늦은 쪽은 취소하고 결과를 저장하지 않는다. 네이티브 작업의 취소 완료가 늦더라도 승자의 저장을 지연하지 않는다.

`Queue.json`의 `generationWinner`와 NPC `Features.json`의 이야기별 `generationWinner`에서 채택한 생성기를 확인한다. HTTP 공급자/실제 모델은 응답에서 제공하는 범위 안에서 보고서의 `backend`에 기록한다. 네트워크에서 제공하지 않는 추론 시간은 측정한 것처럼 채우지 않는다.

Unity Console과 Player 로그에는 `[내장형]` / `[외부 통신]`을 표시한다. 동시 생성 시작, 각 생성기의 이야기 시작·카드 작성·응답 수신·교정·종료·취소, 최종 채택과 다음 회차 저장을 구분할 수 있다. 외부 응답 수신 이후에는 `OmniRoute / groq / openai/gpt-oss-120b`처럼 실제 공급자·모델도 함께 표시한다. Console 검색창에서 `외부 통신`, `내장형`, `최종 채택`으로 필터링한다. API 키와 요청 헤더는 기록하지 않는다.

기존 `allowFailedOutput` 설정은 유지한다. 현재 값이 true이므로 구조가 보존되는 검사 실패 문구도 기존 정책대로 검토 필요 상태로 반영될 수 있다. 모든 구간이 표시 불가이거나 중간까지만 생성된 이야기는 경쟁에서 채택하지 않는다. 이미 저장된 생성본·사람이 수정한 결과·현재 열려 있는 대화의 스냅샷은 기존 보호 정책을 따른다.

## 서버와 키

최초 한 번 사용자 환경 변수 `OMNIROUTE_API_KEY`를 설정한다. 키를 소스, JSON 설정, 씬, 프리팹에 기록하지 않는다. Windows에서는 이미 실행 중인 Editor도 사용자 환경 변수를 읽는다. 다른 PC에서는 해당 PC에 별도로 설정한다.

사용할 때 PowerShell에서 서버를 실행하고 창을 유지한다.

```powershell
$env:OMNIROUTE_SERVER_HOST="127.0.0.1"
omniroute.cmd
```

대시보드 주소는 `http://localhost:20128`이다. 이번 작업 세션에서는 로컬 서버를 백그라운드로 실행했다. 서버가 중지되면 위 명령으로 다시 실행한다. 콤보 설정과 공급자 연결은 별개다. `explain`에 포함된 공급자 하나 이상을 대시보드의 공급자 메뉴에서 로그인/API 키로 연결하고 테스트해야 한다. Unity→OmniRoute 키와 OmniRoute→공급자 키는 용도가 다르다.

2026-10-26 최초 점검에서는 공급자 연결이 0개라 `503 ALL_TARGETS_SKIPPED`가 반환됐다. 사용자가 Groq 공급자를 연결한 후 실제 `explain` 요청이 `groq / openai/gpt-oss-120b`로 처리되었으며, Farewell 이야기의 cards JSON·내용 검사를 통과했다. 기존 데이터베이스와 콤보는 유지했다.

기존 테스트 패널의 모델 버튼에는 OmniRoute도 포함된다. 이 화면의 단일 모델 생성은 비교 시험이며 자동 경쟁은 실제 플레이의 대화 종료 큐에 적용된다.

## 회차와 전투

진행 상한 또는 마지막 에피소드에 도달하면 현재 회차를 완료하지 않고 `3DBattleScene`의 B1 보스로 이동한다. `Progress.json`에 `finalBattlePending`, `finalBattleStage`가 기록된다. 전투 중 종료했다가 다시 시작하면 해당 전투를 다시 연다.

보스 처치 또는 플레이어 사망 시 기존 5초 전투 종료 대기 후 결과를 저장한다. 현재 회차 완료 → 다음 회차 저장 생성/프로필 변경 → MainMenuScene 순서로 한 번만 실행한다. 보스와 플레이어가 함께 죽으면 사망으로 처리한다. 다음 시작은 다음 회차의 Initial이며 기존 아이템·해금 유지 규칙을 사용한다. 이야기 도중 체력 0으로 사망하는 기존 종료 경로도 유지한다.

3D 씬을 직접 실행한 개발 시험에서는 진행 중인 최종 전투 체크포인트가 없으면 기존 DecisionScene 복귀 동작을 사용한다. 빌드 목록에는 MainMenuScene, DecisionScene, 기존 BattleScene과 3DBattleScene이 포함된다.

## 검사

- Unity C# 컴파일.
- `OmniBattleIntegrationCheck.CheckAll`: 먼저 도착한 결과, 늦은 결과 차단, 실패/잘못된 응답, 취소, 공통 프롬프트, 체크포인트 분리, 승리·사망의 저장/회차 증가를 격리 저장소에서 검사.
- `OmniBattleIntegrationSetup.CheckBattleSignals`: 실제 3D 씬의 보스/플레이어 신호, 종료 대기, 동시 사망, 중복 전환을 프리뷰에서 검사.
- `InitialChapterLimitCheck.Check`: 마지막 이야기에서 회차 증가 없이 전투에 한 번만 진입하는지 검사.
- `OmniBattleIntegrationCheck.RemoteSmoke`: 실제 explain 콤보에 공통 프롬프트 전송. 실제 회차 저장은 변경하지 않는다.
- `OmniBattleIntegrationCheck.RuntimeSmoke`: 실제 내장형/OmniRoute 경쟁에서 채택한 이야기를 격리된 다음 회차 저장소에 저장한 후 다시 읽고 게임 연결을 검사한다.

검사 중간 파일은 `tmp/OmniBattleChecks`에 남긴다.

실제 경쟁 검사에서도 OmniRoute / explain이 먼저 완료되었으며, Groq 응답을 Ready 상태로 격리된 2회차에 저장한 뒤 다시 읽는 데 성공했다. 원문의 게임 행동 연결은 유지됐고 실제 플레이 저장은 변경하지 않았다. 전투 씬 프리뷰의 승리·사망·동시 사망 3가지 신호와 마지막 이야기 경계 4가지도 통과했다.
