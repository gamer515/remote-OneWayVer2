# 챕터 이야기 작성과 저장 구조

공통·조건별 집필 규칙과 예시 작성은 [PromptModules.md](PromptModules.md)를 참조한다. Characters는 인물 설정, Explanation은 환경·목적·주체·상태·선택 의도를 담당하며, 집필 문구를 C#에 추가하지 않는다.

2026-10-25 적용. 이야기별 설명과 단락은 JSON에 작성하고 공통 로더·프롬프트 조립기로 읽는다. 새 챕터나 NPC마다 C# 조건문을 추가하지 않는다.

## 원본 위치

`Assets/Decision_YYS/Resources/Chapters/`

```text
Catalog.json
Omnibus.json
Chapter_00/
  Characters.json
  Terrain.json
  Transform.json
  Chapter_00_01/
    Guide/
      Story.json
      Explanation.json
      Branches/
        QuestionGod.Story.json
        QuestionGod.Explanation.json
        QuestionPlace.Story.json
        QuestionPlace.Explanation.json
      Hidden.Story.json
      Hidden.Explanation.json
    Duelist/...
    Gambler/...
Chapter_01/...
Chapter_02/...
```

기존 `Initial`은 물리 폴더 `Chapter_00`, 기존 `Chapter_1`은 `Chapter_01`, 기존 `Chapter_2`는 `Chapter_02`에 대응한다. 기존 세이브·분기를 유지하기 위해 논리 ID와 이야기 경로(`Initial/Initial_01/Encounters/guide` 등)는 Catalog에 보존했다. 물리 폴더명과 호환용 논리 ID의 매핑이며, 원본을 두 벌 보관하는 방식이 아니다.

현재 Omnibus의 진행 상한(lastPlayableChapterId)은 기존 논리 ID Initial이다. 새 챕터를 플레이하려면 이 값을 해당 챕터 ID로 바꾸거나 비운다. 새 파일을 추가하는 것만으로 진행 상한을 자동 해제하지 않는다.

챕터가 환경을 결정한다. 같은 NPC가 여러 청크에 등장하면 Terrain의 `chunkIndex`, `placeId`, `characterId`, `connectStoryCards`로 각각 연결한다. NPC 파일명에 광산·왕국을 붙이지 않는다. LLM이 나누어 생성하는 짧은 구간마다 파일을 만들지는 않는다.

NPC의 메인은 Story/Explanation, 선택지의 갈림은 Branches 아래 해당 분기별 Story/Explanation으로 분리한다. 같은 분기의 능력치 변형은 해당 분기 파일에 모은다. 숨겨진 아이템·해금 대화는 Hidden.Story/Hidden.Explanation에 모은다. Characters에는 챕터 NPC 설정과 숨은 설정의 참조를 모은다. 미작성 능력치는 비워 두며 임의 수치를 생성하지 않는다.

## 이야기와 설명 예시

`Guide/Story.json`의 메인 묶음 키는 `guide`다. 선택지는 storyPath로 분기 파일의 논리 경로를 지정한다. 아래 예시는 다른 청크의 메인을 작성하는 방법이며 기본 게임에 자동 추가되지 않는다.

```json
{
  "guide/second_chunk": {
    "MainStory": [
      {
        "ID": 1001,
        "type": "Next",
        "eventIDstable": "guide_second_chunk_greeting",
        "speakID": "Guide",
        "Text": "여기까지 왔군. 잠시 쉬어 가게."
      },
      {
        "ID": 1002,
        "type": "Choice",
        "eventIDstable": "guide_second_chunk_reply",
        "speakID": "Guide",
        "Text": "나에게 할 말이 있나?",
        "choiceKind": "Attitude",
        "options": ["다시 보니 반갑네.", "이 길은 어디로 이어져?", "혼자 가고 싶어.", "길을 안내하면 믿어 볼게!"],
        "choiceActions": [
          {"choiceID":"second_positive","action":"continue","relationship":"Guide","relationshipDelta":1},
          {"choiceID":"second_neutral","action":"continue","relationship":"Guide","relationshipDelta":0},
          {"choiceID":"second_negative","action":"continue","relationship":"Guide","relationshipDelta":-1},
          {"choiceID":"second_chaotic","action":"continue","relationship":"Guide","relationshipDelta":0}
        ]
      }
    ]
  }
}
```

대사 카드에 허용한 속성은 `ID/type/eventIDstable/speakID/Text/guideAction/choiceKind/options/choiceActions`뿐이다. choiceActions는 `choiceID/action/storyPath/relationship/relationshipDelta`만 허용한다. 사용하지 않는 선택 속성은 생략한다. 새 속성은 로더가 거절한다. 긍정·부정 같은 의도는 Story의 선택 문구나 행동 속성에 넣지 않고 아래 Explanation에서 지정한다.

