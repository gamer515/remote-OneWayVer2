# JSON 프롬프트 모듈 변경 검증

이 문서는 앞선 모듈 변경의 비교 기록이다. 이후 사용자 요청으로 실패본 재생을 켜고 완료 결과 재생성을 없앴다. 현재 정책은 [StorageLifecycle.md](StorageLifecycle.md)를 참조한다.

2026-10-26. 프로젝트 소스 변경과 Unity Editor 컴파일·격리 검증 결과다. 기존 Windows 실행 파일은 이 작업에서 재빌드하지 않았다.

## 구현

- 집필 규칙·예시는 PromptPolicy가 참조하는 11개 JSON 모듈로 분리했다. C#에는 NPC·장면별 집필 지시나 새 예시 대사를 넣지 않았다.
- Characters의 인물 설정과 Explanation의 상황·주체·상태·선택 intent를 읽어 합친다. 로키 도입의 주체와 지식 전후 상태는 Explanation에 작성했다.
- 일반 요청에는 난독·선택지·교정 규칙을 넣지 않는다. 기본 예시는 JSON에서 읽으며 과거 Edit 자동 검색은 껐다.
- 실행 선택 ID를 집필 입력에서 제거하고 요청 내 번호와 의미로 매핑한다. 다른 카드의 식별자도 검증한다.
- 예시 문구의 정확한 복사를 추가 검사한다. 검사 실패본의 자동 적용·자동 승격은 기본적으로 껐다. 명시적 진단 재생은 유지한다.
- 모듈·Explanation·인물 변경은 정책 해시를 바꾼다. 모델 세션과 프롬프트 캐시는 유지하며 추론 초기화 설정은 JSON으로 분리했다.

## 실제 모델 비교

GTX 1660 SUPER 6GB, Gemma 3 4B IT QAT Q4_0, 로키 도입 9카드·8선택지, seed=12345. 매 비교 시작 전에 모델 세션을 해제했다. OS 파일 캐시까지 비운 비교는 아니다. 격리 보고서의 allowFailedOutput=true는 실패 문구까지 관찰하기 위한 것이며 실제 게임의 기본 정책은 false다.

| 조건 | 전체 시간 | 작성 요청 | 교정 요청 | 자동 검사 |
|---|---:|---:|---:|---|
| 기존 | 90.9초 | 6 | 1 | 실패 |
| 모듈 1차 / GPU 24 | 83.5초 | 6 | 1 | 실패 |
| 대상 순서 정리 / GPU 상한 99 | 60.3초 | 6 | 0 | 통과 |
| 최종 / 예시 복사 차단 포함 | 69.9초 | 6 | 1 | 실패 |

최종은 기존 대비 약 23% 짧았다. 입력과 GPU 적재 설정을 함께 바꾼 단일 장면·시드 비교이므로 개선 원인을 분리하거나 다른 장비·장면의 속도를 보장하는 수치는 아니다. 작성 프롬프트 content 합계는 기존 18,538자, 최종 18,182자다. 이는 토큰 수가 아니다. GPU 적재 상한 99는 실제 레이어 수가 아니라 런타임이 가능한 레이어를 적재하는 상한이다. 실행 중 관찰한 GPU 전체 사용량은 약 4.9GB였으며 실제 게임 플레이 중 GPU 경합/FPS는 별도로 측정하지 않았다.

## 품질 판정: 미달

ID 노출은 새 비교 결과에서 없어졌다. 그러나 실제 문장을 읽으면 아래 오류가 남았다.

- 지식을 빼앗는 카드가 여행자의 방문 이유를 묻는 말로 바뀌어 핵심 사건 표현이 빠졌다.
- 난독 카드에서 로키가 “자네 말이 잘 안 들려오네”라고 말해 영향을 받는 주체가 뒤집혔다. 정상 문장에 가까운 표현도 남았다.
- 지식 반환 뒤 상태와 맞지 않는 대사가 나왔다.
- 마지막 NPC 질문이 플레이어 관점의 놀람으로 바뀌었다.
- 모욕 선택지에 다른 장면의 예시 “잠깐 들어가 봐도 될까요?”가 들어갔다. 새 example-copy 검사가 이를 차단했지만 교정도 해결하지 못했다.

따라서 자동 형식 검사 통과는 의미·문장 품질 합격이 아니다. 최종 생성 결과는 실패로 기록되며 기본 일반 적용에서는 원문을 사용한다. 정확한 예시 복사만 차단하며 변형 복사·주체 혼동·사건 누락을 완전히 감지하지는 못한다.

## 회귀 검사

Unity 컴파일 오류 0. 아래 670개 격리 검사를 통과했다.

| 검사 | 수 |
|---|---:|
| PromptCompositionCheck.Regression | 21 |
| PromptCompositionCheck.StrictRuntime | 5 |
| StoryPromptModuleCheck.Regression | 263 |
| LocalStoryChoiceOrderCheck.Regression | 77 |
| ChoiceExplanationCheck.Regression | 82 |
| StoryParagraphTimingCheck.Regression | 20 |
| LocalStoryNativePathCheck.Run | 31 |
| NarrativeStoreCheck.SectionsRegression | 50 |
| FailedStoryPlaybackCheck.Regression | 41 |
| NarrativeStoreCheck.Regression | 80 |

기존 ID 전달을 기대하던 검사는 ID 비노출과 의미 매핑을 확인하도록 갱신했다. 기존 수정본 참고와 실패 재생 검사는 해당 JSON 옵션을 메모리에서 명시적으로 켜고 종료 시 복원해, 선택적 기능의 유지도 검사했다. 테스트는 플레이어 저장소에 적용하지 않았다.

원출력: tmp/PromptComposition/before.json, after.json, final.json, verified.json. 조립 검토본: Docs/CurrentLLMPrompt.md. 작성법: Docs/PromptModules.md.

## 다음 단계

프롬프트 규칙과 무관한 예시를 계속 늘리는 것만으로 해결할 근거는 없다. 주체·상태·선택 의도를 보존한 사람 승인 입출력 자료를 모으고 학습에 사용하지 않은 장면을 평가용으로 분리한 뒤, LoRA/QLoRA 실험과 기존 모델 비교를 진행하는 것이 다음 후보이다. 학습 실행이나 모델 다운로드는 이 작업에 포함하지 않았다. 학습도 의미 정확도와 속도 개선을 보장하지 않는다.
