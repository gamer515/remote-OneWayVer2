# Journey Map Kit — 첫 여행길

Unity 6000.3.12f1 / Universal Render Pipeline 17.3 / Unity UI용 에셋입니다. 원본 프로젝트를 수정하지 않고 별도 프로젝트에서 제작했습니다.

## 먼저 열어볼 것

1. `JourneyMapKit_Unity6_URP.unitypackage`를 Assets → Import Package → Custom Package로 가져옵니다. 모두 `Assets/JourneyMapKit` 아래에 들어갑니다.
2. 현재 작업을 저장한 뒤 `Scenes/JourneyKit_ProjectLayout.unity`를 열어 확인합니다. 현재 게임 장면에 자동으로 적용되지는 않습니다.
3. 원래 시안의 넓은 하단 비율은 `Scenes/JourneyKit_ApprovedLayout.unity`에서 확인합니다.
4. Play에서 기어를 드래그하면 코인 종류가 바뀝니다. 노란 버튼 또는 코인 공급 칸을 누르면 테스트 코인이 경사로 위에 생성됩니다. 이는 경사로와 입력을 확인하는 데모 동작입니다.

## 크기와 배치

기준 화면은 1920×1080, Canvas Scaler는 Scale With Screen Size, Reference Resolution은 1920×1080입니다. UI의 Transform Scale은 (1,1,1)을 유지하고 RectTransform의 크기와 앵커로 조절하세요.

- `JourneyUI_Project_1152x810`: 좌측 이야기 프레임의 외곽 RectTransform이 1152×810입니다. 내부 여백은 왼쪽100, 위36, 오른쪽30, 아래36이므로 실제 콘텐츠 자리와 외곽 크기를 혼동하지 마세요. 기존 화면의 콘텐츠 자체를 1152×810 그대로 유지하려면 `PanelFrame_Transparent`를 그 위에 얹거나 외곽 장식 크기를 별도로 늘리면 됩니다.
- 프로젝트 크기용 레이아웃: 중간 열384px, 오른쪽 열384px, 하단270px. 중간 풍경은590px, 공급부220px로 나눈 새 배치입니다. 기존 Road View의645px와 동일한 배치는 아닙니다.
- `JourneyUI_ApprovedLayout`: 이야기 외곽1010×684, 중간398px, 오른쪽512px, 하단396px. 시안처럼 하단 조작판을 크게 보여주는 대안입니다.
- `StoryFrame_1152x810`과 `PanelFrame_Transparent`는 독립 프레임입니다. 얇은 테두리는 벡터 UI로 그려 크기를 바꿔도 선이 늘어나지 않습니다.

3D 조립체는 `JourneyBoardAssembly`입니다. 왼쪽 보드 폭9, 오른쪽 조작판 폭9, 깊이4.6 Unity 단위이며 전체 폭은18.7입니다. 오른쪽에서 왼쪽으로 내려가는 경사로는 약10.78°, 통로 폭0.9입니다. 먼저 조립체 전체를 같은 비율로 Scale 조절하세요. 부품마다 다른 비율로 늘리면 출구 높이와 충돌체 연결이 달라집니다. 화면에 보이는 크기는 렌더 카메라의 Orthographic Size와 RawImage의 영역으로 조절할 수 있습니다.

## 포함된 실제 에셋

- 일체형 나무 트레이와 기어, 기어 바로 아래의 노란 버튼 1개
- 초록 격자 보드, 양쪽 테두리가 실제로 열린 왼쪽 내리막 통로
- 분리 프리팹: 조작판, 격자 보드, 통로, 공급기, 네 색상 물리 코인
- 종이·나무 텍스처, 투명 나침반·나무·언덕 스케치 스프라이트
- 전체 UI 프리팹 2종, 독립 프레임, 인벤토리 8칸, 상태 곡선
- URP 머티리얼, 물리 머티리얼, Mesh 자산, C# 소스, 미리보기 장면 2개

숫자판과 파란/빨간 조작 버튼은 포함하지 않았습니다. 얼굴과 풍경 이미지는 사용 중인 게임 콘텐츠를 연결할 자리로 비워 두었습니다. 프레임에 구워 넣은 이미지가 아닙니다.