ID는 대사를 고치거나 위치를 옮겨도 유지한다. 카드 순서를 바꾼다고 ID를 재번호 부여하지 않는다. `eventIDstable`은 실행 사건, `choiceID`는 선택 행동을 식별한다. 같은 NPC의 다른 청크 조우는 서로 다른 이야기 경로·묶음 키를 사용한다.

`Guide/Explanation.json` 또는 해당 분기의 `.Explanation.json`에 같은 키로 설명을 연결한다. 문서에서 지정한 `storyFlame` 표기를 사용한다.

```json
{
  "guide/second_chunk": {
    "storyFlame": {
      "purpose": "같은 챕터의 다른 청크에서 로키와 대화한다.",
      "worldFacts": ["NPC는 로키다.", "현재 챕터의 환경을 유지한다."],
      "firstMeetingFlow": ["이 장소에서 처음 대화하는 상황에 맞춰 인사한다."],
      "reunionFlow": ["이번 회차의 실제 이전 만남이 있다면 그 결과와 이어간다."]
    },
    "coreMeaning": {
      "1001": {"coreMeaning":"로키가 여행자를 맞아 쉬어 가라고 권한다."},
      "1002": {
        "coreMeaning":"각 선택 ID의 의미에 맞는 반응을 고른다.",
        "choices": {
          "second_positive": {"choiceRole":"Positive"},
          "second_neutral": {"choiceRole":"Neutral"},
          "second_negative": {"choiceRole":"Negative"},
          "second_chaotic": {"choiceRole":"Chaotic"}
        }
      }
    }
  }
}
```

난독 의미, 선택지의 정상 발화 참고, 카드별 목적·현재 상황은 Explanation의 coreMeaning에 ID별로 모은다. `storyFlame`은 전체 목적·사실·첫 만남/이번 회차 재회 흐름, `coreMeaning.scene`은 장면의 사실·말투를 담는다.

같은 이야기 묶음에 `paragraphs`를 반드시 추가한다. 위 예시의 설명에는 다음 배열을 `storyFlame`, `coreMeaning`과 같은 깊이에 둔다.

```json
"paragraphs": [
  {
    "id": "greeting",
    "eventIds": ["guide_second_chunk_greeting"],
    "purpose": "여행자를 맞아 쉬어 가라고 권한다.",
    "currentState": ["이 장소에서 이번 대화를 시작한다."],
    "limits": {"maxCards": 3}
  },
  {
    "id": "reply",
    "eventIds": ["guide_second_chunk_reply"],
    "purpose": "플레이어가 인사, 길 질문, 거절, 제안 중 하나로 반응하도록 묻는다.",
    "currentState": ["인사는 끝났으며 플레이어의 응답을 기다린다."]
  }
]
```

단락은 Story의 모든 카드를 순서대로 한 번씩 포함해야 한다. 누락·중복·역순은 생성 전에 거절한다. 기존 eventIDstable이 없는 카드는 `card:정수ID`로 참조할 수 있다. 새 카드는 안정적인 eventIDstable 사용을 권장한다. 단락은 파일이 아니므로 분할할 때 파일 수가 늘지 않는다.

기본 생성량은 PromptPolicy의 최대 3카드다. Explanation 전체의 `limits`와 단락의 `limits`로 차례로 덮어쓸 수 있다. 선택지 카드와 게임 행동, 정상/난독 전환에서는 같은 단락 안에서도 요청을 분리한다. 선택지 질문과 모든 options는 함께 생성한다. 로키 도입은 4단락·6요청이며 9카드·8선택지를 모두 생성한다.

`limits`는 `maxCards`(1~16), `textMaxChars`(1~160), `optionMaxChars`(1~45), `maxOutputTokens`(128~1800)를 지원한다. 실제 최적 생성량은 장면과 기기에서 측정해야 한다. `promptRules`에는 해당 장면에만 필요한 집필 규칙 배열을 둔다. 로키의 난독 원인 설명도 여기에 있으며 공통 코드에는 인물별 문장을 넣지 않는다.

