# 3D Battle 개발

## Inspector에서 전투 선택

`3DBattleScene`의 `Battle3D` 오브젝트를 선택합니다.
`Battle 3D Scene Controller` 아래 **Battle Preview / Debug > Battle**에서 B1/B2/B3를 선택합니다.
편집 모드에서는 해당 그룹만 활성화되어 Scene 뷰에서 바로 배치할 수 있습니다.
Play 모드에서는 전투가 전환되며 체력, 입력, 쿨다운, 보스 공격과 효과를 초기화합니다.
**Restart Current Battle**은 현재 전투를 다시 시작합니다.

Hierarchy의 `Battle3D/Stages/B1`, `B2`, `B3` 각각에 환경, 보스, 시작 위치가 있습니다.
B2/B3는 기존 B1의 복제 템플릿입니다. 완성된 다른 캐릭터나 맵은 아니므로 각 그룹 안에서 교체/편집하세요.
보스를 교체할 때는 `BattleStage`의 Boss와 Pattern Runner 참조도 함께 연결하세요.
플레이어의 시작 배치는 `Player Spawn`, 보스 시작 배치는 `Boss Spawn`으로 조정합니다.

## 특정 보스 공격 실행

Play 모드에서 같은 Inspector의 **Boss Attack** 목록을 선택한 뒤 **Run Selected Attack**을 누릅니다.
**Stop Attack**으로 취소할 수 있습니다. 새 공격을 실행하면 이전 공격은 취소됩니다.
보스 또는 플레이어가 사망하면 공격이 중단됩니다. 전투 재시작으로 체력을 복원하세요.
자동 공격 AI는 아직 없으며 패턴을 수동 검증하는 기본 구조입니다.

`Settings/Patterns`의 자산에서 피해량, 예고 시간, 후딜레이, 반경, 각도, 애니메이션 상태를 조절합니다.
기본 예제는 전방 공격, 원형 공격, 빠른 전방 공격입니다. 붉은 경계로 공격 범위를 예고한 뒤 한 번 피해를 줍니다.
대시 중에는 피해를 받지 않습니다. 플레이어 체력과 무적 옵션은 플레이어의 `BattlePlayerHealth`에 있습니다.
범위 판정은 수평 거리/각도 기준이며, 장애물 차폐와 높이 판정은 후속 구현 대상입니다.
패턴 자산은 여러 전투가 공유할 수 있습니다. 전투별 수치를 독립적으로 바꾸려면 자산을 복제해 연결하세요.
Play 중 씬 오브젝트 변경은 종료 시 복원되지만 ScriptableObject 자산을 수정한 값은 유지될 수 있습니다.

## B4 이상 추가

1. 편집 모드에서 기존 B 그룹을 복제하고 이름을 지정합니다.
2. `Create > 3D Battle > Stage Settings`로 설정 자산을 만듭니다.
3. 중복되지 않는 양수 Stage Index, Display Name, 보스 체력, Patterns를 지정합니다.
4. 복제한 `BattleStage`의 Settings와 Boss/Pattern Runner/Spawn 참조를 확인합니다.
5. `Battle3DSceneController.Stages` 목록에 새 그룹을 추가합니다. Inspector 선택 목록에 자동 반영됩니다.

Stage Index에 상한은 없습니다. 번호 4의 표시 이름을 `BD`로 쓰고 싶다면 Stage Settings의 Display Name을 지정하세요.
같은 번호를 중복 등록하거나 필수 참조가 빠지면 전투 전환이 거부됩니다.

## Decision 연결

```csharp
using OneWay.Battle3D;
using UnityEngine.SceneManagement;

Battle3DEntry.SetNext(new Battle3DStartData(stageIndex, trait1, trait2, trait3, trait4));
SceneManager.LoadScene("3DBattleScene");
```

씬의 제어기가 데이터를 한 번 소비해 해당 전투를 선택합니다.
씬을 직접 실행하면 Inspector에서 미리 선택한 전투가 사용됩니다.
성향 값은 `Battle3DSceneController.StartData`에 보관되며 전투 전환 시 유지됩니다.
성향에 따른 능력치 계산은 아직 적용하지 않습니다. Decision 소스는 이 작업에서 수정하지 않았습니다.

## 폴더 역할

- `Scripts/Core`: 씬 전환과 전투 시작 제어
- `Scripts/Data`: 전투별 설정 데이터
- `Scripts/Stages`: 씬에 배치하는 전투 구성
- `Scripts/Patterns`: 공격 정의와 실행기. 새 공격은 `BossAttackPattern`을 상속하고 Execute를 구현합니다.
- `Scripts/Combat`: 체력 등 전투 공통 기능
- `Settings/Stages`, `Settings/Patterns`: Inspector에서 조정하는 설정 자산
- `Editor`: Inspector 및 초기 구성 도구
- `Authoring~/StageDevelopmentChecks.cs`: Pipeline run_script로 실행하는 편집/플레이 검증

