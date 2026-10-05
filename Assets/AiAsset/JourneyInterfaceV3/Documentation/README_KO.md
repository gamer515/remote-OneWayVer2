# Journey Interface V3 — 원형 코인통·여행 배낭·재질 수정

승인한 시안을 기준으로 만든 Unity 6000.3.12f1 / URP 17.3용 독립 에셋입니다. 기존 DecisionScene과 게임 스크립트에 자동 적용하지 않습니다.

## 받는 파일

- `JourneyInterfaceV3_Unity6_URP.unitypackage`: 실제 3D 조작판 프리팹, URP 머티리얼, 네 색상 코인, Collider/Rigidbody와 별도 물리 설정, PNG 4장, Image만 사용하는 배치 프리팹, 확인용 장면.
- `JourneyInterfaceV3_UI_Only.unitypackage`: 화면 테두리와 구분 그림, Image 배치 프리팹만 필요할 때.
- `JourneyInterfaceV3_CoinPhysics.unitypackage`: 기존 코인에 물리 설정만 적용할 때.
- `PNG/`: 각 Image의 Source Image에 넣는 독립 PNG 4장.
- `Blender/JourneyInterfaceV3.blend`, `JourneyBoardAssembly.fbx`, `JourneyCoin.fbx`: 수정 가능한 모델 원본과 교환용 모델. .blend의 텍스처는 내부에 포함되어 있습니다.

원형 코인통은 속이 빈 원형 입구·세로 금속 가이드·완전한 원판 동전으로 구성했습니다. 원판을 가리던 뒤쪽 직사각형 지지대를 뒤로 물려, 동전 윗면이 반원으로 잘리지 않습니다. 지지대와 원형 입구 사이의 실제 Mesh 간격도 확인합니다.

나무는 넓은 손그림 결을 가진 따뜻한 오크, 가방은 짜임 있는 천과 밤색 가죽, 금속은 차분한 황동으로 다시 만들었습니다. 색상 텍스처와 노멀맵이 함께 들어 있습니다.

최종 확인 결과는 `VALIDATION.txt`, 새 프로젝트에서의 패키지 가져오기 결과는 `IMPORT_VALIDATION.txt`에 있습니다.

## 1. 실제 3D 조작판

패키지를 Import하면 `Assets/JourneyInterfaceV3/Prefabs/JourneyBoardAssembly.prefab`가 생깁니다. 실제 Mesh로 구성되어 있고 Camera, RenderTexture, UI를 그리는 스크립트, 입력/코인 생성 스크립트는 들어 있지 않습니다. 메인 카메라가 직접 봅니다.

기존 저장된 프로젝트의 조건에 맞춰 전체 Transform은 Position `(0.9, -8.14, -2.55)`, Rotation `(0,0,0)`, Scale `(1,1,1)`입니다. 원래 `_Gameplay3D/3D_UI`와 함께 보이면 외장이 중복되므로 새 조작판을 시험할 때 기존 조작판의 해당 외장 Renderer를 잠시 비활성화하세요. 기존 전체 루트의 스크립트까지 일괄 끄는 방식은 피하세요. 이 패키지가 기존 참조를 자동 변경하지 않습니다.

기준 메인 카메라는 Position `(0.9,1,-10)`, Rotation `(30,0,0)`, Orthographic Size `5`, 화면 `1920×1080`입니다. 하단 외장이 좌우와 화면 아래까지 이어지도록 여유 있는 폭으로 제작했으며, 앞쪽 외장 일부가 화면 밖으로 이어지는 것은 시안에서 요청한 마감입니다. UI Transform Scale을 키워 맞추는 구조가 아닙니다.

`JourneyBoardShell.prefab`는 기존 코인통·기어·노란 버튼·가방을 유지해 배치하기 위한 외장 버전입니다. 완성 조립체에는 새 원형 코인통 네 개와 새 여행 배낭이 들어 있습니다. 배낭은 Blender와 Unity가 같은 실제 모델을 사용하며, 기존 가방을 복제한 임시 모델이 아닙니다. 단독으로 쓰는 `JourneyBackpack.prefab`도 제공합니다. V3는 `Assets/JourneyInterfaceV3` 폴더에 들어가므로 V2 에셋을 덮어쓰지 않습니다.

## 2. 기존 컴포넌트 연결 지점

프리팹 내부 오브젝트를 Inspector의 기존 참조 필드에 지정하세요. 이 에셋 자체가 버튼 이벤트나 게임 규칙을 새로 구현하지는 않습니다.

