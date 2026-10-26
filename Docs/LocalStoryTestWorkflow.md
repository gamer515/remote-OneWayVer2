# 이야기 생성 확인 방법

현재 프롬프트는 JSON 모듈 구조이며 [PromptModules.md](PromptModules.md), [StorageLifecycle.md](StorageLifecycle.md)를 우선 참조한다. 사용자 요청으로 실패본 다음 회차 재생을 켰고, 회차 전환·정책 변경에 의한 개정 재생성은 없앴다. 아래 버전별 설명은 과거 검증 기록이다.

2026-10-26 현재 구조. 데이터 작성과 경로 설명은 [ChapterStoryAuthoring.md](ChapterStoryAuthoring.md)를 참조한다.

## 실제 플레이

1. Unity Main 씬에서 Play하고 기존 세이브는 이어하기로 진입한다. Windows 빌드는 Builds/GemmaWindows/OneWay.exe다.
2. 로키와 대화한다. 원문에서 “진짜 신이구나. 신기하네!” 선택은 표식 지급과 표식 대화로 이어진다. 생성본에서는 문장이 달라질 수 있다. 긍정·부정 같은 분류명은 선택지에 붙지 않아야 한다.
3. 표식을 가방 밖으로 꺼내 놓고 종료한 뒤 다시 실행한다. 아이템이 같은 위치에 하나만 복원되는지 확인한다. 진행 중이던 대화·능력 배분도 이어져야 한다.
4. 표식 대화에서 용도를 묻는 선택은 비밀 해금을, 돌려주는 선택은 아이템 회수를 확인할 수 있다. 같은 선택 효과가 재실행 시 중복되지 않아야 한다.
5. NPC 조우를 끝까지 마친다. Console의 LocalStory 요청·작업 로그와 GeneratedContent/For_Run_다음번호/Queue.json을 확인한다. 대화를 열기만 해서는 생성 요청이 생기지 않는다.
6. 같은 폴더의 Chapter_00/Chapter_00_01/Guide/Features.json과 Story.json을 확인한다. 청크·분기별로 파일이 늘어나는 대신 stories에 모여야 한다.
7. 다음 회차에서 완료된 생성본은 새 만남부터 사용한다. 문구 검사 실패본도 ReviewRequired로 표시한다. 미완료·표시 불가 출력·원문 변경이면 원문으로 진행할 수 있어야 한다. 현재 읽는 대화는 생성 완료만으로 바뀌지 않는다.
8. Saves/Records/Chapter_00.xlsx를 다시 열어 NPC설정·해금규칙·아이템보상 세 시트만 있는지 확인한다. 역할·성격·플레이기록·생성현황은 없어야 한다. 아이템보상의 선택_ID에는 loki_question_god 같은 choiceID가 남고 현재상황은 종류별로 줄이 나뉘어야 한다. Excel은 갱신 시 닫아 두고 다시 열어 확인한다.
9. 표식을 반납하지 않은 상태로 회차를 끝내고 다음 회차로 넘어간다. 가방 안에 있든 밖에 꺼냈든 그대로 하나가 남아야 한다. 로키의 마지막 태도 선택에서 긍정을 고르면 첫 선택지가 “어, 나 이미 그 아이템 있는데?”인 숨은 대화가 열린다. 그 선택으로 의아해하는 반응과 비밀 설명을 확인한다.
10. 다시 회차를 넘기면 “오래된 길의 단서라는 것도 알아” 선택지와 비밀을 아는 반응으로 달라진다. 반납했다면 다음 회차에 표식이 되살아나면 안 된다. 전체 초기화에서는 아이템·해금이 모두 사라져야 한다.
11. 첫 회차에서 능력 배분까지 진행하면 능력 설명 후 배분 화면이 나온다. 2회차 이상에서는 같은 배분 진입 지점에서 설명 16카드를 건너뛰고 짧은 대사 1개와 배분 화면이 나와야 한다. 표식·비밀 관련 대화는 그 앞에서 계속 확인할 수 있다.
12. 2회차 배분 중 일부를 배치하고 종료한 뒤 이어한다. 배치 초안이 복원되고, 확정·재시도·능력치 0에 따른 기존 결과가 유지되어야 한다. 이미 확정한 배분을 재접속 때문에 다시 확정하게 해서는 안 된다.