패턴 실행 중 상태는 설정 자산에 저장하지 말고 실행기 또는 코루틴 지역 변수에 보관하세요.
전투별 탄환/이펙트를 추가할 때는 취소·사망·전투 전환에서도 정리되도록 실행기를 확장하세요.

## 마우스 회전 / 휠 이동 조작

- 마우스 이동: 현재 거리에서 플레이어 주위를 회전합니다. 지면에 가까워지면 하강 각도를 제한합니다.
- 휠 앞으로: 자동 전진 시작. 한 번만 굴려도 계속 이동하며 카메라 수평 방향을 따라갑니다.
- 휠 뒤로: 정지합니다.
- 좌클릭: 자동 전진을 멈추고 카메라 방향으로 평타. 적을 클릭할 필요는 없으며 전방 범위 안의 보스에게만 피해를 줍니다.
- 우클릭: 대쉬. 평타가 마지막 행동이면 뒤로 한 번, 그 외에는 앞으로 나갑니다. 휠 전진/정지 입력도 뒤 대쉬 예약을 해제합니다.
- 자동 전진 중 대쉬하면 대쉬 종료 후 전진을 이어갑니다. 대쉬 후 별도 경직/쿨다운은 없습니다.
- Esc: 마우스 고정 해제 및 이동 중단. 게임 화면을 클릭하면 다시 고정됩니다(재고정 클릭은 공격하지 않음).
- 좌우 동시 입력은 현재 대쉬 우선이며 패링은 구현하지 않았습니다. 기존 차지/특수공격 입력은 새 조작에서 사용하지 않습니다.

Main Camera의 Mouse Sensitivity, Min/Max Pitch, Ground Clearance로 감도와 각도, 지면 여유를 조절합니다.
이전 Smooth Time 값은 거리 유지를 위해 새 회전 카메라에서는 사용하지 않습니다.
검증: Authoring~/OrbitControlChecks.cs (Play 모드에서 Pipeline run_script).

## 점프 이동 / 행동 우선순위 디버그

B1 보스의 `Boss Jump Motion Debug`에서 Play 모드로 점프 이동을 시험합니다.
12번은 `Boss Jump Timing Debug`의 이륙 1.7초, 착지 2.38초를 사용합니다.
재생 속도 1배 기준 0.68초 동안 실제 루트가 이동합니다. 재생 속도를 바꾸면 이동 시간도 함께 바뀝니다.
착지점은 이륙 순간 보스→플레이어 직선 위에서 `Landing Gap`만큼 거리를 남기는 지점으로 확정합니다. 플레이어의 회전은 계산에 사용하지 않으며, 이미 간격 안에 있으면 수평 이동하지 않습니다.
`Additional Height`는 원본 뼈 애니메이션에 추가하는 루트 상승 높이로, 기본 0입니다.
공중 경로의 장애물 회피/내비게이션은 이 디버그 도구에 포함하지 않습니다.

우선순위는 공중 구간 > 쓰러지기 > 체력 기준 피격 반응 > 준비/회복/기타 동작입니다.
공중에도 체력은 감소하지만 반응은 착지까지 보류합니다. 착지 시 사망이 먼저 처리됩니다.
2/3 및 1/3 체력 기준은 각각 한 번 처리하며, 한 번에 여러 기준을 넘으면 피격 반응 하나로 합칩니다.
공중 중단 요청은 착지 후 처리합니다. 씬/전투 재시작·오브젝트 비활성화는 테스트 정리를 위해 즉시 초기화합니다.
Inspector에서 피해 34% / 체력 0 버튼으로 우선순위를 확인할 수 있습니다.
시작 위치 초기화 버튼은 보스 체력과 체력 기준 기록도 복원합니다.

17번도 같은 이동·우선순위 구조를 사용하지만 이륙/착지 시간이 아직 -1(미지정)이므로 실행이 거부됩니다.
시간을 확인한 뒤 Jump17 Takeoff/Landing에 입력하고 Use Jump17을 선택합니다.
검증 코드: `Authoring~/JumpMotionSetup.cs`의 Test.

12번 전투 재생에는 Basic_Jump_InPlace_Boss1.anim을 사용합니다. 원본의 수직 동작과 관절 회전은 보존하고 루트·골반 수평 이동만 제거하여 착지 후 미끄러짐을 막습니다. 원본 FBX 및 시점 확인용 클립은 유지합니다.
