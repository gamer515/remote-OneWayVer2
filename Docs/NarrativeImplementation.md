# 이야기 데이터 실행 구조

집필 규칙과 예시는 [JSON 모듈](PromptModules.md)에서 조건별로 조립한다. 사용자 요청으로 실패본은 다음 회차에서 확인하고, 완료 결과는 회차 전환이나 정책 변경으로 다시 생성하지 않는다. 현재 저장 규칙은 [StorageLifecycle.md](StorageLifecycle.md)를 참조한다.

현재 작성법은 [ChapterStoryAuthoring.md](ChapterStoryAuthoring.md), 확인 절차는 [LocalStoryTestWorkflow.md](LocalStoryTestWorkflow.md)를 참조한다.

## 입력과 실행

ChapterContentCatalog는 Chapters/Catalog의 안정적인 논리 경로를 storyFile/explanationFile로 지정한 NPC 메인·선택지별 Branches·Hidden 파일에 연결한다. 카드 속성을 검사하고 ID별 설명과 Terrain의 실행 규칙을 결합해 기존 런타임 모델로 제공한다. 문서의 ID/eventIDstable/speakID/Text/relationship 이름을 런타임의 기존 이름으로 변환한다. JSON을 바꾸면 Editor의 카탈로그 캐시가 갱신된다.

TerrainRepository는 챕터 Terrain/Transform의 episode 데이터를 읽는다. EncounterContentRepository는 NPC/청크의 이야기, 능력치 원문 변형, 해당 회차 생성 변경분을 선택한다. 열려 있는 대화는 snapshot으로 복원하며 새 생성 결과로 중간 교체하지 않는다.

NarrativeInteractions는 선택 ID에 연결된 기존 표식 지급·비밀 해금·회수와 후속 분기를 실행한다. 아이템과 해금 조건은 Story 카드의 추가 속성이 아니라 별도 규칙에 있다. 공개·숨은 설정은 Characters를 참조한다.

## 생성 시점

실제 대화 종료 → LocalStoryDialogueQueue → LocalStoryRuntimeService → LocalStorySections → 내장 Gemma → 검사·구간 교정 → GeneratedContent 저장 순서다. 해당 원문 장면 전체의 모든 카드·선택지를 짧은 구간으로 처리한다. 실패 구간 교정과 완료 구간 체크포인트 재사용은 유지한다. 현재 v7은 실패 문구도 확인할 수 있도록 ReviewRequired 상태로 반영한다. 실패 구간 뒤의 단락도 계속 생성하며 valid/correctionValid는 실패 상태를 유지한다.

LocalStoryPrompt는 공통 PromptPolicy, Characters의 기본/조건부 설정, Explanation의 단락·목적·상황·규칙을 읽는 공통 조립기다. 단락별 생성량을 적용하고 게임 행동·난독 전환·선택지 경계를 보호한다. 로키 9카드는 4단락·6요청이다. 각 요청은 공통 규칙과 인물/장면을 앞에 고정 배치하며 캐시 재사용을 요청한다. NPC 이름이나 챕터별 집필 문장을 C#에서 선택하지 않는다. 전체 비교 생성과 실패 구간 교정도 같은 조립기를 쓴다. 정책·설명·인물 수정은 생성 정책 해시에 반영되어 이전 체크포인트를 그대로 재사용하지 않는다.

2026-10-26에는 테스트 창의 남아 있던 전체 장면 생성·전체 재교정 경로도 제거했다. ‘로컬 생성’ 버튼은 GenerateSections → LocalStoryRuntimePipeline.Infer → LocalStorySections로 자동 생성과 같은 경로를 호출한다. 단락 수·작성 요청 수·카드 범위를 따로 표시하며, 성공한 구간은 다시 교정하지 않는다. 대사에 현재 카드의 이벤트/선택 ID를 출력하면 검사 실패로 기록하지만, 사건 의미 전체를 자동으로 판정하는 검사는 아니다.

이후 사용자 요청으로 검사 실패와 표시 가능 여부를 분리했다. TryBuildForReview는 JSON 카드/인덱스/선택지 수/빈 문구를 검사하고 원문의 실행 연결에 text/options만 덧씌운다. 내부 ID, 숫자 변화, 난독 복원, 중복 등 문구 검사 실패는 원출력 그대로 표시한다. 구조가 깨진 구간은 원문을 유지하고 그 사실을 기록한다. 모든 구간이 표시 불가이면 게임 데이터로 적용하지 않는다. 다음 회차 새 대화에서 실패본 사용 로그를 남기며, 이미 열려 있는 대화는 기존 snapshot을 유지한다.

