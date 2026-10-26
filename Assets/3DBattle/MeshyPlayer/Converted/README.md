# 플레이어용 Meshy 모션

기존 KnightCharacter 골격에 맞춘 클립입니다. 원본 FBX, 기존 모델, 장비는 유지합니다.
구간 번호는 Blender에서 확인한 **30 FPS** 기준입니다. FBX를 Blender에 가져오면 첫 키는 1프레임이므로 Unity 시간 0에 대응합니다. 요청한 0프레임은 첫 자세를 유지하는 구간으로 처리했습니다. 출력 클립은 보간 확인을 위해 60 FPS로 저장합니다.

| 원본 | Blender 구간 | 길이 | KnightBattle Animator 상태 |
|---|---|---|---|
| Weapon_Combo | 0–75 | 2.5초 | AttackNew |
| Double_Combo_Attack | 30–45 변환 후 Unity 0–25프레임으로 자름 | 약 0.417초 | MeshyDoubleComboAttack_Player |
| Attack | 22–85 | 2.1초 | MeshyAttack_Player |
| Female_Crouch_Pick_Fr | 0–60 | 2초 | MeshyCrouchPick_Player |
| Run_03 | 전체, 1–25 | 0.8초 | MeshyRun_Player |
| Shield_Push_Left | 전체, 1–71 | 약 2.333초 | MeshyShieldPushLeft_Player |
| Run_Jump_and_Roll | 지상 구르기 57–104 + 준비 | 약 1.967초 | MeshyGroundRoll_Player |

`Weapon_Combo` 변환 파일은 기존 연결을 유지하기 위해 `Assets/3DBattle/Animations/MeshyWeaponCombo_Player.anim` 위치에 있습니다. 나머지 6개 클립은 이 폴더에 있습니다. 길이는 배속 1 기준입니다.

## 보정 내용

- Weapon_Combo, Double_Combo_Attack, Attack, Female_Crouch_Pick_Fr, Shield_Push_Left는 몸과 장비의 최저 높이를 기준으로 자세 높이를 보정했습니다. 수평 바닥 Y=0에서 120 FPS로 중간 자세까지 검사했으며, 발의 지면 침투가 없습니다. 경사면·계단용 발 IK는 포함하지 않습니다.
- 검 공격은 몸통에 대한 오른손의 이동 궤적을 따라 검끝이 향하도록 손목과 엄지를 보정했습니다. 손의 움직임이 느린 구간은 원본 손잡이 방향과 부드럽게 혼합합니다. 방패 동작의 오른손은 검 대기 자세를 유지합니다.
- Attack의 추가 수정: Unity 60 FPS 기준 0~18프레임은 검끝을 하늘 방향으로 유지하고, 19~30프레임에 전방으로 전환한 뒤 126프레임까지 전방 방향을 유지합니다. 전 구간에서 오른손이 검날 쪽, 왼손이 손잡이 끝 쪽에 위치하도록 양팔의 도달 범위 내에서 그립을 맞췄습니다. 이 추가 수정은 `MeshyPlayerLibrary.RepairAttackGrip`으로 적용하며, 원본에서 라이브러리를 다시 생성하면 이 후처리를 다시 실행해야 합니다.
- 방패 동작: 왼팔의 원본 휘두르기를 유지합니다. 오른팔은 몸통을 따라가는 검 대기 자세를 유지합니다. 방패 모델은 아직 장착하지 않았습니다.
- 달리기: 전체 사이클을 사용하며 마지막 몇 샘플을 첫 자세에 연결해 반복되도록 했습니다.
- 구르기: 앞의 달리기와 공중회전을 제외했습니다. 왼발이 앞으로 나온 자세에서 0.4초 동안 구르기 준비 자세로 이어집니다.
- 구르기의 몸, 투구, 견장은 모델 원점의 수평 바닥 Y=0을 기준으로 침투를 보정했습니다. 검날은 이 지면 보정의 기준에서 제외되어 추가 조정이 필요할 수 있습니다. 경사면·계단 대응은 포함하지 않습니다.
- 모션 자체의 수평 이동은 제거했습니다. 실제 전진·대시 이동량은 플레이어 이동 코드에서 별도로 적용해야 합니다.

