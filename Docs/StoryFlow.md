# Story 지문과 분기 수정

## 임시 챕터 진행 상한

`Omnibus_01.json`의 `"lastPlayableChapterId": "Initial"`로 현재 플레이를 Initial까지 제한한다. chapters 목록 및 Chapter_1/Chapter_2의 모든 JSON/에셋은 그대로 보존한다. Initial_01은 groundIds 한 개와 chunkCount 1로 한 청크만 사용하며 NPC 3명과 Story 연결은 유지한다. Initial의 마지막 에피소드 완료 후 다음 챕터를 로딩하지 않고 현재 회차 완료 저장 → 다음 회차 준비 → MainMenuScene 이동으로 종료한다. 중복 종료 호출은 새 회차를 추가 생성하지 않는다.

완료된 이전 회차 기록은 보존하고 다음 회차는 Initial부터 시작한다. 이후 전체 진행을 다시 허용하려면 lastPlayableChapterId를 빈 문자열로 바꾸거나 필드를 제거한다. 특정 챕터까지 열려면 해당 chapterId를 지정한다. 제한 이후 콘텐츠는 초기 검증 및 지형 등록 대상에서도 제외된다.

Initial도 다음 회차 이야기 변경 대상이다. 챕터 완료 시 실제로 읽은 Encounter 카드만 로컬 요청에 포함하며, 회차 종료 시 실제 선택/관계/결과를 담은 LocalStoryRevisionRequest를 저장한다. 현재 내부 모델은 미연결이므로 외부 API를 호출하지 않고 원본 이야기로 진행한다. 향후 GeneratedContent/For_Run_XXXX/Encounters 결과를 적용할 때 카드 순서·선택 결과·분기·미니게임 규칙 및 { } 핵심 문자열은 검증으로 보호한다.

Unity 컴파일 오류 0개. `AgentScripts/InitialChapterLimitCheck.cs`의 진행 상한과 완료 순서/중복 방지, Initial 요청 생성, 응답 검증, 생성 지문 적용, 생성 대기 후 입력 잠금 해제 검사가 통과했다. 실제 저장·씬 이동·API 호출 없이 메모리에서 검사했다. Main부터 Initial 전체를 끝내는 수동 Play와 실제 외부 API 결과 확인은 별도 항목이다.

Scene의 TempAiM/AIAPIClient는 이전 외부 모델 구현의 잔존 컴포넌트이며 현재 StoryRelay에서는 호출하지 않는다. API 키를 설정하는 방식으로 테스트하지 않는다. 내부 모델 단계에서 이를 교체하며, 이번 검사에서는 씬을 저장하거나 API 키를 출력하지 않았다.

본 게임의 NPC/장소 지문은 모두 `Assets/Decision_YYS/Resources/Story_Json_Data/` 아래의 `Story.json`으로 진행합니다. 도착하면 첫 카드를 바로 표시하며, 별도 Interaction 시작 나레이션은 없습니다. 기존 일반 대사와 Story 선택지는 유지했습니다.

## 지금 수정할 파일

- 대련 NPC 대화/선택지: `Initial/Initial_01/Encounters/duelist/Story.json`
- 대련 시작 안내: `Initial/Initial_01/Encounters/duelist/Duel/Start/Story.json`
- 대련 결과: 같은 `Duel` 아래 `Win`, `Lose`, `Error` 폴더의 `Story.json`
- 도박 NPC 대화/선택지: `Initial/Initial_01/Encounters/gambler/Story.json`
- 앞/뒤 선택 안내: 같은 NPC의 `Coin/Heads`, `Coin/Tails` 폴더의 `Story.json`
- 도박 결과: `Coin/HeadsWin`, `HeadsLose`, `TailsWin`, `TailsLose`, `Error` 폴더의 `Story.json`

화면 지문은 각 카드의 `text`를 수정합니다. Inspector 항목은 추가하지 않았습니다. DecisionScene의 독립 테스트 버튼 UI는 본 게임 이야기와 별개로 유지됩니다.

## 일반 카드 / 별도 이야기 연결

루트 `MainStory` 배열에 `Next`, `Choice`, `End` 카드를 작성합니다. Choice의 `options` 네 개는 좌상·좌하·우상·우하 순서입니다. 기존 Choice처럼 `choiceActions`가 없으면 선택 후 다음 카드로 진행합니다.

별도 이야기로 연결하려면 네 선택지와 같은 순서로 `choiceActions`를 작성합니다.