같은 회차 재회 자동 생성은 이번 구조 정리에서 새로 추가하지 않았다. 현재 자동 생성은 실제 만남이 끝난 뒤 다음 회차를 준비한다. 기존 CurrentRun 입력과 Dream 입력은 구분하고 DreamMemoryRules는 유지한다. 가상 테스트를 실제 플레이 기록으로 기록하지 않는다.

## 저장

SaveIOService.ChapterLayout은 NPC별 Story 변경분과 Features, 회차별 Queue를 관리한다. Source/content hash로 원문 버전과 무결성을 확인한다. 수동 검토·비활성화·이전 검토본 복원과 실제 플레이 기록 보호를 유지한다. 원문 변경으로 유효하지 않은 변경분은 원래 데이터를 보존하고 해당 장면만 적용을 보류한다.

ProgressDocument는 진행·조우·플레이어·세계·효과 상태를 구분한다. 중복되는 현재 대화 기록은 누적 기록 참조로 저장한다. History를 먼저 기록하고 이전 체크포인트와 일치하는 백업을 유지한다. 기록 저장 후 Progress 쓰기가 중단돼도 직전 기록을 복원한다. 가방 밖 아이템의 개체 ID·보드 상대 위치·회전과 능력 배분 상태를 보존한다.

LocalStoryTestArchive.Grouped는 결과 JSON과 수정본을 회차/챕터/에피소드/NPC별로 저장한다. Result만으로 조회·수정본 적용이 가능하며 Evidence.zip은 생성하지 않는다. 당시 원문과 AI 원출력·마지막 실제 프롬프트를 보존하고, 구간별 전체 요청과 실제 플레이 전체 기록의 중복 사본은 제거한다. 실제 기록은 Runs에서 다시 읽는다. 자동 결과는 최근 세 개를 유지하며 사람이 수정한 내용은 정리하지 않는다.

Result의 diagnostics.sectionTimings에는 각 요청의 카드 범위·작성/교정 시간·검증/적재/추론 시간만 작게 남긴다. Windows의 짧은 파일 잠금에는 원자적 교체를 20/40/80ms 뒤 재시도하고 계속 실패하면 적용을 보류한다.

실패본에는 reviewRequired/validationError를 같은 Story 항목에 저장한다. 같은 Result에 reviewError, 생성 문구 카드 수, 요청별 reviewApplied/sourceFallback/오류를 기록하며 파일을 추가로 만들지 않는다. ApplySavedFailedOutputs는 기존 Failed 결과 중 원문 연결이 일치하는 출력도 반영한다. 수동 적용/직접 수정/비활성 콘텐츠는 기존 보호 규칙을 따른다. 표시할 수 없는 결과는 재시도 확인 여부를 남겨 같은 실패를 계속 처리하지 않는다.

LocalStoryNotebook.Chapters는 NPC설정·해금규칙·아이템보상의 세 시트를 관리한다. 역할·성격·플레이기록·생성현황을 중복 작성하지 않는다. 선택_ID는 choiceID를 유지하고 현재상황은 조건 종류별 줄바꿈으로 표시한다. JSON을 읽는 공통 BookData가 템플릿과 실행 시 새 콘텐츠 행 추가를 모두 담당한다. 기존 이름·자동생성·모델·능력치는 보존한다. 내용이 같으면 파일을 다시 쓰지 않으며 생성 구간별 Excel 갱신도 제거했다. Excel이 잠긴 경우 JSON 진행을 유지하고 갱신만 보류한다.

StartNextRun은 소유 아이템과 꺼내 둔 개체의 위치·회전, 해금을 복사하고 새로운 여정 진행도를 만든다. 로키의 상호작용은 소유·미소유와 해금·미해금, 현재 회차 지급 기록을 구분해 보통 지급 / 이미 소유 / 비밀을 이미 앎의 실제 분기를 선택한다. 해금에 따른 플레이 선택과 NPC 반응은 Hidden에서 작성하고 LLM의 즉흥 판단에 맡기지 않는다.

해금규칙 시트는 실제 해금 ID별 0/1 상태와 설정 참조를 표시한다. 요청 준비 시 Excel의 상태를 캐시하고 실제 획득 기록과 대조해 허용 ID 스냅샷을 고정한다. 프롬프트는 이 스냅샷과 소유 아이템 ID로 Characters.hidden을 선별한다. 같은 내용을 publicFacts와 revealedPast 양쪽에 반복하지 않는다. 상태 원본은 기존 진행 JSON이며 Excel 수동 편집으로 미획득 해금을 새로 부여하지 않는다.

## 이번 단계의 한계

단락과 공통 입력의 재사용 구조를 적용했지만 전체 이야기의 생성 속도와 문학적 품질을 보장하는 것은 아니다. 보스 약점·보상 변화의 새 규칙과 메인 메뉴 전체 기록 화면은 별도 게임 기능이다. 단락 생성량과 기기별 추론 설정의 최적값은 추가 측정이 필요하다.