## 모션 확인

WeaponCombo 최신 손목 제한(60 FPS): 1~40프레임은 1·40프레임의 손목 상대 회전 사이를 짧은 경로로 연결하고, 40~50프레임 자세를 유지합니다. 50~150프레임은 50프레임 손목 상대 각도를 고정하며 경계의 보간 오버슈트도 제거했습니다. 팔과 몸통 움직임에 따른 검의 이동은 유지됩니다. `MeshyPlayerLibrary.LimitWeaponWrist`가 손목 교환 이후 적용된 최신 후처리입니다.

WeaponCombo 손목 교환(60 FPS): 올려베기 34~50과 내려베기 74~86, 올려베기 86~110과 내려베기 124~142의 오른손 손목 상대 회전 곡선을 진행도에 맞춰 서로 교환했습니다. 각 경계는 3프레임 동안 연결합니다. 오른손 엄지는 손목과 함께 보정하며, 나머지 뼈와 2.5초 길이는 유지합니다. `MeshyPlayerLibrary.SwapWeaponComboWrists`는 교환 작업이므로 이미 적용한 결과에 반복 실행하지 마세요.

Run 최신 수정: 전체 0~48프레임(60 FPS, 0.8초)에서 오른쪽 팔꿈치와 손목을 기본 Idle_swordRight 자세 각도로 고정했습니다. 오른쪽 어깨·상완은 원본 타이밍을 따라 앞뒤 평면으로만 30도 범위에서 왕복합니다. 팔꿈치·손목의 상대 각도는 기본 자세로 고정하며, 검끝은 좌우로 틀어지지 않고 수직에서 전방으로 20~50도 기울어진 범위를 오갑니다. 몸통·하체·왼팔과 반복 연결을 유지합니다. 최종 후처리는 `MeshyPlayerLibrary.RepairRunSagittalSwing`입니다.

GroundRoll 추가 수정(60 FPS): 8~14프레임에 접촉 자세로 연결하고, 14~80프레임은 오른손이 손잡이를 잡고 왼손 손바닥이 검끝에 닿도록 양팔을 보정합니다. 81~98프레임에는 검을 위로 90도 돌려 앞위를 향하게 하며, 양손은 94프레임까지 원래 팔 동작으로 연결합니다. 98~118프레임은 검의 앞위 방향을 유지합니다. 클립 길이와 하체 동작은 유지합니다. `MeshyPlayerLibrary.RepairRollSword` 후처리로 적용합니다.

DoubleCombo 추가 수정(60 FPS): 사진의 1·5·10·18프레임 검 방향을 기준으로 올려베기를 연결하고, 18~25프레임은 검을 180도 회전시킵니다. 기존 25프레임의 전체 자세를 그대로 보존하며 클립은 25프레임에서 끝납니다. `MeshyPlayerLibrary.RepairDoubleCombo` 후처리로 적용합니다.

CrouchPick 추가 수정(60 FPS): 0~58프레임은 검끝을 전방으로 고정하고, 103~120프레임은 103프레임의 검 방향을 유지합니다. 59~64프레임은 경계의 급회전을 줄이기 위해 원래 동작으로 연결하며, 65~102프레임은 유지합니다. `MeshyPlayerLibrary.RepairCrouchSword` 후처리로 적용합니다.

`PlayerMotionPreview.prefab`을 씬에 임시로 배치하고 Play하면 7개 모션을 순서대로 재생합니다.
그 오브젝트의 `Meshy Animation Debugger`에서 Previous / Next, Pause, Time 슬라이더로 확인할 수 있습니다.
이 프리팹의 `Reload clips from model FBX` 버튼은 사용하지 마세요. 원본 FBX가 아닌 변환 클립 목록을 사용합니다.

