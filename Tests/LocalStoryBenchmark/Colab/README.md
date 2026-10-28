# Colab 학습 시험과 로컬 이어 학습

현재 실행 노트북: [OneWay_Colab_Training](https://colab.research.google.com/drive/1zjUGxs-9laG_FGhoQIVEoFCjGWP0l7am).
로컬 원본: `OneWay_Colab_Training.ipynb`. 사용자는 승인된 입력/정답 6쌍과 학습 도구를 Google Colab에 업로드·실행하도록 명시적으로 승인했다. 공개 공유 설정은 바꾸지 않는다.

실행 상태: 사용자가 HF_TOKEN을 직접 등록한 뒤 모델 접근·자료 검증을 통과했다. T4 GPU(15360 MiB)에서 2단계 역전파·체크포인트 저장·어댑터 재로딩과 별도의 12단계 학습·어댑터 재로딩이 모두 완료됐다. 실제 입력/정답 길이는 811~1574토큰이었다. 최종 학습의 최대 할당 메모리는 11551162880바이트(약 10.76GiB), 최대 예약 메모리는 14963179520바이트였다. 이 설정은 현재 로컬 6GB GPU에 그대로 들어가지 않는다. 다른 학습 설정의 가능성까지 부정하는 결과는 아니다. 자세한 관찰 기록은 CloudExecutionStatus.json에 있다.

## 첫 실험 결과 — 2026-10-29

| 항목 | 측정 결과 |
|---|---|
| 기반 모델 | google/gemma-3-4b-it / 093f9f388b31de276ce2de164bdc2081324b9767 |
| 자료 | 승인 후 Explanation 의미와 대조한 Guide 계열 6쌍 |
| 방법 | NF4 QLoRA / 언어 모델 q_proj·v_proj / rank 8 |
| 짧은 시험 | optimizer 2단계 / 역전파·저장·재로딩 통과 |
| 별도 실험 | optimizer 12단계 / 4 epoch / 역전파·저장·재로딩 통과 |
| 별도 실험 학습 시간 | 389.50초 |
| 적재·최종 저장 포함 | 426.37초 |
| 평균 학습 손실 | 2.6247 / 미학습 품질 점수가 아님 |
| GPU 최대 할당 / 예약 | 약 10.76 / 13.94 GiB |
| 게임 적용·미학습 평가·로컬 180초 | 미실행 |

결과는 [실험 폴더](https://drive.google.com/drive/folders/12M4KS7P9nCnCulrf0TinWjLYgdX5BbCZ), [최종 어댑터](https://drive.google.com/drive/folders/1X1Z2-hJxTzYxDq_ialx25ALA_kzvYzOD), [checkpoint-12](https://drive.google.com/drive/folders/1tMrRVKbR8yUKNLK3PAgIWMml_4hoxH5d)에 저장돼 있다. Google Drive 도구로 어댑터 파일(8932856바이트), 설정·출처·토크나이저와 체크포인트의 optimizer/scheduler/RNG/trainer 상태 파일 존재를 확인했다. 중단 후 resume 자체를 실제 실행해 본 것은 아니다. 모델·어댑터·개인 세이브를 Git에 추가하지 않았다.

학습 가능성과 저장 경로는 확인했다. 다음은 미학습 장면의 의미 기준을 확정하고 기존 모델과 비교하는 단계다. 학습 원본이 현재 게임 QAT GGUF와 다르므로 학습하지 않은 동일 원본의 변환 결과도 비교군으로 필요하다. 자료 6쌍의 학습 손실 감소를 일반화·게임 적용 승인으로 해석하지 않는다.

## 실행 순서

1. 노트북에 GPU 런타임을 할당한다. 무료 GPU의 종류와 실행 시간은 보장되지 않는다.
2. 포함된 데이터와 도구를 복원하고 SHA256·승인·의미 정렬·학습 묶음을 검사한다.
3. 본인 계정으로 Gemma 이용 조건을 확인한다. Colab 보안 비밀에 읽기용 `HF_TOKEN`을 등록한다. 토큰을 코드·Git·출력·압축 파일에 넣지 않는다.
4. Drive를 연결한다. `MyDrive/OneWayTraining/experiment-001`에 정확한 모델 revision과 실제 도구 버전, 실제 토큰 길이를 기록한다.
5. 원본 입력을 자르지 않고 2 optimizer step 시험을 한다. 적재·역전파·유한한 gradient·체크포인트 저장·어댑터 재로딩을 검사한다.
6. 위 시험이 성공하면 같은 원본 모델에서 새로 12 optimizer step을 실행한다. 시험 어댑터와 누적하지 않는다. 이는 학습 가능성 시험이며 일반화 증명이 아니다.

`maxSequenceLength=4096`은 사실·정답을 잘라 넣는 설정이 아니다. 실제 길이가 초과하면 중단한다. OOM이나 수치 오류도 실패로 남기며 자동으로 모델·입력·프롬프트를 바꾸지 않는다. 학습 설정을 바꾸는 경우 새 실험 디렉터리와 새 bundle을 사용한다.

## 중단 후 재개

체크포인트는 2 optimizer step마다 Drive에 저장하고 최근 두 개를 유지한다. 갑작스러운 종료 시 마지막 저장 이후 단계는 다시 수행해야 한다. 모델 적재/토큰 다운로드가 중단된 경우 저장된 모델 commit을 재사용해 prepare부터 다시 실행한다.

같은 노트북의 자료 복원·설치·Drive 연결·prepare를 실행한 뒤 `RESUME_INTERRUPTED_TRIAL=True`인 재개 셀을 실행한다. smoke/train을 처음부터 재실행하지 않는다. 완성된 adapter/optimizer/scheduler/trainer/RNG 파일이 있는 checkpoint만 사용한다. 입력·정확한 모델 revision·도구 버전·학습 설정·계산 정밀도가 다르면 정확한 상태 재개를 거부한다. GPU가 바뀌면 비트 단위 동일한 수치 결과까지 보장하지 않는다.

## 로컬에서 이어가기

Drive에서 `experiment-001` 전체를 로컬의 Git 제외 경로(예: `outputs/AiTraining/experiment-001`)로 내려받는다. 로컬에도 동일한 학습용 원본과 토크나이저가 필요하다. 현재 게임의 QAT GGUF는 학습 체크포인트가 아니다.

별도 Python 환경을 만들고 [PyTorch 공식 설치 안내](https://pytorch.org/get-started/locally/)에 맞는 CUDA용 torch를 준비한다. 나머지 후보 버전은 requirements.txt를 사용한다. 정확한 재개는 experiment-lock.json의 실제 패키지 버전과 일치해야 한다. 현재 6GB PC에서 성공했다고 검증한 것은 아니다.

번들을 로컬에서 생성하고 압축을 Git 제외 경로에 푼 뒤 다음처럼 실행한다. 아래 경로는 예시이며 실제 작업 폴더에 맞춘다.

```powershell
python AgentScripts/ColabTraining/build_bundle.py --project C:/remote-OneWayVer2
Expand-Archive -LiteralPath outputs/AiTraining/OneWay_Colab_Bundle.zip -DestinationPath outputs/AiTraining/bundle
python outputs/AiTraining/bundle/train.py validate --bundle outputs/AiTraining/bundle
python outputs/AiTraining/bundle/train.py resume --bundle outputs/AiTraining/bundle --run outputs/AiTraining/experiment-001 --checkpoint outputs/AiTraining/experiment-001/trial/checkpoint-10
```

`checkpoint-10`은 예시다. 실제로 존재하고 원래 목표 단계에 미달한 완성된 체크포인트를 선택한다. 최종 12단계가 이미 끝났다면 resume로 추가 단계를 몰래 늘리지 않는다.

보완 자료로 새로운 학습을 시작할 때는 기존 모듈에서 승인 자료를 다시 내보내고 bundle을 재생성한다. 새 run에서 smoke를 통과한 뒤 기존 어댑터를 warmstart로 읽는다. 이 경우 원본 모델 revision은 부모 어댑터와 동일하게 고정하며 optimizer·scheduler는 새로 시작한다.

```powershell
python outputs/AiTraining/bundle/train.py smoke --bundle outputs/AiTraining/bundle --run outputs/AiTraining/local-followup-001
python outputs/AiTraining/bundle/train.py warmstart --bundle outputs/AiTraining/bundle --run outputs/AiTraining/local-followup-001 --adapter outputs/AiTraining/experiment-001/trial/adapter
```

warmstart를 위해 먼저 prepare를 따로 수행하는 경우 experiment.json의 modelRevision을 부모 provenance.json의 값으로 고정해 새 bundle을 만든다. 어댑터만 가져오는 것은 optimizer 상태 재개와 다르다.

## 아직 완료되지 않은 검증

- 현재 자료는 Guide 계열 6쌍이다. Gambler 중간 검증과 Duelist 최종 평가에는 세부 의미 기준 보완이 필요하다. 같은 Guide 분기나 기억 변형을 미학습 평가로 사용하지 않는다.
- 클라우드 학습 후보 google/gemma-3-4b-it와 게임 기준 QAT GGUF 사이의 차이를 분리해야 한다. 학습하지 않은 원본 변환 결과와 학습 후 변환 결과도 비교한다.
- 병합·GGUF 변환·양자화·LlamaLib 2.0.5 호환·Unity 적용은 별도 단계다. 자동으로 게임 모델을 바꾸는 코드는 없다.
- 180초 목표는 최종 로컬 장면 생성에 대한 것이다. 학습 시간이나 Colab 생성 시간으로 대신 판정하지 않는다.

## 미학습 장면 탐색 비교 — 2026-10-29

사용자 승인 후 기존 Unity 프롬프트 조립 모듈로 Gambler 검증 장면과 Duelist 평가 장면을 내보냈다. 각 장면의 안내 2카드 구간과 선택 1카드 구간에 두 시드(12345/67890)를 사용하고, 동일한 HF NF4 기반 모델에서 LoRA 비활성/활성으로 총 16회 생성했다. 게임 모델·프롬프트·설정·개인 세이브는 수정하지 않았다. 각 출력은 수정하지 않고 Drive와 로컬에 보존했다.

결과는 [Drive 비교 폴더](https://drive.google.com/drive/folders/1RFJuS2z0T0O06tWgRQ0BNpLpyX1LxgdC)에 있다. 로컬 원시 JSON/HTML과 검토를 붙인 ComparisonReviewed.html은 outputs/AiTraining에 있다. EvaluationReview.json의 검토 문구 10개는 실제 출력에 존재하는지 검사했다.

이번 조건에서는 안정적인 의미 보존 개선을 확인하지 못했다. 원본의 코드 블록 접두사는 학습 후 사라졌으나 두 안내 카드가 한 카드로 줄고, 선택지 문자열 대신 입력의 intent/reference 객체를 출력했다. 형식 검사를 통과한 학습 후 대련 선택도 '방어 자세를 살핀다'를 '검을 살펴본다'로 바꾸고 '이번에는 지나간다'를 '다른 방법을 찾는다'로 바꿨다. 입력에 없는 판돈·상금·패배 시 소유물 이전도 확인됐다. 이번 어댑터를 게임 모델로 채택하지 않는 것이 Codex의 검토 권고이며 사용자 품질 승인과는 구분한다.

엄격 형식 통과는 학습 전 0/8, 학습 후 1/8이었지만 양쪽 모두 게임의 GBNF 없이 HF에서 생성했다. 따라서 이 수치를 게임 런타임의 통과율로 해석하지 않는다. 세부 Explanation 의미 항목도 비어 있어 기존 Story 대사·선택지를 보조 근거로 표시했다. 현재 QAT GGUF와의 최종 비교, 모든 장면의 품질 판정, 로컬 180초 목표 측정은 아니다. 이후 대련 출력에 맞춰 추가 튜닝한다면 새로운 최종 미학습 평가 장면을 분리해야 한다.

## 근거와 로컬 검증

[Colab 제한](https://research.google.com/colaboratory/faq.html), [Gemma 공식 학습 안내](https://ai.google.dev/gemma/docs/tune), [PEFT 양자화 학습](https://huggingface.co/docs/peft/v0.17.0/developer_guides/quantization), [Gemma 3 모델 API](https://huggingface.co/docs/transformers/en/model_doc/gemma3)를 참고했다. 고정 패키지 조합은 실행 시험으로 확인할 후보이며 GPU 성공을 미리 주장하지 않는다.

`AgentScripts/ColabTraining/check_bundle.py`는 노트북 코드 구문, 내장 zip 일치, 6쌍 정렬을 확인하고 미승인 자료·검사 실패 자료·최종 평가 묶음 누수·정답 변경·파일 변조를 거부하는 다섯 검사를 수행한다. GPU 학습 성공 여부는 Drive의 smoke/completed.json과 trial/completed.json으로만 확인한다.
