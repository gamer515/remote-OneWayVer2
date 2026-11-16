# Decision 직접 LLM 통신 기틀

이 폴더만 추가/수정했다. 기존 `DirectLlmRoundRobinTester.cs`는 다른 프로젝트의 참고용으로 유지한다.
**씬이나 기존 생성 큐에는 아직 연결하지 않았다.** 이 코드가 존재하는 것만으로 HTTP 요청을 보내지 않는다.

## 구성

| 파일 | 역할 |
| --- | --- |
| `Runtime/DirectLlmSettings.cs` | Inspector 설정 에셋, 공급자 순서/모델/활성화/시간 제한, 환경 변수 인증 |
| `Runtime/DirectLlmClient.cs` | 요청별 순환, 실패 시 다음 공급자, 취소, 비밀정보 없는 시도 기록 |
| `Runtime/DirectLlmProtocol.cs` | Gemini 및 OpenAI 호환 요청/응답 변환, `cards` 구조 검사 |
| `Runtime/DirectLlmTransport.cs` | UnityWebRequest 전송, 테스트용 전송 인터페이스 |
| `Runtime/DirectStoryGenerator.cs` | 기존 `LocalStorySections`와 연결하는 어댑터 |
| `Verification~/VerifyDirectLlm.cs` | 실제 HTTP/키 없이 실행하는 검증 스크립트 |

Namespace: `OneWay.Decision.DirectLlm`. 기존 Newtonsoft.Json, UnityWebRequest를 사용하며 패키지 추가는 없다.

## 설정

Project 창에서 이 폴더를 선택하고 **Create > Decision > Direct LLM Settings**로 설정 에셋을 만든다.
`endpoints`의 순서, enabled, model을 바꿀 수 있다. 설정 변경 후에는 클라이언트를 새로 만든다.
기본 모델 ID는 예시이며 실제 사용 계정의 접근 가능 모델을 확인한다.

키는 에셋에 넣지 않는다. 실행 프로세스 환경 변수에서 읽으며 Windows에서는 사용자 환경 변수도 확인한다.

| 공급자 | 필요한 환경 변수 |
| --- | --- |
| Gemini | `GEMINI_API_KEY` |
| Groq | `GROQ_API_KEY` |
| Cloudflare | `CLOUDFLARE_API_TOKEN`, `CLOUDFLARE_ACCOUNT_ID` |

키가 없거나 모델 설정이 잘못되면 해당 공급자를 실패 처리하고 다음으로 넘어간다.
Account ID는 32자리 16진수다. 런타임의 별도 키 관리가 필요하면 클라이언트 생성자의 credentials delegate를 주입한다.
팀 공용 비밀 키를 배포용 Unity 클라이언트에 포함하지 않는다. 현재 구성은 개발 환경의 직접 통신 기틀이다.

## 통신 동작

- 기본 순서: Gemini → Groq → Cloudflare. 활성 공급자만 사용한다.
- 순환 단위는 **HTTP 요청 하나(기존 이야기의 한 구간)**다. 새 요청마다 시작 공급자를 한 칸 이동한다.
- 실패하면 그 요청에서 남은 공급자를 순서대로 한 번씩 시도한다. 성공하면 나머지는 호출하지 않는다.
- `roundRobin=false`이면 매 요청을 첫 활성 공급자에서 시작한다. 실패 시 전환은 유지한다.
- 기본 공급자당 30초. 각 공급자 최대 한 번, 자동 반복 없음. 전체 이야기 제한 시간은 기존 파이프라인이 관리한다.
- HTTP 오류, 응답 잘림/거절, 잘못된 JSON/cards, 어댑터의 원문 검증 실패도 공급자 전환 대상이다.
- 호출자의 취소는 예외로 즉시 전파한다. 취소 후 다음 공급자를 호출하지 않는다.
- 한 클라이언트/어댑터 인스턴스는 순차 호출한다. 중복 호출은 거절하며 대기열을 중복 생성하지 않는다.
- 하나의 cards 응답을 여러 공급자의 응답과 합치지 않는다. 서로 다른 이야기 구간은 다른 공급자가 작성할 수 있다.
- 스트리밍은 사용하지 않는다. Gemini/Groq에 seed를 전달하지만 재현성은 보장하지 않는다. Cloudflare에는 seed를 보내지 않는다.

Unity 메인 스레드에서 호출하고 `Task.Run`으로 감싸지 않는다. 씬 전환/종료 때 호출자가 CancellationToken을 취소한다.
어댑터는 기존 `LocalStoryLifetime`에도 등록되므로 기존 에디터 종료/리로드 취소 흐름에 참여한다.

## 기존 게임 연결 예시 — 팀원이 연결할 지점

서비스가 시작될 때 한 번 생성하여 재사용한다. 매 구간 새로 생성하면 순환 순서가 초기화된다.

```csharp
using OneWay.Decision.DirectLlm;

// 설정 에셋을 전달하거나 new DirectLlmOptions()를 사용할 수 있다.
var direct = new DirectStoryGenerator(settings.CreateClient());

// 기존 LocalStoryRuntimeService.Pump의 generate delegate 교체 지점.
await LocalStoryDialogueQueue.Process(
    storage, job, direct.GenerateRuntimeAsync, cancellationToken);
```