| 목적 | 조립체 내부 오브젝트 |
|---|---|
| 기어 회전 Transform | `GearPivot` |
| 기어 손잡이 클릭 Collider | `GearPivot/GearHandle`의 SphereCollider |
| 노란 버튼 클릭 Collider | `YellowButton/YellowButtonCap`의 MeshCollider |
| 파란 공급 버튼 Collider | `CoinSupplyRack/Canister_Blue/SupplyButton_Blue/ButtonBrassRim` |
| 나머지 공급 버튼 | 같은 경로의 `Red`, `Yellow`, `Teal` |
| 동전 배출 위치 4개 | 각 Canister의 `CoinSpawn_Blue`, `CoinSpawn_Red`, `CoinSpawn_Yellow`, `CoinSpawn_Teal` |
| 짧은 출구 끝 위치 | 각 Canister의 `OutletEnd_...` |
| 왼쪽 연결 경사로 입구/끝 | `TransferEntry`, `TransferExit` |
| 새 가방을 놓는 기준 | `BackpackMount` |
| 가방 Animation 컴포넌트 | `BackpackMount/JourneyBackpack` |
| 가방 덮개 회전 Transform | `BackpackMount/JourneyBackpack/LidPivot` |

기존 `JoystickLikeGear`의 gear3D에는 `GearPivot`, gearHandleCollider에는 손잡이 Collider를 연결하는 구조입니다. 코인 생성 함수에는 선택한 종류의 `CoinSpawn_...` 위치를 사용할 수 있습니다. 기존 스크립트의 이벤트/직렬화 필드 구조에 따라 연결해야 하므로, 단순 모델 Import만으로 클릭 배출 기능이 생기는 것은 아닙니다.

가방에는 `OpenLid`, `CloseLid`라는 Legacy Animation 클립을 제공합니다. `LidPivot`을 0°↔65°로 0.45초 동안 여닫으며 자동 재생은 꺼져 있습니다. 기존 가방 동작 코드가 특정 오브젝트 이름이나 Animation 필드에 의존한다면 새 Animation 컴포넌트와 클립 이름을 연결해야 합니다. 코인 저장이나 인벤토리 규칙은 구현하지 않았습니다.

작은 출구는 길이 약 `0.657` Unity 단위, 경사 약 `21.8°`, 바닥 폭 `0.585`입니다. 큰 왼쪽 경사로는 별개이며 단축하지 않았습니다. 양쪽 가이드가 있고 앞 끝은 열려 있습니다. 통합 외장 Collider가 코인 통로를 덮지 않도록 부위별로 나눴습니다. 트레이에 도착한 코인이 왼쪽 보드까지 자동 이동하는 규칙은 추가하지 않았습니다. 기존 게임이 동전을 왼쪽 경사로 입구까지 보내면 내리막으로 보드에 넘어갑니다.

## 3. Image용 PNG

PNG는 표시 크기의 두 배 해상도로 만들었습니다. 테두리는 중앙 Alpha가 정확히 0이며 장면·얼굴·수치·3D 조작판이 포함되어 있지 않습니다.

| 파일 | PNG 원본 | 권장 RectTransform | 1920×1080 기준 좌상단 |
|---|---:|---:|---:|
| `Frame_Story_1065x778.png` | 2130×1556 | 1065×778 | (18.22,16) |
| `Frame_Map_370x556.png` | 740×1112 | 369.52×556.12 | (1159.01,16) |
| `Frame_Face_384x520.png` | 768×1040 | 384×520 | (1536,16) |
| `Divider_70x1080.png` | 140×2160 | 68.78×1080 | (1083.22,0) |

세 화면 테두리의 상단 여백은 모두 `16px`입니다. 얼굴 테두리를 아래로 `16px` 이동했으며 크기는 `384×520`으로 유지했습니다. 좌상단 Anchor/Pivot `(0,1)`을 사용할 때 세 테두리의 Anchored Position Y는 모두 `-16`입니다. 수정 확인 결과는 `UI_ALIGNMENT_VALIDATION.txt`에 있습니다.

1152×810은 왼쪽 Encounter_Display 외곽이며, 실제 Walking_View는 1065×778입니다. 현재 영상의 크기를 유지하려면 테두리를 실제 Walking_View 위에 같은 크기의 Image로 올리세요.

테두리 Image는 Type `Sliced`, Fill Center `Off`, Raycast Target `Off`, Color `White`, Transform Scale `(1,1,1)`로 사용합니다. Import된 Sprite는 Pixels Per Unit `200`, Border `96px`로 설정되어 있습니다. Sliced 모드에서는 크기를 조금 바꿔도 모서리 장식이 찌그러지지 않습니다. 구분 그림은 Type `Simple`로 사용합니다.