## 기존 프로젝트 연결

`JourneyBoardReferences`에 기어, 손잡이 Collider, 노란 버튼 Collider, 트레이 생성 위치, 출구 입구/끝, 코인 진입 지점4개, 트레이 바닥 Collider가 연결되어 있습니다.

`JourneyUIReferences`의 Landscape View / Portrait View에 기존 RenderTexture를 연결하거나 `SetLandscape(texture)`, `SetPortrait(texture)`를 호출하세요. 빈 RawImage는 기본 비활성화되어 있으므로 Inspector로 연결했다면 해당 오브젝트도 활성화하세요. Story Content 아래에는 기존 이야기 UI를 넣을 수 있습니다. 기존 콘텐츠 크기를 유지할 때는 위 크기 설명을 따르세요.

`JourneyBoardInput`은 노란 버튼 클릭(`onYellowPressed`)과 기어 선택(`onGearSelected`, 0~3)을 UnityEvent로 제공합니다. 렌더 텍스처 방식이면 Input Camera와 Board Viewport를 함께 연결합니다. 제공 데모 장면에는 연결되어 있습니다. 실제 게임 카메라로 직접 보이는 3D 오브젝트라면 Board Viewport를 비우고 Input Camera를 지정합니다.

현재 프로젝트의 `BettingButtonController`는 파란·빨간·노란 버튼 세 개를 모두 요구합니다. `CoinDropController`도 파란 버튼의 생성 이벤트와 빨간 버튼의 확정 이벤트를 각각 구독하며 해당 처리 함수는 private입니다. 따라서 새 프리팹을 놓거나 Collider 참조만 바꾸는 것으로 한 버튼 게임 규칙까지 자동 전환되지는 않습니다. 한 버튼을 ‘투입’으로 쓸지 ‘확정’으로 쓸지에 맞춰 기존 게임 로직의 이벤트 연결을 별도로 변경해야 합니다. 이번 패키지는 기존 스크립트를 바꾸지 않습니다.

기존 `JoystickLikeGear`를 사용할 경우 새 기어 Transform과 손잡이 Collider를 지정하고, 같은 기어를 두 입력 컴포넌트가 동시에 조작하지 않도록 `JourneyBoardInput` 사용 여부를 정하세요. 새 입력은 별도 이벤트 방식이며 기존 `SelectedCoinIndex`를 자동 변경하지 않습니다.

제공 코인은 Rigidbody/Convex MeshCollider가 붙은 시각·물리 샘플입니다. 현재 게임의 `BettingCoin` 등 전용 컴포넌트를 자동 대체하지 않습니다. 기존 코인 프리팹에 새 Mesh/Material을 적용하는 방법도 가능합니다.

## 수치와 표시 변경

- 공급기: `JourneyCoinStack.SetCount(int)`로 0~12개의 표시 코인 개수를 바꿉니다.
- 상태 그래프: `JourneyMoodGraphic.SetMood(float)`에 -1~1 값을 전달합니다.
- 인벤토리: `JourneyUIReferences.SetInventoryIcon(index, sprite)`를 사용합니다.
- 이번 디자인에는 Display_024 숫자판이 없습니다.

데모 컨트롤러는 입력·물리 확인용입니다. 실제 게임 장면에 적용할 때 데모 코인 생성 이벤트를 그대로 게임 규칙으로 취급하지 마세요. 미리보기 장면은 렌더용 레이어29/30을 사용합니다. 프로젝트에서 이미 이 레이어를 사용한다면 카메라 Culling Mask와 오브젝트 Layer를 함께 변경하세요. 패키지는 ProjectSettings, 레이어 이름, URP 프로젝트 설정을 덮어쓰지 않습니다.

## 검증 범위

별도 Unity 6000.3.12f1 프로젝트에서 컴파일, 프리팹·머티리얼 참조, 1152×810 외곽 프레임, 코인의 경사로 통과, 실제 렌더 및 Unity 네이티브 패키지 내보내기를 검증합니다. 최종 결과는 배포 폴더의 VALIDATION.txt / IMPORT_VALIDATION.txt를 확인하세요. 기존 게임 전체의 진행·베팅 규칙 연결은 이번 에셋 검증에 포함되지 않습니다.
