# JourneyInterfaceV3 본 게임 통합

DecisionScene의 표시 외장과 화면 프레임을 V3로 교체했다. 기존 입력/스토리/인벤토리 컴포넌트와 UnityEvent 콜백은 그대로 유지한다.

## 구성

- `_Gameplay3D/3D_UI/JourneyInterfaceV3_Runtime`: 새 조작판, 코인통, 기어, 노란 버튼, 가방.
- `_UI/Decision_Canvas/Decision_UI_Root/JourneyInterfaceV3_Frames`: 새 프레임. 기존 Canvas 안에 있고 Raycast Target은 꺼져 있다.
- Decision Canvas의 Plane Distance는 100. 기존 메인 카메라에서 3D 조작판보다 뒤에 그린다. 인벤토리 팝업은 기존 Overlay Canvas를 유지한다.
- 기존 외장의 Renderer/Collider만 비활성화했다. 스크립트와 대련 제작 앵커는 삭제하지 않았다.
- 코인통은 합쳐진 메시에서 재고 부분만 분리해 `JourneyCoinStack`으로 연결했다. 생성된 메시 8개는 `Assets/Decision_YYS/JourneyV3Integration`에 저장되어 런타임에 제작용 JSON이 필요하지 않다.
- 새 가방 인스턴스에 가져온 `Backpack_OpenLid`와 `Backpack_CloseLid`를 연결했다. 원본 프리팹 및 애니메이션 커브는 수정하지 않았다.
- 코인은 종류별 V3 출구에서 오른쪽 트레이로 배출된다. 왼쪽 보드까지 자동 이동하는 새 규칙은 추가하지 않았다.

## 보존

전투 클립 파일 해시가 작업 전후 동일하다. 손/기사 생성 및 Pose/SpawnPoint 기준 Transform 9개를 보존했다. 기사 복제 위치와 0.6 크기를 실행 중에도 확인했다. 대련 판정, 궤적, 방어 및 종료 규칙은 변경하지 않았다. 테스트 대련의 Health 표시만 새 코인통 참조를 따라가도록 연결했다.

작업 전 DecisionScene의 Editor 백업: `Temp/JourneyV3Migration/DecisionBefore-20261005-100359.unity`.
기존 사용자 변경을 포함한 상태의 백업이며 Temp 폴더는 Git 대상이 아니다.

## 검증 (2026-10-05)

- Unity 컴파일 성공.
- 카메라 Raycast에서 노란 버튼, 기어 손잡이, 공급 버튼 4개 모두 검출.
- 기존 노란 버튼/기어 콜백 각 1개 유지, 누락 스크립트 0개, DecisionScene EventSystem 1개.
- Play 재진입 후 참조와 숨김 상태 유지: 프리팹 오버라이드로 저장.
- 네 종류 코인 배출과 실제 재고/통 표시 10→9 확인, 출구 위치 일치 및 트레이 착지 확인.
- 가방 열림/닫힘 0→65→0도, 인벤토리 패널 표시/숨김 및 가방 클릭 Collider 확인.
- 대련 시작, 기사 복제 생성, 조작판 입력 잠금 및 Cleanup 후 입력 복원 확인.
- 마지막 검증 실행에서 새 오류 없음.
- 사용자의 실제 저장을 변경하지 않도록 스토리 시작 컴포넌트만 테스트 중 임시 비활성화하고, 테스트 이후 원복했다. Main 메뉴부터 이어지는 전체 스토리 플레이는 별도 사용자 확인 항목이다.

검증 캡처: `Temp/JourneyV3Migration/decision-v3-play.png`.
열려 있던 V3 미리보기 씬의 저장되지 않은 수정은 저장하거나 폐기하지 않았다.

## 재현 도구

### 화면 배치 보정 (2026-10-05)

- 조작판은 비율을 유지하고 화면 좌우 및 하단에 여백 없이 맞췄다. 1920×1080 기준 실제 메시 투영 범위는 X=0~1920, Y=0~577.79이다.
- 이야기 프레임은 1038×650으로 조정해 아랫변과 모서리를 조작판이 가리지 않는다.
- 맵과 얼굴 프레임은 모두 369.52×450이다. 기존 얼굴 리그는 프레임 안에 들어오도록 크기와 위치만 조정했다.
- 메인 카메라와 손/기사의 Pose/SpawnPoint, 애니메이션 파일은 변경하지 않았다.
- 최종 배치에서 버튼 6개 Raycast 검출, 누락 스크립트 0개, EventSystem 1개를 재확인했다. 이번 배치 검증의 Play에서는 Editor 프레임이 진행되지 않아 애니메이션 재생은 재검증하지 못했다. 위 Play 검증 결과는 이전 통합 단계의 결과다.
- 화면 캡처: `Temp/JourneyV3Migration/layout-no-margin.png`. 편집 도구: `AgentScripts/JourneyV3Layout.cs`.

`AgentScripts/JourneyV3Migration.cs`: Pipeline의 `run_script`로 실행하는 Editor 전용 네이티브 배치/검증 도구. `Migrate`는 이미 적용된 씬이나 생성 메시 폴더가 있으면 중복 적용/덮어쓰기를 거부한다.
`AgentScripts/JourneyV3RuntimeCheck.cs`: 저장 콜백을 격리한 Play smoke test. 검증 시 `IsolateView → Prepare → Play → Check → Stop → Restore → RestoreView` 순서로 실행하며 중간에 실패해도 Stop/Restore 단계는 수행한다. Assets 밖에 있어 게임 빌드에는 포함되지 않는다.