기존 기록을 유지하는 확인에는 저장 초기화 기능을 사용하지 않는다. 데이터 이전은 실행 시 수행되며 이전 파일은 Migrations/LegacyContent.zip과 LocalStoryTests/Legacy.zip에 보관된다.

## 생성 결과 직접 확인

DecisionScene의 ‘이야기 LLM 테스트’를 연다. Gemma 선택을 확인하고 생성하거나 ‘저장 결과 보기’를 사용한다. 테스트 생성은 실제 만남 기록으로 승격되지 않는다.

‘로컬 생성’도 자동 게임 생성과 같은 단락 생성기를 사용한다. 생성 중 `단락 1/4 (arrival) / 요청 1/6 / 카드 1~3/9`처럼 표시된다. 완료 시 단락 4개·작성 요청 6회·모델 대상 9카드를 확인한다. 9는 카드 번호이며 요청 9회를 뜻하지 않는다. 교정은 실패 구간에만 추가된다.

| Explanation 단락 | 카드 | 작성 요청 |
|---|---|---|
| arrival: 첫 만남·광산 안내 | 1~3 | 1회 |
| knowledge_prank: 지식 회수·난독 반응·반환 | 4, 5, 6 | 상태 경계마다 1회, 총 3회 |
| after_prank: 정체·광산에 온 이유 | 7~8 | 1회 |
| final_response: 질문과 선택지 4개 | 9 | 함께 1회 |

결과 위치:

- LocalStoryTests/Results/For_Run_02/Chapter_00/Chapter_00_01/Guide/0001.Result.json
- LocalStoryTests/Edits/For_Run_02/Chapter_00/Chapter_00_01/Guide/0001.json

Result의 draft/correction은 변경 대사와 선택지를 보여 주고 diagnostics는 마지막 실제 프롬프트·소요 시간·모델·환경을 표시한다. diagnostics.sectionTimings는 요청별 카드 범위와 작성/교정·파일 검증·모델 적재·추론 시간을 표시한다. Evidence.zip은 생성하지 않는다. 기존 결과도 JSON만으로 저장 결과 보기와 Edit 적용이 되어야 한다.

Edit은 ID를 유지하고 text/options를 수정한다. 테스트 UI의 ‘게임 확인용 적용’에서 AI 초안·AI 교정본·Edit 초안·Edit 교정본을 고르고 미리보기와 적용 회차를 확인한 뒤 저장한다. 자동 게임 생성과 이 수동 적용은 별도 경로다. 문구 검사 실패도 확인용으로 반영하며 실패 이유를 미리보기에서 보여 준다. 깨진 JSON/잘못된 인덱스/선택지 수는 실행 연결로 사용하지 않는다.

## 개발 검증 명령

연결된 Unity Editor에서 Play를 중지하고 다음을 실행한다. 모두 실제 Saves 대신 격리 저장소를 사용한다.

```powershell
unity command run_script --file AgentScripts/ChapterStructureCheck.cs --entry ChapterStructureCheck.Sources
unity command run_script --file AgentScripts/ChoiceExplanationCheck.cs --entry ChoiceExplanationCheck.Regression
unity command run_script --file AgentScripts/LocalStoryChoiceOrderCheck.cs --entry LocalStoryChoiceOrderCheck.Regression
unity command run_script --file AgentScripts/ChapterStructureCheck.cs --entry ChapterStructureCheck.Storage
unity command run_script --file AgentScripts/ChapterStructureCheck.cs --entry ChapterStructureCheck.Migration
unity command run_script --file AgentScripts/ChapterStructureCheck.cs --entry ChapterStructureCheck.ChunkIsolation
unity command run_script --file AgentScripts/ChapterStructureCheck.cs --entry ChapterStructureCheck.StaleSourcesAndRetention
unity command run_script --file AgentScripts/NarrativeStoreCheck.cs --entry NarrativeStoreCheck.Regression
unity command run_script --file AgentScripts/NarrativeStoreCheck.cs --entry NarrativeStoreCheck.SectionsRegression
unity command run_script --file AgentScripts/LocalStoryDialogueQueueCheck.cs --entry LocalStoryDialogueQueueCheck.Regression
unity command run_script --file AgentScripts/NarrativeProgressCheck.cs --entry NarrativeProgressCheck.InventoryPersistence
unity command run_script --file AgentScripts/StoryRevisionCheck.cs --entry StoryRevisionCheck.Regression
unity command run_script --file AgentScripts/StoryRevisionCheck.cs --entry StoryRevisionCheck.GenerationStatus
unity command run_script --file AgentScripts/NotebookCleanupCheck.cs --entry NotebookCleanupCheck.Regression
unity command run_script --file AgentScripts/NotebookCleanupCheck.cs --entry NotebookCleanupCheck.ArchiveRegression
unity command run_script --file AgentScripts/StoryRevisionCheck.cs --entry StoryRevisionCheck.InvalidGenerationFallback
unity command run_script --file AgentScripts/StoryPromptModuleCheck.cs --entry StoryPromptModuleCheck.Regression
unity command run_script --file AgentScripts/StoryPromptModuleCheck.cs --entry StoryPromptModuleCheck.ExportReview
unity command run_script --file AgentScripts/StoryParagraphTimingCheck.cs --entry StoryParagraphTimingCheck.Regression
unity command run_script --file AgentScripts/FailedStoryPlaybackCheck.cs --entry FailedStoryPlaybackCheck.Regression
```

