# 로컬 이야기 비교 모델 조사 — 2026-10-09

## 현재 조건

확인된 PC: i5-10400, RAM 약 16GB, GTX 1660 SUPER. Unity 보고 GPU 메모리 약 5966MiB. 게임과 Editor도 메모리를 쓰므로 모델 파일 크기만 보고 GPU에 들어간다고 판단하지 않는다.

현재 모델: Qwen3-4B-Instruct-2507 Q4_K_M, 파일 2,497,280,736바이트. LlamaLib 2.0.5를 Unity 내부에서 호출하며 외부 추론 서버는 없다.

아래 후보는 조사 시점의 제안이다. 이후 사용자가 공식 Gemma 파일을 다운로드해 제공했고 동일 입력 8개 비교를 완료했다. 게임 모델은 바꾸지 않았다. 실제 결과와 경로 주의점은 [고정 시험 문서](LocalStoryBenchmark.md)의 Gemma 비교 절을 확인한다. 한국어 품질 순위가 일반적으로 입증된 것은 아니다.

## 우선 비교 후보: Gemma 3 4B IT QAT Q4_0

Google이 제공하는 4B GGUF이고 게시된 파일 크기는 약 3.16GB다. 같은 4B 규모의 다른 모델 계열이므로 현재 Qwen과 다른 오류 양상을 비교할 후보로 잡는다. 한국어 게임 대사가 더 좋다고 보장할 수 없으며 실제 고정 시험으로 판단해야 한다. [Google 공식 GGUF](https://huggingface.co/google/gemma-3-4b-it-qat-q4_0-gguf)

텍스트만 사용한다. 이미지 입력 기능이나 이미지용 추가 파일을 설치할 이유는 없다. 기존 LlamaLib의 실제 모델 로딩·채팅 템플릿·JSON 문법 제약이 작동하는지 짧은 호환성 시험이 먼저 필요하다.

공식 다운로드에는 Hugging Face 로그인과 조건 수락·연락처 정보 공유 동의가 필요하다. 사용자 대신 이를 수락하거나 계정 정보를 전송하지 않는다. [공식 접근 조건](https://huggingface.co/google/gemma-3-4b-it-qat-q4_0-gguf#you-need-to-agree-to-share-your-contact-information-to-access-this-model)

Gemma는 Apache 라이선스가 아니라 별도 이용 조건을 적용한다. 게임에 모델을 배포할 경우 약관 사본·공지·사용 제한 등의 배포 조건을 검토해야 한다. 법률 적합성을 보증하는 문서가 아니다. [Google 이용 조건](https://ai.google.dev/gemma/terms)

## 대안: Qwen3 8B Q4_K_M

공식 GGUF 파일 약 5.03GB, Qwen3 아키텍처와 Apache-2.0 라이선스다. 현재 모델보다 큰 모델을 비교하는 후보다. [공식 모델](https://huggingface.co/Qwen/Qwen3-8B-GGUF), [공식 라이선스](https://huggingface.co/Qwen/Qwen3-8B-GGUF/blob/main/LICENSE)

GPU 총 6GB에서 게임과 함께 전부 GPU에 올리는 방식은 여유가 부족할 가능성이 있다. CPU/GPU 분할과 실제 RAM·VRAM 측정이 필요하다. 느려질 수 있고 설치 용량도 더 크다. 이 판단은 파일 크기와 현재 하드웨어에 근거한 예상이며 아직 실측하지 않았다.

Thinking/Non-thinking을 지원하므로 현재 비추론형 Instruct 모델과 달리 비추론 모드 적용을 확인해야 한다. 단순 GGUF 파일 교체만으로 같은 비교가 된다고 가정하지 않는다.

## 이번 우선순위에서 제외: EXAONE 4.0 1.2B

한국어를 지원하는 소형 모델이지만 공개 모델의 라이선스는 EXAONE AI Model License Agreement 1.2 - NC다. 상업 게임 배포를 목표로 삼을 경우 해당 조건을 별도로 확인해야 하므로 첫 배포 후보로 선정하지 않았다. [공식 모델·라이선스 안내](https://huggingface.co/LGAI-EXAONE/EXAONE-4.0-1.2B#license)

## 다음 결정

1. Gemma의 접근·이용 조건을 사용자가 검토하고 선택하거나, 용량·속도 부담을 감수하고 Qwen3 8B를 선택한다.
2. 선택한 파일의 revision·크기·SHA256을 고정하고 테스트용 별도 위치에 설치한다. 현재 게임 모델은 덮어쓰지 않는다.
3. 기존 내부 추론 엔진에서 짧은 한국어·JSON 출력 호환성을 확인한다. 실패하면 런타임 변경 필요성을 먼저 보고한다.
4. 고정된 Inputs를 사용하고 모델 전용 템플릿 적용 차이를 기록해 비교한다.
5. 의미 오류·자연스러움·변화·시간·메모리를 함께 보고 교체 여부를 결정한다. 수치만으로 품질을 승인하지 않는다.

Gemma 로컬 추론 비교는 완료했다. 다운로드와 공식 조건 동의는 사용자가 직접 진행했다. 파인튜닝·가중치 변경·게임 모델 교체는 하지 않았다.
