# 프로젝트 용량·중복 처리 정리 — 2026-10-09

## 삭제한 파일

삭제 합계: **5,791,397,836바이트 — 5.79GB / 5.39GiB**. 파일 크기의 합이며 실제 디스크 할당량과는 차이가 있을 수 있다.

정리 후 전체 폴더 재측정: **약 9.37GB / 8.73GiB** (`.git`와 캐시 포함). 주요 잔여 용량은 `Assets` 약 3.27GiB, 현재 게임 `Library` 약 3.01GiB, `.git` 약 1.16GiB, 제작 소스 `AssetBuild` 약 0.81GiB, 배포·원본 `outputs` 약 0.46GiB다. Unity를 열거나 검사하면 캐시 용량은 조금 변한다.

| 대상 | 삭제한 바이트 | 복구 방법 |
| --- | ---: | --- |
| `AssetBuild/JourneyInterfaceV2BuildProject/Library` | 2,030,649,493 | 해당 제작 프로젝트를 Unity로 열면 재생성 |
| `AssetBuild/JourneyInterfaceV3ImportTest/Library` | 2,000,236,338 | 해당 검증 프로젝트를 Unity로 열면 재생성 |
| `AssetBuild/JourneyInterfaceV2ImportTest/Library` | 1,633,091,790 | 해당 검증 프로젝트를 Unity로 열면 재생성 |
| `AssetBuild/BlenderTools/blender-4.5.0-windows-x64/blender.pdb` | 119,697,408 | 필요할 경우 동일 Blender 배포본에서 다시 확보 |
| 위 Blender 폴더의 `4.5/python/lib/venv/scripts/nt/python.pdb` | 3,543,040 | 동일 배포본에서 다시 확보 |
| 위 Blender 폴더의 `4.5/python/lib/venv/scripts/nt/pythonw.pdb` | 3,526,656 | 동일 배포본에서 다시 확보 |
| `AgentScripts/LocalStoryTwoPassUI.png` | 653,111 | 테스트 화면을 다시 캡처 |

휴지통 이동이 아닌 영구 삭제다. 캐시는 자동 재생성되지만 PDB와 스크린샷은 자동 재생성되지 않는다. BlenderTools에는 실행 파일 없이 위 디버깅 파일 3개만 남아 있었다. 실제 게임 에셋과 Blender 제작 원본은 삭제하지 않았다.

## 유지한 파일과 이유

- 현재 게임의 `Library`: 실행 중인 에디터와 충돌할 수 있고 다시 생성되므로 이번 정리에서 제외.
- `Assets`, 모든 씬·프리팹·애니메이션, 원본 Story JSON, 사용자 변경분: 게임 연결 유지.
- `Assets/StreamingAssets/LocalStory`: 실제 로컬 생성에 사용하는 모델·추론 라이브러리. 캐시가 아니다.
- `.git`: 변경 이력과 복구에 사용.
- `outputs`의 최신 배포 ZIP, Blender 원본과 `.blend1`: SHA256 비교 결과 세 백업 모두 현재 원본과 달랐다. 단순 중복이 아니므로 보존.
- `AssetBuild/JourneyInterfaceV2BuildProject/BoardGeometryV3.json`: `AgentScripts/JourneyV3Migration.cs`에서 사용하는 재구축 입력. 삭제하면 재구축 도구가 작동하지 않는다.
- 제작·검증 프로젝트의 소스, 기존 검사 도구, 실제 플레이 저장 및 LLM 테스트 보고서: 재제작·회귀 검증·품질 비교에 필요.
- V3/V4/MapKit 에셋: 다른 버전으로 보여도 기존 참조를 함께 사용하는 부분이 있어 이번 정리에서 삭제하지 않았다.

## 파일 통합 검토와 코드 정리

`Assets/Decision_YYS`의 `.cs`와 `.json` 151개를 파일 크기와 SHA256으로 비교했으며, 완전히 동일한 파일은 발견되지 않았다. NPC별/능력치별 Story 파일은 경로로 조회되는 개별 콘텐츠이므로 한 파일로 합치지 않았다. 씬 연결 스크립트 이름이나 GUID도 변경하지 않았다.

대신 테스트 UI와 검사 도구에 중복되어 있던 초안 검증·유사도 계산·변경 카드 집계를 `LocalStoryRewrite.EvaluateDraft`로 공통화했다. UI, 단일 생성 검사, 두 단계 생성 검사가 같은 처리를 사용한다. 재검사 때 집계가 누적되지 않으며 검사 실패 시 이전 후보·집계를 지운다. 실패한 생성 원시 출력은 그대로 남기고 원문으로 대체하지 않는다. 교정 출력·원문·게임 규칙·프롬프트·모델 설정은 변경하지 않았다.

중첩 제작 프로젝트의 캐시 제외 규칙은 `.gitignore`의 공통 패턴으로 정리했다. 캐시가 재생성되어도 Git에 들어가지 않도록 한다.

## 검증

현재 프로젝트 Unity 6000.3.12f1에서 재컴파일 완료: 오류 없음.

`AgentScripts/LocalStoryEditorCheck.cs`의 다음 검사를 실행했다:

- `Regression`: 334개 통과 — 원문 보존, 잘못된 JSON·규칙 변경 거부.
- `ReportRegression`: 7개 통과 — 공통 집계, 재검사 안전성, 실패 출력 보존.
- `BriefRegression`: 10개 통과 — 이야기 요약·기억 필터·원문 보존.
- `CorrectionRegression`: 10개 통과 — 초안/교정 분리·고정 표현·설정 유지.
- `OutputLineBreaks`: 7개 통과 — 표시 줄바꿈과 원시 출력 보존.
- `Persistence`: 격리된 임시 저장소에서 다음 회차 적용·실패 시 원본 대체 통과. 실제 플레이 저장에는 쓰지 않았다.

총 368개 검사와 회차별 저장 검사 통과. 이번 정리에서는 모델을 다시 추론하거나 게임 이야기를 생성·교체하지 않았다. 자연스러움 등 생성 품질이 개선되었다는 뜻은 아니다.