위 연결은 **외부 직접 통신만 사용**한다. 기존 `StoryGenerationRace.GenerateRuntime`를 그대로 호출하면
OMNI Route도 계속 실행되므로 우회해야 한다. 내장 모델과의 동시 생성을 유지할 경우 팀원이
`StoryGenerationRace.FirstApplicable`의 외부 후보를 직접 통신 어댑터로 연결하고 후보별 report/checkpoint를 분리한다.
현재 작업에서는 기존 파일을 수정하지 않았다.

어댑터는 기존 `LocalStorySections.Generate`에 전송 함수를 주입한다. 기존의 단락 분할, 프롬프트 모듈,
이전 대사 문맥, 수정 요청, 구간 체크포인트가 유지된다. 기존 큐가 요청 스냅샷을 만들고 기존 저장/로더가
다음 회차에 반영한다. 이 폴더의 코드 자체는 Resources, 세이브, 씬을 쓰지 않는다.

현재 공통 `LocalStoryTestModels.ModelFor`는 알 수 없는 이름을 Qwen 메시지 포맷터로 보내며 이 포맷터는
system/user 메시지를 그대로 통과시킨다. 어댑터의 `Direct HTTP` 라벨은 **이 포맷터만** 이용한다.
Qwen/Gemma 추론은 호출하지 않는다. 장기적으로 팀원은 명시적인 Direct 모델 구분을 공통 코드에 추가하는 것이 좋다.
공통 `LocalStoryLog.Source` 역시 현재 OmniRoute 이름만 외부 통신으로 분류하므로 Direct 라벨 분류도 연결 시 수정해야 한다.
실제 공급자/모델은 결과의 `backend`에 `Direct / 공급자 / 모델`로 남는다.

## 독립 호출 계약

```csharp
var client = new DirectLlmClient(new DirectLlmOptions());
// messages는 기존 LocalStoryRewrite.Messages / SectionMessages가 반환하는 JSON 문자열.
var result = await client.GenerateAsync(messages, maxTokens, seed, token,
    json => LocalStoryRewrite.TryBuild(json, originalSection, memory, out _, out _));
LocalStoryModelOutput output = DirectStoryGenerator.ToModelOutput(result);
```

입력은 `[{"role":"system","content":"..."},{"role":"user","content":"..."}]`다.
Groq/Cloudflare에는 그대로, Gemini에는 systemInstruction/contents로 같은 내용을 전달한다.
별도의 단어 치환 프롬프트를 추가하지 않는다. 반환하는 `CardsJson` 예시:

```json
{"cards":[{"index":0,"text":"이 길을 지나려면 네 {무력}을 보여라.","options":[]}]}
```

카드 index는 구간 내부 0부터 순서대로다. JSON 속성 순서는 기존 엄격한 파서가 읽도록 정규화하지만 대사 내용은 바꾸지 않는다.
코드 펜스 하나는 제거할 수 있다. 중복/추가 필드, 빈 문구, 순번 오류, 길이 초과, 잘린 응답은 거절한다.
낮은 수준의 `GenerateAsync`는 원문을 모르므로 호출자가 acceptCards를 전달해야 원문과 비교할 수 있다.
게임 어댑터는 이를 항상 전달하여 카드/선택지 수, `{고정 문자열}`, 숫자와 기존 원문 검사를 적용한다.
테스트 스크립트의 `replacements` 형식과 중괄호 내부 변경 규칙은 사용하지 않는다.

기존 공통 파이프라인의 `allowFailedOutput`, 교정 설정은 그대로다. 어댑터가 원문 검사를 통과시킨 이후에도
상황/예시 복사 검사에서 실패한 문구는 기존 정책에 따라 ReviewRequired가 될 수 있다. 이 어댑터를 연결하는 것만으로
프로젝트 전체를 엄격한 실패 폐기 모드로 바꾸지는 않는다.

## 검증

Unity Pipeline의 `run_script`로 다음 파일의 `VerifyDirectLlm.Run`을 실행한다.
외부 HTTP 요청과 실제 인증키를 사용하지 않는다. 검증용 폴더는 Unity가 자동 컴파일/임포트하지 않는 `~` 폴더다.

```text
Assets/Decision_YYS/ZLLMControl_HJH/Verification~/VerifyDirectLlm.cs
```

공식 요청 형식 참고:
- [Gemini GenerateContent](https://ai.google.dev/api/generate-content)
- [Groq Chat Completions](https://console.groq.com/docs/api-reference)
- [Groq GPT-OSS reasoning 설정](https://console.groq.com/docs/reasoning)
- [Cloudflare OpenAI 호환 API](https://developers.cloudflare.com/workers-ai/configuration/open-ai-compatibility/)

실계정의 인증, 모델 제공 여부, 한도, 한국어 생성 품질은 팀원의 실제 API 통합 테스트로 확인해야 한다.