Sources는 원문 58묶음·103카드·52선택지를 검사한다. 최초 변환 시 보관한 tmp/ChapterMigration/Originals.json이 있으면 변환 전후 게임 데이터도 비교한다. 구간 생성 검사는 로키 9카드·8선택지 모두가 대상인지, 취소 후 완료 구간을 재사용하는지 확인한다.

선택 의도 작성 위치는 `Chapter_00/Chapter_00_01/Guide/Explanation.json`의 `coreMeaning.-1574548902.choices`다. ChoiceExplanationCheck는 실제 파일을 바꾸지 않고 메모리 안에서 의도와 버튼 순서를 바꿔 ID별 연결을 검사한다. 첫 회차 17카드 보존, 2회차 1카드 선택, 다음 회차 생성 큐·저장·복원, 옛 설명 작업의 보존도 검사한다.

격리 가짜 추론 검사는 데이터 흐름 검증이며 문장 품질 평가가 아니다. 실제 모델 검사는 NativeSectionsSmoke를 사용하고 결과의 targetedCards/targetedOptions/allCardsTargeted와 실제 문장을 함께 읽는다. 작은 구간은 전체 이야기의 일부를 제외하는 설정이 아니다.

기본 모델은 Editor와 Windows x64 Mono 모두 Gemma 3 4B IT QAT Q4_0이다. Qwen은 별도 비교용이며 Tests/LocalStoryBenchmark/Models에 보관한다. 모델 가중치를 학습시키는 기능은 없다.

## 현재 v7: 실패 결과 플레이 확인

전체 흐름은 대화 종료 → 실제 만남/선택/아이템/해금 상태 고정 → 방문한 이야기별 큐 → Explanation 단락 계획 → Gemma 구간 생성 → 검사/실패 구간 교정 → 합본 저장 → 다음 회차 새 만남에서 읽기다. 이야기 여러 개를 등록하는 로그와 한 이야기 안의 여러 단락 요청 로그를 구분한다. 회차를 넘길 때까지 기다려야 생성이 시작되는 구조는 아니다.

사용자 요청에 따라 문구 검사 실패를 게임에서 볼 수 있게 바꿨다. 교정 후에도 실패한 문장은 그대로 합본에 넣고 다음 구간을 생성한다. 저장 상태는 Ready와 구분한 ReviewRequired이고, 검증 결과 valid/correctionValid를 성공으로 바꾸지 않는다. 카드 타입, 화자 ID, choiceID, 분기, 보상, 아이템 효과는 원문을 복제한다. JSON 형식이 맞지 않는 부분은 원문을 유지하고 그 범위와 이유를 기록한다.

확인 순서:

1. 세이브 초기화 없이 이어하기로 진행한다. 이미 저장된 실패 결과는 실행 시 다시 읽어 다음 회차용으로 반영한다.
2. 다음 회차의 새 로키 만남에서 검사에 실패한 문구도 표시되는지 확인한다. 현재 열어 둔 대화는 snapshot을 유지한다. Console의 `[검사 실패본 사용]`에는 해당 이야기와 실패 이유가 나온다.
3. 마지막 질문에서 `loki_question_god` 같은 내부 ID가 실제 선택지에 보일 수 있다. 이번 변경은 그 오류를 관찰하기 위한 반영이며, 그 버튼을 누르면 원래 해당 choiceID의 분기로 이어져야 한다.
4. 생성 큐 Queue.json의 status=ReviewRequired, 같은 NPC Story.json의 reviewRequired=true/validationError를 확인한다. 실패는 성공으로 위장하지 않는다.
5. ‘이야기 LLM 테스트 → 저장 결과 보기’에서 실패 결과를 열면 ‘검사 실패 문구 포함’ 표시, 실패 범위와 원출력을 읽을 수 있다. 수동 적용도 가능하며 구조가 깨진 JSON은 그대로 동작 데이터로 적용하지 않는다.

격리 검사 701개가 통과했다. 실패본 검사 41개에는 모든 구간 계속 생성, 실패 상태 보존, 실제 2회차 로더 표시, 행동/선택 연결 보존, 취소·재개, 기존 Failed 반영, 수동 적용 보호, 복원 상태, 깨진 JSON의 원문 유지, 회차 종료 경로의 상태 집계가 포함된다. 다른 검사는 모듈 263, 테스트 경로 20, 선택 연결 77, 큐 62, 구간 50, 결과 호환 16, 선택 설명 82, 저장 80, 실패 문구/깨진 JSON 10이다.

실제 기존 실패 결과 3개를 반영했다: 로키 도입, 표식 비밀 설명, 표식 대화. 도입은 실제 사용자 실행 기록에서 119.3초, 비밀 설명은 20.0초, 표식 대화는 73.6초였다. 표식 대화는 중단 전 생성된 1/2카드만 반영하고 나머지는 원문을 유지한다. 실제 Profile/Runs/Edit JSON 9개는 전후 해시가 같았고, 세 실패본 모두 실제 2회차 게임 로더로 읽어 원문 행동 연결을 비교했다. 신규 모델 추론으로 품질 개선을 검증한 결과는 아니다.

최종 Windows 빌드 `build_f4767390f21e`는 56.9초, 오류 0개·경고 2개로 성공했다. `Builds/GemmaWindows/OneWay.exe`와 함께 배포되는 게임 DLL에서 실패본 재생·기존 실패 기록 반영 기능을 확인했다. 모델은 Gemma만 포함한다. Editor와 실행 파일이 같은 사용자 저장 경로를 사용하므로 둘을 동시에 플레이하며 저장하지 않는다.

## 이전 v6: 2026-10-26 테스트 창 통합 확인

아래는 이전 검증 기록이다. 실패본 거부 정책은 위 v7의 확인용 반영 방식으로 바뀌었다.

게임 자동 생성과 달리 테스트 창의 ‘로컬 생성’에는 9카드 전체 초안 뒤 전체 교정을 실행하는 옛 경로가 남아 있었다. 이를 공통 단락 생성기로 통합했다. 4단락·6작성 요청·9카드·8선택지를 모두 대상으로 하며, 성공 구간의 일괄 재교정은 하지 않는다. 단계 표시는 단락/요청/카드 번호를 구분한다. 정책 버전을 v6으로 올려 이전 검사로 만든 작업과 생성본을 그대로 재사용하지 않도록 했다.

격리 회귀 검사 570개가 통과했다: 테스트 창 20, 프롬프트 모듈 263, 기존 선택 연결 77, 생성 큐 62, 구간/재개 50, 결과 호환 16, 선택 설명/반복 배분 82. Windows의 짧은 파일 잠금으로 결과 저장이 실패하는 경우도 확인해 원자적 교체를 최대 3회 재시도한다. 사용자 원문·실제 Saves·수정본·애니메이션 데이터는 이 검사의 수정 대상이 아니다.

같은 테스트 진입점으로 실제 Gemma 전체 장면을 두 번 측정했다. 환경은 i5-10400, RAM 약 16GB, GTX 1660 SUPER 6GB다. 고정 seed 12345, 기억/수정 예시 없는 입력이며 플레이 중 프레임 영향을 포함한 측정은 아니다.

| 상태 | 총 시간 | 파일 검증 | 모델 적재 | 추론 합계 | 호출/결과 |
|---|---:|---:|---:|---:|---|
| 모델 미적재 상태 | 81.5초 | 12.7초 | 7.0초 | 61.4초 | 작성 6회 + 실패 구간 교정 1회, 거부 |
| 같은 모델 재사용 | 61.4초 | 0.001초 | 0초 | 61.0초 | 작성 6회 + 실패 구간 교정 1회, 거부 |