공통 규칙은 `Assets/Decision_YYS/Resources/LocalStory/PromptPolicy.json` 한 곳에서 수정한다. LocalStoryPrompt가 Characters·Explanation·해금 스냅샷을 읽어 인물/장면과 이번 대상 자료를 조립한다. 공통 규칙과 인물/장면은 앞에, 단락·기억·대상·교정 초안은 뒤에 배치한다. Gemma에는 두 역할을 합친 user 메시지 하나와 별도 출력 문법·샘플링 설정을 전달한다. 프롬프트 계산 캐시를 활성화했으며 모델 적재 재사용과 별도 기능이다. 전체 입력은 `Docs/CurrentLLMPrompt.md`에서 확인할 수 있다.

`choices`는 버튼 번호가 아닌 `choiceID`를 키로 삼는다. 선택 문구와 해당 행동을 함께 옮겨 순서를 바꾸어도 설명의 의도는 ID를 따라간다. 여러 선택이 같은 의도여도 된다. 런타임에서만 설명을 읽어 기록·꿈 기억 필터에 쓰며, 분류명은 게임 선택지에 표시하지 않는다. 조작 선택(`Mechanic`)에는 관계 의도를 붙이지 않는다. 관계 선택(`Attitude`)은 여전히 현재 UI에 맞는 네 선택을 사용한다.

## 회차에 따른 능력 배분 설명 생략

첫 회차의 `guide/Redistribute`는 설명 16카드와 실제 배분 1카드다. 같은 Redistribute Story/Explanation 파일 안에 `guide/Redistribute/Repeat` 묶음을 두고 배분 1카드만 담았다. 별도 파일은 늘리지 않았다. 원래 배분 카드의 ID와 `allocate` 행동·확정/재시도/사망 분기 연결을 유지한다.

원래 묶음의 Explanation에 다음 경로를 지정한다. 두 경로는 Catalog에 등록되어 있어야 하며 같은 NPC·에피소드여야 한다.

```json
"coreMeaning": {
  "scene": {
    "repeatFromRun": 2,
    "repeatStoryPath": "Initial/Initial_01/Encounters/guide/Redistribute/Repeat"
  }
}
```

진입 시 현재 회차로, 다음 회차 생성 큐에는 대상 회차로 경로를 선택한다. 첫 회차가 끝나면 다음 회차용 Repeat 장면을 생성하고 선택된 장면의 모든 카드를 짧은 구간으로 처리한다. 이전 저장의 설명 생성 대기 작업은 기록으로 보존하고 반복 장면 원문으로 플레이할 수 있다. 첫 회차 원문과 NPC·아이템·숨은 비밀 대화는 생략하지 않는다. 진행 중 배분 초안과 확정 결과는 기존 저장 방식으로 이어진다.

## 청크에 연결하기

1. NPC의 Story/Explanation에 같은 묶음 키를 추가한다.
2. Catalog.stories에 `path/chapterId/chapterFolder/episodeId/characterId/placeId/chunkIndex/key/storyFile/explanationFile` 연결을 추가한다. `key`는 두 파일의 묶음 키와 같아야 한다. storyFile은 `Chapter_00/Chapter_00_01/Guide/Story.json`, explanationFile은 `Chapter_00/Chapter_00_01/Guide/Explanation.json`처럼 Chapters 아래 상대 경로를 적는다.
3. Terrain.episodes의 해당 에피소드 places에 `placeId/chunkIndex/prefabId/characterId/connectStoryCards`를 지정한다. connectStoryCards는 Catalog의 path다.
4. Transform.episodes의 같은 에피소드 places에 동일한 placeId의 좌표·회전을 지정한다.
5. 새로운 NPC라면 Characters.characters에 정의하고, 해당 에피소드의 NPC Story/Explanation 두 파일을 만든다. C#에 NPC 이름을 추가할 필요는 없다.

랜덤 배치 순서나 실제 월드 좌표가 바뀌어도 placeId와 이야기 연결은 유지한다. 다른 청크의 다른 조우는 다른 placeId로 구분한다.

## 아이템·해금·전투 연결

이야기 속성을 늘리지 않고 Terrain의 `actions`와 `interactions`에서 연결한다.

- actions: 논리 이야기 경로 → 문장 ID → 기존 게임 행동과 선택 조건. 결투 시작·승패 경로·다음 이야기, 선택별 아이템 보유/해금 조건 등이 여기에 있다.
- interactions: NPC/choiceIds에 연결된 아이템 지급·회수, 해금, 후속 이야기. 로키 표식 지급·비밀 발견·반납 예제가 실제로 연결돼 있다.
- Characters.hidden: 숨은 설정의 실제 텍스트와 requiresUnlock_ALL. 규칙과 Excel은 `@npc:Guide/hidden/prototype_loki_mark_secret`처럼 참조한다.