```json
{
  "MainStory": [
    {
      "type": "Choice",
      "text": "이제 무엇을 할까요?",
      "options": ["대련한다", "계속 이야기한다", "작별한다", "작별한다"],
      "choiceActions": [
        { "action": "story", "storyPath": "Initial/Initial_01/Encounters/duelist/Duel/Start" },
        { "action": "continue" },
        { "action": "skip" },
        { "action": "skip" }
      ]
    },
    { "type": "End", "text": "이야기를 마쳤습니다." }
  ]
}
```

`storyPath`는 Resources의 `Story_Json_Data` 기준 **폴더 경로**입니다. 끝에 `/Story`나 `.json`을 붙이지 않습니다. 해당 폴더에 `Story.json`을 만들면 됩니다. `story`는 그 이야기로 이동하고, `continue`는 다음 카드로 진행하며, `skip`은 조우를 마칩니다. 다른 Story로 이동한 뒤 자동으로 이전 카드에 돌아오지는 않습니다.

기존 선택 행동의 `requiresUnlocks`, `grantsUnlocks`, `relationshipId`, `relationshipDelta`도 필요할 때 같은 `choiceActions` 항목에 작성할 수 있습니다.

## 미니게임 시작 / 결과

시작 안내 Next 카드에는 `startAction`과 `winStoryPath`, `loseStoryPath`, `errorStoryPath`를 둡니다. 지원 행동은 `duel`, `coin_heads`, `coin_tails`입니다. 안내 표시 후 바로 미니게임을 실행하고, 종료 콜백이 해당 결과 Story를 엽니다.

대련 시작 카드의 `"duelHitTarget": 3`은 NPC/플레이어 공통 피격 목표입니다. NPC가 3회 맞으면 승리, 플레이어가 3회 맞으면 Health 코인이 남아 있어도 패배합니다. 생략/0이면 기본3, 음수는 오류입니다. 현재 교관은 `duelist/Duel/Start/Story.json`에서 수정합니다. 피격당 현재 Health 재고를 1개 차감하고 저장하며 0개가 먼저 되면 패배합니다. 새로운 Inspector 조절 항목은 없습니다.

저장에는 `activeStoryPath`를 함께 기록하므로 분기 중 이어하기는 해당 Story로 복원됩니다. 예전 저장 호환을 위해 `interactionPhase`라는 저장 필드 이름은 남겼지만 Interaction JSON을 읽는 코드는 제거했습니다. 회차별 생성 이야기는 기존처럼 text만 바꿀 수 있으며 선택 행동과 분기 경로는 원본과 같아야 합니다.

불필요해진 `Interaction.json` 17개와 대응 메타 파일은 삭제했습니다. 기존 Git 기록에서 복원할 수 있습니다. 코인 클릭이나 물리 입력의 Interaction 명칭은 이야기 시스템과 무관하므로 삭제하지 않았습니다.

## 확인 범위

최신 정리/검사 및 수동 테스트 순서는 `StoryCleanupAndTest.md`와 `LokiGuideStage1.md`를 기준으로 한다. `background`, `isTransition`, `statWeights`는 현재 Story 스키마에서 제거됐고 전환 전용 UI/가중치 계산도 사용하지 않는다. 아래 내용은 이전 작업의 검사 기록이다.

Unity 컴파일 오류 0개, Story Resources 28개 로딩, NPC/장소 17개의 첫 카드 직접 시작, 미니게임 선택지 2개, 시작/오류 분기 3개, 결과/End 분기 8개, 기존 체크포인트 및 분기 복원 3개를 격리된 메모리 검사로 확인했습니다. 생성 이야기가 선택 행동을 바꾸면 원본으로 대체하는 검증도 확인했습니다. 검사 중 실제 저장 파일과 씬은 저장하지 않았으며 Main 메뉴부터 끝까지 진행하는 Play 검사는 별도로 수행하지 않았습니다.

선택지에서 새 Story로 이동할 때 `JourneyBoardInput.ResetSelection()`이 선택 이벤트를 즉시 호출하므로 `PresentEncounterCard`는 초기화 전에 상태를 Transitioning으로 전환합니다. 이전 Choice 상태에서 새 Next 카드의 없는 options를 읽던 NullReferenceException을 수정했습니다. 실제 기어 초기화 이벤트를 연결한 설명/대련 시작·결과/동전 앞·뒤 등 6개 전환 검사와 선택지가 없는 카드의 표시 방어 검사를 통과했습니다.