실제 플레이어 Animator에는 위 표의 상태가 등록되어 있습니다. 예:

```csharp
player.animator.Play("Base Layer.MeshyShieldPushLeft_Player", 0, 0f);
```

전투 코드가 다른 상태를 재생하면 덮어쓸 수 있으므로 독립적인 확인에는 미리보기 프리팹을 사용하세요.
현재 좌클릭 평타는 아래 5단 콤보를 사용합니다. 달리기·방패·구르기 등의 게임 입력 배정, 방패 판정은 이 작업에 포함하지 않습니다. 피해 판정은 기존 코드처럼 각 공격 시작 시 1회이며, 검이 닿는 프레임에 맞춘 타격 시점 조정은 별도입니다.

## 5단 평타

원본 3개 클립은 보존하고 이 폴더에 `MeshyCombo01.anim`~`MeshyCombo05.anim`을 만들었습니다. 아래 프레임은 Unity **60 FPS** 기준이며 시작/연결 및 복귀 구간이 포함되어 원본 번호와 다릅니다.

서로 다른 원본의 몸통 수평 위치를 WeaponCombo 시작 위치로 정렬한 뒤 연결합니다. DoubleCombo 원본의 약 1m 후방 오프셋 때문에 생기던 4타 후퇴·5타 복귀 현상을 제거했습니다. 높이와 관절 자세는 유지하고 전체 골격의 수평 이동만 보정합니다.

| 단계 | 사용 원본 구간 | 시작/연결 | 공격 끝(복귀 시작 경계) | 복귀 완료 |
|---|---|---|---|---|
| 1 | WeaponCombo 0~60 | 0~12: 기본 자세→원본 시작 | 72 | 90 |
| 2 | WeaponCombo 60~100 | 앞 공격 끝과 동일한 자세로 시작 | 40 | 58 |
| 3 | WeaponCombo 100~150 | 앞 공격 끝과 동일한 자세로 시작 | 50 | 68 |
| 4 | DoubleComboAttack 0~25 | 0~8: 3타 끝→원본 시작 | 33 | 51 |
| 5 | Attack 0~126 | 0~8: 4타 끝→원본 시작 | 134 | 152 |

각 공격 끝에 18프레임의 기본 자세 복귀 동작을 추가했습니다. 1~4타에서 공격 끝 경계 전에 좌클릭을 누르면 다음 공격을 예약하고 복귀 구간을 건너뜁니다. 예약하지 않으면 해당 클립의 끝까지 복귀합니다. 복귀 구간에 새로 누르면 1타부터 다시 시작합니다. 5타 뒤에 다시 예약하면 5타의 복귀를 끝낸 뒤 1타로 돌아갑니다. `Normal Attack Speed`는 시작/연결/복귀 구간 모두에 적용됩니다.

실제 플레이어의 평타는 자동으로 이 5개 상태를 사용하며 원본 `AttackNew`는 보존했습니다. 독립 미리보기는 `FiveHitComboPreview.prefab`을 사용합니다. 미리보기는 각 클립의 복귀까지 보여주며 연속 입력 연결은 실제 플레이어에서 확인합니다.

재생성: `MeshyPlayerLibrary.BuildFiveHitCombo`. 원본 모션을 수정하면 이 생성 작업을 다시 실행해야 파생된 5개 클립에 반영됩니다. 검사: `FiveHitComboCheck.Run`(Play 모드), 보고서 `Authoring~/FiveHitComboValidation.txt`, `FiveHitComboRuntimeValidation.txt`.

변환 스크립트: `Assets/3DBattle/Authoring~/MeshyPlayerLibrary.cs`.
검증 결과와 자세별 이미지: `Assets/3DBattle/Authoring~/MeshyPlayerLibrary*Validation.txt`, `Meshy*_Player_review.png`.