회차 전환은 가방 아이템·꺼내 둔 아이템의 ID/좌표·해금을 복사한다. 대화 진행과 장소 해결, `event:` 장소 완료 플래그, 효과 중복 방지 기록은 새로 시작한다. 실제 반납·소비한 아이템은 되살리지 않으며 전체 초기화 시에는 소유와 해금도 초기화한다.

로키의 긍정 선택에서 표식이 없으면 기존 지급으로 진행한다. 이전 회차에서 가져온 표식이 있으면 Hidden의 MarkOwned가 열리고 첫 선택지는 “어, 나 이미 그 아이템 있는데?”다. 선택하면 MarkOwnedExplain에서 의아해하는 반응과 광산 비밀 설명을 보고 해금한다. 비밀까지 이미 알면 MarkKnown의 첫 선택지와 MarkKnownExplain 반응으로 달라진다. 같은 회차에서 로키가 지급한 사실이 있으면 이전 회차의 물건처럼 놀라지 않고 기존 선물을 상기한다. 조건은 Terrain.interactions의 requiresItems/excludesItems, requiresUnlocks/excludesUnlocks, requiresEffects/excludesEffects로 판정한다. 대사 카드 속성은 늘리지 않는다.

현재 guideAction은 기존 지식 체험·반환·벌·무릎 꿇기·능력 배분 행동을 유지한다. 보스 관계·약점 같은 새 전투 규칙을 LLM이 임의로 만들지는 않는다. 새 게임 규칙의 작성은 별도 게임 로직 작업이다.

## Excel

게임에서 사용하는 파일은 `Saves/Records/Chapter_00.xlsx`, `Chapter_01.xlsx`, `Chapter_02.xlsx`다. 최초 사용 시 StreamingAssets/LocalStory/Chapters 템플릿에서 생성한다. 기존 Saves/Narrative/NpcStoryData.xlsx가 있으면 NPC 설정과 해금 규칙을 옮기고 원본 바이트를 Migrations에 보관한다.

각 문서에는 NPC설정·해금규칙·아이템보상의 세 시트만 있다. 역할·성격은 Characters.json에서 읽으며 Excel에 중복 작성하지 않는다. 실제 플레이 기록과 생성 상태도 기존 JSON에만 저장한다.

- NPC설정에서는 이름·자동생성·모델·능력치를 조절한다. 공개/숨은 설정 참조와 해금규칙·아이템보상은 JSON에서 자동으로 표시한다. 실행 분기 조건은 Terrain.interactions와 Characters.hidden이 기준이다. Excel의 별도 활성/분기 재지정 기능은 제거했다.
- 해금규칙은 `해금_ID / NPC_ID / 설명 / 해금상태 / 설정 참조`의 다섯 열이다. 0은 미해금, 1은 해금이며 실제 진행 기록에서 갱신한다. 수동으로 비트를 바꾸어 게임 해금을 부여하는 기능은 아니다. 새 콘텐츠의 조건과 기존 만남 완료 ID도 목록에 자동 반영된다.
- 요청 준비 때 Excel의 0/1을 읽어 ID별 상태와 NPC의 허용 해금 ID 배열로 보관한다. 각 구간은 이 스냅샷을 사용하며 Excel을 다시 읽지 않는다. Characters.hidden의 모든 requiresUnlock_ALL과 requiresItems_ALL을 만족한 text만 프롬프트에 포함한다. 아이템 조건은 요청에 보관한 실제 소유 목록으로 검사하며 영구 해금과 구분한다.
- 체력·속도·지능·매력은 미작성 시 모두 빈칸으로 둔다. 입력한다면 네 칸 모두 정수를 작성한다.
- 선택_ID는 원문의 choiceID를 그대로 사용한다. 대사 문구로 치환하지 않는다. 한 셀에 여러 ID가 있으면 항목마다 줄을 나눈다. 목록 번호는 ID의 일부가 아니다.
- 현재상황은 보유/미보유 아이템, 해금/미해금 상태, 이번 회차 기록의 발생/미발생을 구분해 한 줄씩 표시한다. 한 규칙의 현재상황은 모두 충족해야 하며, 선택_ID 목록 중 하나를 골랐을 때 해당 규칙을 검사한다. 지급·회수·해금 결과는 별도 열에 표시한다.
- 자동생성 기본값은 Characters의 autoGenerate에서 읽는다. 현재 로키/Gemma 켜짐, 나머지 NPC 꺼짐이다. 해당 챕터 NPC설정의 자동생성을 1로 바꾸면 등록된 NPC도 생성 대상이 된다. Characters의 speechStyle에는 NPC 기본 말투를 지정할 수 있다.
- 새 NPC·숨은 설정·상호작용을 JSON에 등록하면 같은 공통 로직으로 행을 추가한다. 제거한 콘텐츠의 표시 행도 제거한다. 동일 내용으로 Excel을 다시 쓰지 않으며 생성 구간마다 Excel을 갱신하지 않는다.
- 기존 Records는 편집한 제어값을 유지해 새 형식으로 옮긴다. 이전 파일은 Migrations/BeforeSimpleRecords에 한 벌만 보관한다. Excel이 잠기면 갱신을 보류하고 JSON 진행을 유지한다.

