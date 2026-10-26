# Knight sword combo

기본 엔비 데마라의 빠른 한손검 전투를 참고한 오리지널 4단 평타 초안입니다.
원작 애니메이션 데이터를 사용하거나 정확히 복제한 것이 아닙니다.

## 게임에서 확인

`3DBattleScene`을 열고 Play → 우클릭으로 보스에게 접근 → 사거리 안에서 보스를 짧게 연속 좌클릭합니다.

1. 대각선 베기: 0.483초
2. 역방향 베기: 0.500초
3. 회전 베기: 0.650초
4. 내려베기: 0.800초

빠른 클릭은 다음 공격 1회를 예약합니다. 마지막 공격 이후 잠시 쉬면 1타부터 다시 시작합니다.
우클릭 이동, 대시, 포커스 상실은 콤보 예약을 취소합니다.
0.7초 이상 누르고 놓는 차지 공격은 기존 동작을 사용합니다.
피해량과 사거리, 이동·대시 수치는 유지하며 피해는 기존처럼 공격 시작에 적용합니다.
루트 모션은 사용하지 않습니다.

## Blender에서 편집

- `KnightVoltCombo.blend`를 열고 `HumanArmature`를 선택합니다.
- Dope Sheet → Action Editor에서 `VoltSlash01`~`VoltSlash04`를 선택합니다.
- 60 FPS 기준으로 편집합니다. 각 액션에 Windup / Contact / Recover 마커가 있습니다.
- 검 모양의 단순 프리뷰 오브젝트는 편집용이며 게임 모델로 내보내지 않습니다.
- 편집본을 저장한 후 아래처럼 샘플을 다시 내보냅니다. 경로는 프로젝트 루트 기준입니다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background 'Assets/3DBattle/Authoring~/KnightVoltCombo.blend' --python 'Assets/3DBattle/Authoring~/export_combo.py'
```

Unity에서 `Tools > 3D Battle > Rebuild Sword Combo from Blender Samples`를 실행합니다.
`Animations/VoltSlash01.anim`~`VoltSlash04.anim`과 전용 `KnightBattle.controller`가 갱신됩니다.
실제 클립 길이는 실행 시 컨트롤러에서 읽으므로 키프레임 길이를 바꿔도 콤보 타이밍에 반영됩니다.

`make_combo.py`는 최초 포즈를 다시 만드는 생성기입니다. 실행하면 수작업으로 편집한 Blender 원본과 샘플을 덮어쓰므로, 편집본 재출력에는 `export_combo.py`를 사용하세요.

## 범위와 검증

공용 모델은 읽기만 하며 변경은 `Assets/3DBattle/` 안에서 수행합니다.
`Authoring~`는 Unity가 자동 임포트하지 않는 원본 폴더입니다. `.blend` 자동 FBX 변환에 의존하지 않습니다.
게임 실행에는 생성된 `.anim`, 컨트롤러, C# 코드만 필요합니다.

`BattleComboAuthoring.ValidateClips()`는 네 클립의 경로, 길이, 검 이동, 상태 연결을 검사합니다.
`BattleSceneBuilder.ValidateRuntime()`는 Play Mode의 임시 복제본에서 콤보와 기존 전투 동작을 검사합니다.
`ComboPreview.png`는 열마다 1~4타, 행마다 준비/타격/후속 포즈를 보여줍니다.
