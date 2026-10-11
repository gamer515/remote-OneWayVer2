# 선택지 JSON 작성 기준

## 태도 선택

NPC에 대한 태도를 고르는 카드에는 `choiceKind: Attitude`를 지정합니다.

| 화면 번호 | 배열 위치 | choiceRole | 로키 정체 반응 원문 |
| --- | --- | --- | --- |
| 1 | 0 | Positive | 진짜 신이구나. 신기하네! |
| 2 | 1 | Neutral | 왜 이 광산에 있는 거야? |
| 3 | 2 | Negative | 관심없어. |
| 4 | 3 | Chaotic | 꺼져! |

문구 `options[i]`와 기능 `choiceActions[i]`가 한 묶음입니다.
각 행동에 고유하고 안정적인 `choiceId`와 위 표의 `choiceRole`을 넣으세요.
순서를 바꿀 때는 문구뿐 아니라 ID·분기·관계 변화·해금 조건도 함께 옮겨야 합니다.
이미 기록에 쓰인 ID를 다른 의미의 선택에 재사용하지 마세요.

```json
{
  "type": "Choice",
  "choiceKind": "Attitude",
  "text": "자, 무슨 말을 할 텐가?",
  "options": [
    "진짜 신이구나. 신기하네!",
    "왜 이 광산에 있는 거야?",
    "관심없어.",
    "꺼져!"
  ],
  "choiceActions": [
    { "choiceId": "loki_question_god", "choiceRole": "Positive", "action": "continue" },
    { "choiceId": "loki_question_place", "choiceRole": "Neutral", "action": "continue" },
    { "choiceId": "loki_dismiss", "choiceRole": "Negative", "action": "continue" },
    { "choiceId": "loki_insult", "choiceRole": "Chaotic", "action": "continue" }
  ]
}
```

위 예시는 필드 설명용입니다. 실제 로키 원문은 각 선택에 기존 별도 Story 분기가 연결되어 있습니다.
역할은 대사의 태도이며 보상 수치가 아닙니다. 중립 질문에 관계 +1이 있을 수도 있습니다.
현재 로키 4번은 무모한 모욕이므로 혼돈 칸에 두되, 꿈 기억은 별도 모욕 조건으로 선별합니다.
모든 혼돈 선택을 적대적으로 기억하지는 않습니다.

## 조작·체험 선택

난독 체험, 동전 앞뒤, 전투 시작/설명 메뉴, 단순 탐색 메뉴에는 `choiceKind: Mechanic`을 넣습니다.
`choiceRole`을 붙이지 않으며 위 네 태도에 억지로 맞추지 않습니다.
난독 선택은 기존 의도와 분기를 유지하며 발음 표현은 생성 과정에서 변할 수 있습니다.
기본 꿈 기억 규칙에서는 이런 선택을 호감/불쾌감으로 넘기지 않습니다.
능력치별 Variants도 같은 선택 종류와 기능 연결을 유지하세요.

## 검사와 기존 자료

태도 카드의 네 문구·네 행동, 고유 ID, Positive/Neutral/Negative/Chaotic 순서가 맞는지 검사합니다.
누락된 역할·엇갈린 순서·모르는 종류는 오류입니다. 관계 기능이 없는 과거 챕터의 미지정 메뉴는 기존대로 읽되 Unknown으로 기록합니다.
검사는 한국어 문장의 감정을 자동 판독하는 기능이 아닙니다. 작성자가 문구와 역할을 맞춰야 합니다.
생성/교정 프롬프트에는 태도 카드 번호와 짧은 공통 순서 규칙만 넣고, 모델은 문구만 변경합니다.

기존 번호별 Edit·Result와 세이브는 이 작업에서 덮어쓰거나 삭제하지 않습니다.
예전 Edit 입력에 선택 설명 필드가 없으면 현재 원문의 설명을 보완해서 적용 검사합니다.
분기 ID·기능 연결이 실제로 달라지거나 역할이 명시적으로 충돌하면 거절합니다.
과거 생성물이 예전 핵심 설명을 포함하면 로더 검증에 실패해 원문을 쓸 수 있습니다. 새 원문 기준으로 생성하거나 검증된 Edit을 다시 적용하세요.
Guide 원문을 변경하면 연결된 `LocalStory/GuideIntroBrief.json`의 정규화된 원문 SHA256도 갱신해야 합니다.