첫 1~3카드 요청은 각각 29.7초/9.6초였다. 두 실행 모두 9카드를 대상으로 호출했지만 마지막 선택지에 내부 choiceID가 출력되어 교정 후에도 거부됐다. 지식 반환 대사에서도 화자가 뒤바뀐 의미 오류가 남았다. 이 수치는 유효한 이야기 완성 시간이 아니며, 속도·문장 품질 해결 완료를 뜻하지 않는다. 기존 검사에서 이런 ID 출력을 통과시켰던 결함은 막았다. 프롬프트 끝의 추가 지시 실험은 앞 대사 반복을 늘려 제외했다.

측정 결과는 `tmp/ParagraphTiming/Result.json`, 당시 실제 원출력과 마지막 입력은 그 파일의 reportPath에 있다. 실제 Saves JSON 2개는 측정 전후 동일했다. 전체 프롬프트 조립 예시는 `Docs/CurrentLLMPrompt.md`에 다시 내보냈다. 내부 ID 출력과 의미 혼동을 줄일 프롬프트 정리가 다음 과제다.

최종 Windows 빌드 `build_f3d93a7a9d3a`는 약 75초, 오류 0개·경고 3개로 완료됐다. Gemma 단독 배포 설정 검사 9개도 통과했다. Player 자동화 설정, 미사용 colour 변수, 기존 AangFace 셰이더 키워드 경고가 남았다. 실행 파일은 `Builds/GemmaWindows/OneWay.exe`다.

재측정은 Play를 중지하고 다음을 실행한다. 임시 저장소를 사용하며 모델 적재·네이티브 생성 때문에 수 분이 걸릴 수 있다.

```powershell
unity command run_script --file AgentScripts/StoryParagraphTimingCheck.cs --entry StoryParagraphTimingCheck.NativeBenchmark --timeout_ms 500000 --timeout 510 --result-only --format json
```

사용자 확인은 새로 컴파일된 Editor 또는 갱신한 빌드에서 진행한다. ‘이야기 LLM 테스트’ → Gemma → 로키 도입 → ‘로컬 생성’을 누른다. 저장된 옛 결과 조회와 새 생성을 구분한다. 진행 표시가 1~6 요청을 가리키는지, 마지막 선택지의 실패 교정이 앞의 성공 구간을 다시 생성하지 않는지 확인한다. Result JSON의 paragraphsPlanned=4, sectionsGenerated=6, diagnostics.sectionTimings 6행과 각 카드 범위를 확인한다. 실패 결과를 게임에 적용할 수 없어야 한다.

## 2026-10-25 검증 결과

단락/프롬프트 모듈화 이후 총 772개 격리 검사가 통과했다: 새 모듈 263, 구간 50, 선택 의도 82, 생성 큐 62, 이야기/아이템 218, 저장 구조 23, 생성 상태 15, Excel 16, 기존 결과 호환 16, 인벤토리 27이다. 새 모듈은 현재 64개 이야기의 모든 카드를 확인하고 임시 새 챕터·NPC·해금을 추가해 C# 수정 없이 Excel과 프롬프트에 들어가는 것을 검증한다. 임시 콘텐츠는 제거한다. 기존 변환 원문 58묶음·103카드·52선택지 비교도 유지했다.

실제 Gemma 아이템 대화 검사는 2카드·4선택지 모두를 대상으로 실행했다. 설명 단락과 카드 종류를 명시한 후 약 34.7초, 초안 2회·교정 0회로 형식/연결 검사를 통과했다. 그중 파일 해시 확인 약 12.7초, 모델 적재 약 11.3초, 생성 약 10.5초였다. 전체 이야기의 속도 개선 수치가 아니다. 출력에는 화자에 어울리지 않는 질문이 남아 있어 문장 품질 검사는 별도로 필요하다. 형식 통과를 의미 정확성이나 완성된 이야기 품질로 해석하지 않는다.

실사용 Records 3개를 0/1 해금 상태가 있는 형식으로 갱신했다. 실제 진행 JSON 9개와 직접 수정본 6개는 바이트 비교에서 동일했다. 기존 Evidence는 0개이며 새로 생성하지 않았다.

