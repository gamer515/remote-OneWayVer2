# 로키 가이드 1단계

## 구현 범위

- `Characters/Guide.json`, `Duelist.json`, `Gambler.json`: 고정 인물 ID, 이름, 역할, 성격.
- `Initial/Initial_01/Encounters/guide`: 지식 코인 5개 회수/복원 → 신 소개 선택 → 분기 → 능력 소개 → 20개 배분 → 출발.
- `Insult`: 통별 1개까지 감소 → 20개까지 채움 → 기존 수량으로 복원하되 체력은 1개 → 기존 `HumanArmature_kneel` 재생 → 배분.
- 회수 코인은 통 출구에서 경사로 끝으로 이동해 사라지고, 추가 코인은 통 위에서 내려온다. 실제 모델/애니메이션 원본은 수정하지 않는다.
- 통별 표시 슬롯은 런타임에만 20개로 확장한다. 기존 에셋/배치는 유지한다.

## 배분 조작

1. 기어를 위(좌상/우상)로 기울이면 **추가**, 아래(좌하/우하)면 **빼기**.
2. 원하는 코인통 버튼을 눌러 1개씩 변경. 빼면 남은 배분량으로 반환.
3. 총 20개를 모두 배분하고 노란 버튼으로 확정. 통별 최대 20개.

배분 초안의 0개는 사망 판정을 하지 않는다. 확정 때 체력 0 → 회차 종료/메인; 체력이 있고 민첩 0 → 로키가 재배분 허용. 둘 다 0이면 사망 우선.

지식 0, 매력 0, 둘 다 0은 결투·도박의 별도 `Variants` JSON을 읽는다. 매력 0은 불친절한 대사와 관계 변화 2배를 적용하며 결투·도박을 건너뛰지 않는다. 능력치에 따른 공격/방어/이동 속도와 보상 수량 계산은 후속 단계이며, 현재 대사는 목표 규칙을 소개한다.

## 기록과 다음 회차

`Saves/Runs/Run_####/Progress.json`에 지형 ID/이름, 인물 ID/이름, 실제 선택 문구/ID, 관계 변화, 코인·능력치 전후, 결투·도박 결과, 읽은 카드, 코인 연출 진행 상태를 저장한다. 아이템 교환 필드는 마련했지만 아직 없는 보상을 만들어 기록하지 않는다.

회차 종료 시 같은 폴더에 `LocalStoryRevisionRequest.json`을 저장한다. 상태는 **PendingLocalModel**이며 외부 API를 호출하지 않는다. 내부 모델이 아직 연결되지 않았으므로 다음 회차는 원본 이야기를 사용한다. 지형/인물/선택 결과 같은 고정 사실은 변경 대상이 아니다.

모델 다운로드·Unity 내부 추론·특정 이야기 생성 테스트 버튼·엑셀 관리/내보내기·파인튜닝은 이 단계에 포함하지 않았다. 학습은 사용자의 요청대로 후순위다.

## 검증

- Unity 코드 재컴파일 오류 없음.
- `AgentScripts/GuideStageOneCheck.cs`: 실제 플레이어 저장소와 분리한 `Temp/GuideStageOneChecks/<GUID>`에서 JSON 그래프, 변형본의 행동 보존, 배분 제한, 재고 복원, 선택/연출 중복 방지, 중단 재개, 사망 메뉴 콜백, 로컬 생성 요청 저장 검사.
- 자동 검사 124개 통과. 이 중 각 JSON 파일과 코인 1개 추가도 개별 검사로 계산한다.
- 새 가이드의 지문 칸 넘침은 문장을 여러 카드로 나눠 처리. 배분 안내는 기존 지문/선택지 칸을 함께 사용한다.
- 실제 Main부터 마우스로 끝까지 플레이하는 시각·조작 검증은 별도로 필요하다. 기존 진행 중 세이브가 이미 가이드를 해결했다면 새 회차에서 가이드를 확인한다. 기존 저장 파일은 삭제하지 않는다.

## 재검사

Unity Editor가 멈춰 있을 때:

```powershell
unity command recompile --format json
unity command recompile_status --format json
unity command run_script --file AgentScripts/GuideStageOneCheck.cs --entry GuideStageOneCheck.Regression --format json
unity command run_script --file AgentScripts/GuideStageOneCheck.cs --entry GuideStageOneCheck.TextFits --format json
```

`Regression`은 임시 파일만 남기고 플레이어 저장소나 씬을 저장하지 않는다. `BindKneel`/`RestoreIncidentalLayout`은 씬 저장을 수반하는 구현용 도우미이므로 재검사에 사용할 필요가 없다.
