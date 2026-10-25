# 이야기 프롬프트 JSON 모듈 (v8)

NPC·장면별 집필 지시와 예시는 C#에 작성하지 않는다. 코드는 로딩·조건 선택·조립·검증·게임 연결만 담당한다.

| 내용 | 작성 위치 |
|---|---|
| 모듈 목록·조립·길이·추론·실패본 적용 설정 | `Assets/Decision_YYS/Resources/LocalStory/PromptPolicy.json` |
| 공통 작성 규칙 | 같은 폴더의 `PromptModules/Common.json` |
| 선택지·정상 발화·난독 규칙과 입출력 예시 | `PromptModules/Choice.json`, `Normal.json`, `Garbled.json` |
| 기억 조건 | `PromptModules/MemoryNone.json`, `MemoryCurrent.json`, `MemoryDream.json` |
| 해금·고정 문자열·교정·출력 | `PromptModules/Unlocked.json`, `Fixed.json`, `Correction.json`, `Output.json` |
| NPC 이름·정체·성격·말투·조건부 지식 | 각 챕터 `Characters.json` |
| 환경·단락·카드 의미·주체·전후 상태·선택 의도 | 각 장면 `Explanation.json` |

## 조립

Characters·Explanation·실제 기억/해금을 읽고 요청의 `hasChoices`, `speechMode`, `memoryMode`, `hasFixedStrings`, `hasUnlockedFacts`, `task`를 계산한다. manifest의 modules 순서대로 JSON의 when을 모두 만족하는 모듈만 선택한다. 빈 when은 공통 모듈이다.

선택한 rules를 system 내용으로 합치고 관련 examples를 최대 composition.maxExamples개 넣는다. 예시는 input과 output.cards로 구성한다. 예시 자체의 when도 지원하며 모델에 전달하지 않는다. 새 모듈은 LocalStory 하위 JSON으로 작성하고 PromptPolicy.modules에 확장자 없는 resource 경로를 추가한다.

일반 대사는 Common + Normal + 해당 Memory + Output이다. 선택지·난독·교정 등의 규칙은 해당 요청에만 추가한다. 난독 요청에는 정상 발화 예시가 들어가지 않는다. Gemma 어댑터는 system과 user 내용을 한 user 메시지로 합친다.

실제 선택 ID는 코드가 보관한다. 모델에는 요청 안의 번호, 한국어 intent와 reference만 전달한다. composition.useEditedExamples=false로 과거 Edit 파일 자동 검색은 껐으며, 기본 예시는 JSON 모듈에서 읽는다.

## 주체와 상태

Explanation의 coreMeaning.<카드 ID>에 필요할 때 아래 필드를 작성한다. 인물 이름과 집필 문구는 이 파일에 둔다.

```json
{
  "participants": { "speaker": "인물 이름", "listener": "여행자", "actor": "행동하는 인물", "target": "영향받는 인물" },
  "stateBefore": { "여행자": "사건 전 상태" },
  "stateAfter": { "여행자": "사건 후 상태" },
  "dialogueTiming": "대사가 사건 전후 중 언제인지"
}
```

choices.<선택 ID>.intent에는 긍정·부정 분류 대신 플레이어가 누구에게 무엇을 묻거나 요구하는지 작성한다. 없으면 기존 의미 참고문 또는 원문 선택지를 사용한다. 신규 NPC를 위한 C# 분기는 필요 없다. Story와 Terrain의 실행 연결은 이 작업에서 변경하지 않는다.

## 실패본·정책 변경

runtime.allowFailedOutput=false가 기본이다. 실패 원출력은 기록에서 확인하고 자동 게임 적용과 저장된 실패본의 자동 승격을 막는다. 테스트 창의 명시적 실패본 미리보기는 남는다. 실패본을 게임에서 확인할 때만 이 설정을 켠다. 이는 v7의 실패본 자동 재생 기본값을 대체한다.

모듈 내용·조립 설정·인물·Explanation 전체가 정책 해시에 반영된다. 이전 자동 생성본·체크포인트를 새 정책의 결과로 재사용하지 않는다. 기존 저장 파일과 사람이 수정한 자료는 삭제하지 않는다. 현재 열린 대화 snapshot은 유지한다.

## 검증

AgentScripts/PromptCompositionCheck.cs의 Regression은 모듈 조건·예시·주체·ID 차단·JSON 수정 반영을 검사한다. Baseline/After는 실제 Gemma 결과와 시간을 tmp/PromptComposition에 기록하며 플레이어 저장소에 적용하지 않는다.

구간별 모듈 경로·프롬프트 문자 수·첫 출력·적재·생성·교정 시간을 남긴다. 최종 프롬프트 검토본은 Docs/CurrentLLMPrompt.md다. 첫 출력 시간은 순수 입력 처리 시간이 아니다. 현재 콜백이 실제 토큰 수를 제공하지 않으므로 문자 수와 토큰 수를 혼동하지 않는다.

형식·ID·게임 연결 검사는 의미 정확도를 보장하지 않는다. 실제 대사의 주체·사건·선택 의도·자연스러움을 별도로 평가해야 한다. 추론 설정은 JSON으로 분리했지만 속도 개선은 실측으로 판단한다.