최종 Windows x64 빌드 `build_e3791636d499`는 약 56초에 성공했으며 오류 0개·경고 2개였다. 경고는 Player용 Pipeline 자동화 설정 없음과 기존 미사용 colour 변수다. Gemma 3 4B IT QAT Q4_0 한 모델이 포함됐고 배포 고지를 확인했다. 마지막 Excel 숫자형 저장 수정도 포함했다.

### 사용자가 확인할 순서

1. `Docs/CurrentLLMPrompt.md`에서 `카드 1~3 / arrival`, `카드 9~9 / final_response`를 찾는다. 공통 규칙은 같고 단락·상황·대상·선택 ID는 달라야 한다. 이 파일은 원문을 앞선 대사로 사용한 조립 예시이며 실제 모델 출력이 아니다.
2. Play를 중지하고 Guide/Explanation의 `paragraphs[0].purpose`에 확인 가능한 문구를 넣는다. ExportReview를 다시 실행하면 앞 단락 입력에 해당 문구가 들어가야 한다. 확인 후 원래 문구로 돌린다. Story나 C#에 같은 내용을 추가할 필요가 없다.
3. Excel을 닫은 상태로 게임을 실행하거나 대화를 종료해 기록을 갱신한 뒤 Saves/Records/Chapter_00.xlsx의 해금규칙을 연다. 실제 해금 ID가 한 행씩 나오고 상태는 숫자 0/1이어야 한다. 현재 사용자 저장에서는 표식 비밀과 초기 NPC 만남들이 1, 다음 챕터 미진행 만남은 0이었다. 값을 수동 변경해 게임 해금을 부여하는 기능은 아니다.
4. 기존 아이템 테스트 순서대로 표식 획득→용도 질문→다음 회차로 진행한다. 표식과 비밀이 유지되고 이미 보유/이미 아는 경우의 Hidden 대화가 열려야 한다. 표식을 돌려주면 보유 조건은 사라져도 비밀 해금은 유지돼야 한다.
5. 이야기 LLM 테스트에서 Gemma로 표식 대화 또는 로키 도입을 생성한다. 도입은 4단락·6요청에 9카드·8선택지가 모두 포함돼야 한다. 생성된 질문을 NPC가 하는 말로 읽을 수 있는지, 플레이어 응답을 NPC가 말하지 않는지, 장난 전후 상태와 선택 의도가 맞는지 확인한다. 거부된 결과는 게임에 적용되면 안 된다.

Windows 빌드: `Builds/GemmaWindows/OneWay.exe`. Editor와 동일한 Gemma 모델과 Resources의 JSON 설정을 사용한다. 플레이 저장 초기화는 필요하지 않다. Editor와 빌드가 같은 실제 저장 경로를 사용하므로 동시에 실행하며 저장하지 않는다.

### 이전 Excel 정리 검증

이번 정리에서는 Excel/임시 콘텐츠 16개, Evidence 제거·기존 결과/수정본 호환 16개, 선택 의도/반복 배분 82개, 생성 큐 62개, 구간 생성 50개, 저장 구조 23개, 아이템·해금·원문 로딩 218개, 생성 상태 JSON 17개로 총 484개 검사가 통과했다. 가짜 추론으로 데이터 흐름을 확인했으며 교정 정책·모델 설정·추론 속도 개선은 이번 작업에 포함하지 않았다.

실사용 Records 3개를 갱신하고 기존 결과 6개를 Evidence 없이 읽도록 옮겼다. Evidence.zip은 1개에서 0개가 됐으며 실제 플레이 저장 파일 9개와 수정본 6개는 바이트 비교에서 동일했다. 임시 NPC·두 청크의 이야기·해금·보상 규칙은 자동 반영 검사 후 제거했다.

정리한 코드를 포함한 Windows x64 Mono 개발 빌드도 갱신했다. 빌드는 오류 0개·경고 3개로 완료됐다. 애니메이션·Animator·씬 데이터는 이 정리 작업의 수정 대상이 아니다.

이전 Gemma 실행 파일 검사에서는 모델 재사용과 표식 장면 2구간 생성을 확인했다. 약 44초라는 실행 검사 시간과 약 10.4초의 최초 모델 적재 시간은 모든 이야기의 생성 시간이나 속도 개선 결과를 의미하지 않는다.