템플릿은 NotebookCleanupCheck.ExportTemplates로 공통 표시 데이터를 내보낸 뒤 AgentScripts/StoryWorkbook/chapters.mjs로 재생성한다. 런타임과 같은 JSON 표시 로직을 사용한다. 별도 템플릿이 없는 새 챕터도 기존 공통 시트 서식을 재사용해 생성한다.

## 생성본·검토본·진행 기록

```text
Saves/
  GeneratedContent/For_Run_02/
    Queue.json
    Chapter_00/Chapter_00_01/Guide/
      Features.json
      Story.json
  LocalStoryTests/
    Results/For_Run_02/Chapter_00/Chapter_00_01/Guide/
      0001.Result.json
    Edits/For_Run_02/Chapter_00/Chapter_00_01/Guide/
      0001.json
  Records/Chapter_00.xlsx
  Runs/Run_01/
    Progress.json
    History.json
    History.previous.json
  Migrations/LegacyContent.zip
```

Features는 생성에 사용한 관계값·관계 변화·말투·인상·해금·작업 상태를 보여 준다. Story는 조우별 저장 정보와 `ID/Text/options` 변경분을 담는다. 실행할 때 원문의 행동·분기와 합친다. 다른 청크의 조우들도 이 두 파일에 모인다.

Results의 draft/correction과 Edits는 변경된 줄을 ID로 표시한다. Edit의 ID를 유지하고 text/options를 고친다. 빠진 줄은 원래 AI 출력과 합쳐 적용한다. Result에는 당시 원문 행동 연결과 AI 원출력을 한 번씩 보관하므로 원문 변경 전의 검토본도 검사할 수 있다. 실패해 파싱할 수 없는 출력도 그대로 남긴다.

Result의 diagnostics에는 마지막 실제 전송 프롬프트, 시간, 모델, CPU/GPU/메모리·운영체제가 있다. Evidence.zip을 만들지 않으며 결과 JSON만으로 조회·Edit 적용이 가능하다. 구간별 전체 요청/출력과 실제 플레이 전체 기록을 중복 저장하지 않는다. 과거 Evidence는 필요한 결과를 검증해 옮긴 후 삭제하고 Edits는 유지한다. 실제 기록이 필요한 경우 Runs의 Progress/History에서 읽는다.

자동 생성 결과는 이야기별 최근 세 결과를 유지한다. 사람이 고친 Edits와 검토한 결과는 자동 정리에서 제외한다. 실행용 자동 생성본은 현재 기준 최근 네 회차와 다음 회차 범위를 관리하며 수동 검토본과 실제 플레이 기록을 보존한다.

Progress는 progress/encounter/player/world/effects로 구분한다. 누적 대화·선택 기록은 History로 옮기고 현재 대화 기록은 같은 내용의 중복 사본 대신 참조로 저장한다. 현재 진행 중인 대화와 효과·능력 배분 상태, 가방과 꺼낸 아이템의 개체 ID·위치·회전은 유지한다. History.previous는 기록 저장 중 중단됐을 때 이전 Progress와 맞는 기록을 복구하는 한 개의 예비본이다.

기존 Run_0001은 Run_01로 이전한다. 예전 평면 Progress는 계속 읽을 수 있고 다음 저장부터 새 형식으로 기록한다. LocalPacket 생성은 중단했으며 이전 파일은 복구 보관한다. 이전 Narrative/Chapters와 Results/Input/Analysis/HTML은 새 구조로 이전한 뒤 원본을 압축 보관한다. 실제 사용자가 진행을 시작할 때 이전하며 에이전트 검증은 격리 폴더를 사용한다.

원문 버전이 다른 생성본은 적용을 보류한다. 자동 생성기가 소유한 결과는 새 원문 기준으로 다시 준비할 수 있고, 사람이 검토한 결과는 자동으로 덮어쓰지 않는다. 생성이 늦거나 실패하면 원문으로 플레이한다. 이미 열린 대화는 저장된 내용을 유지한다.