구분 그림은 아래 약 275px 구간을 투명하게 비워 하단 3D 경사로를 가리지 않습니다. 위쪽에는 작은 나침반, 점선 길과 나무 스케치만 들어 있습니다.

편의용 `UIOverlay_1920x1080.prefab`를 기존 `Decision_UI_Root` 아래에 넣고 가장 마지막 형제로 배치하면 위 좌표로 Image 4개를 올립니다. 이 프리팹에는 Canvas나 별도 입력 컴포넌트가 없으므로 기존 Canvas 안에서 사용하세요. 기존 Canvas Scaler의 Reference Resolution `1920×1080`을 유지합니다.

PNG만 직접 가져오면 Texture Type `Sprite (2D and UI)`, Sprite Mode `Single`, Pixels Per Unit `200`, Mipmap `Off`, Alpha Is Transparency `On`, Max Size `4096`, Compression `None`을 지정하세요. 테두리의 Sprite Border는 좌/하/우/상 모두 `96px`입니다.

## 4. 코인 물리 설정

`CoinSlide.physicMaterial`은 코인과 경사로 Collider에, `BoardSurface.physicMaterial`은 보드·트레이 Collider에 사용합니다. 물리 설정 패키지는 별도로 제공됩니다.

| 항목 | 값 |
|---|---:|
| Mass | 0.025 |
| Linear Damping | 0.01 |
| Angular Damping | 0.1 |
| Use Gravity | On |
| Is Kinematic | Off |
| Interpolation | Interpolate |
| Collision Detection | Continuous Dynamic |
| Solver Iterations / Velocity Iterations | 12 / 4 |
| 코인 반지름 / 두께 | 0.22 / 0.08 |
| CoinSlide Dynamic / Static Friction | 0.055 / 0.065 |
| Bounciness | 0 |
| Friction Combine / Bounce Combine | Minimum / Minimum |
| BoardSurface Dynamic / Static Friction | 0.28 / 0.35 |

제공 코인 프리팹에는 Convex MeshCollider와 Rigidbody가 있습니다. 기존 코인에는 `Coin_Rigidbody.preset`에서 필요한 항목을 적용한 뒤 Collider의 Physics Material을 따로 지정하세요. 기존 드래그 스크립트가 Is Kinematic이나 Constraints를 바꾼다면 배출할 때 물리 상태를 복구해야 합니다. Input Manager/Input System 설정은 바꾸지 않습니다.

## 제작 및 검증

Blender에서 실제 모델과 FBX를 만들고, 별도 Unity 프로젝트에서 네 출구의 동전 하강과 왼쪽 통로 통과를 물리 시뮬레이션으로 확인했습니다. 정적 외장의 시각 Mesh는 그룹 단위로 합쳐 렌더러 수를 줄였으며, 버튼·기어 피벗과 배출 위치는 따로 유지했습니다. UI는 이미지 생성 도구로 만든 테두리 원화를 9-slice 규칙으로 내보낸 PNG이며 런타임 코드로 그리지 않습니다.

`Preview/ApprovedConcept.png`는 승인한 합성 시안, `Board_ThreeQuarter.png`는 실제 Blender 모델 렌더입니다. `Backpack_Detail.png`와 `Canister_Detail.png`는 수정된 모델의 확대 렌더입니다. `Unity_ProjectCamera.png`는 실제 Unity 3D 조작판만 표시한 화면, `Unity_CompleteAssets.png`는 같은 조작판에 테두리 PNG 3장과 구분 그림을 Image로 배치한 화면입니다. 위쪽 화면의 내용은 비워 두었습니다. 시안에 있던 게임 장면과 얼굴은 사용자 프로젝트의 콘텐츠이므로 새 PNG에 구워 넣지 않았습니다.

확인용 `InterfacePreview_Direct3D.unity` 장면에는 조작판과 Image가 함께 배치되어 있습니다. 원래 프로젝트와 같은 Linear 색상 공간에서 촬영했습니다. 미리보기는 부드러운 방향광과 금속 확인용 환경 반사를 사용하며, 기존 게임의 조명 아래에서는 색감이 달라질 수 있습니다. 이 확인 장면을 열어도 기존 DecisionScene의 오브젝트나 참조가 바뀌지는 않습니다. 승인한 시안은 표현 방향을 정하는 그림이며, 실제 모델의 모양과 재질은 Unity 확인 화면을 기준으로 판단하세요.
