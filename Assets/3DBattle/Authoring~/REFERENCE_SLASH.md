# 사진 기반 VoltSlash01

편집 파일: **KnightVoltCombo_ReferenceSlash.blend**

기존 KnightVoltCombo.blend는 덮어쓰지 않았습니다. 저장되지 않은 Blender 편집 내용에도 접근하지 않았습니다.
시작 시점의 저장본, 샘플 데이터, 1타 클립, 컨트롤러는 Backups/BeforeReferenceSlash_20261015에 보관했습니다.

## 동작

사용자가 제공한 1~5번 사진의 자세 순서와 오른손/왼발/오른발 설명을 기준으로 제작했습니다.
엔비 참고 이미지의 낮은 무게중심과 골반·상체 회전을 반영했습니다.
60 FPS, 프레임 0~54, 총 0.9초입니다.

| 프레임 | 동작 |
|---|---|
| 0 | 오른손 검을 세운 기본 자세 |
| 14 | 왼발 선행, 무릎 굽힘, 오른손을 왼쪽으로 이동하고 검끝은 뒤로 |
| 24 | 왼쪽에서 오른쪽으로 가로베기 시작, 오른발 이동 중 |
| 32 | 오른발을 앞에 디디며 골반과 상체가 오른쪽으로 회전 |
| 41 | 검을 오른쪽으로 뻗은 마무리 자세 |
| 45~54 | 다음 공격/대기 자세로 연결하기 위한 회복 |

각 포즈에는 액션 마커가 있습니다. 새 원본에도 기존 2~4타 액션을 유지했습니다.
검 프리뷰 오브젝트는 편집용이며 게임에서는 기존 기사 프리팹의 검을 사용합니다.

## 실제 전진

1타에서 CharacterController가 바라보는 방향으로 약 0.55 Unity 단위를 전진합니다.
전체 동작의 15~62% 구간에 부드러운 곡선으로 이동하며 벽과 보스 충돌을 지킵니다.
Blender에 작성된 전진분은 Unity 클립 변환 시 제거하고 코드가 적용하므로 이중 이동하지 않습니다.
우클릭 이동, 대시, 포커스 상실과 입력 취소 시 남은 전진은 취소됩니다.
기존 피해량과 피해 적용 시점은 변경하지 않았습니다.

보폭은 3DBattleScene의 Player > BattlePlayerController > First Slash Step Distance로 조절할 수 있습니다.
기본값 0.55는 현재 애니메이션의 발 디딤과 맞춘 값입니다. 크게 바꾸면 발이 미끄러져 보일 수 있습니다.

## Blender 편집 후 반영

1. KnightVoltCombo_ReferenceSlash.blend에서 HumanArmature, VoltSlash01을 선택해 편집하고 저장합니다.
2. 프로젝트 루트에서 다음 명령을 실행합니다.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background 'Assets/3DBattle/Authoring~/KnightVoltCombo_ReferenceSlash.blend' --python 'Assets/3DBattle/Authoring~/export_combo.py' -- --only VoltSlash01
```

3. Unity의 Play 모드를 끄고 Tools > 3D Battle > Rebuild Only VoltSlash01 from Blender Samples를 실행합니다.
4. 3DBattleScene에서 사거리 안의 보스를 짧게 좌클릭합니다.

이 경로는 2~4타 샘플과 클립을 갱신하지 않습니다.
액션의 forward_travel, travel_start, travel_end 사용자 속성은 전진 추출에 사용하므로 유지하세요.
author_reference_slash.py는 사진 기반 초기 포즈를 재생성하는 도구입니다. 새 편집본의 수작업 수정까지 덮어쓰므로 일반 편집 후에는 export_combo.py를 사용하세요.

## 미리보기

- ReferenceSlash.gif: 실제 속도에 가까운 반복 재생(끝에 확인용 정지 포함).
- ReferenceSlash_Slow.gif: 느린 재생.
- ReferenceSlashPoses.png: 위쪽 정면 / 아래쪽 측면, 왼쪽부터 사진 1~5에 해당하는 포즈.

사진에 없는 깊이와 중간 동작은 기사 캐릭터 비율에 맞춰 보간한 초안입니다.
