# PNG UI / 코인 물리 설정

이번 파일은 이전 JourneyMapKit과 분리되어 있습니다. 기존 게임 프로젝트는 수정하지 않았습니다. UI를 실행 중 스크립트로 그리지 않습니다.

## UI 사용

`Journey_Image_UI.unitypackage`를 가져오면 `Assets/JourneyImageUI/PNG`에 Sprite로 설정된 PNG가 생깁니다. 원하는 파일을 기존 Unity UI Image의 Source Image에 드래그하세요. 포함된 Prefabs도 RectTransform / CanvasRenderer / Image만 사용하는 편의용 오브젝트입니다. 커스텀 C# 스크립트는 없습니다.

- `Background_Project_1920x1080`: 프로젝트 크기 기준 전체 종이 배경과 프레임. 왼쪽 이야기 외곽은1152×810입니다.
- `Background_Approved_1920x1080`: 하단 조작판 공간이 더 넓었던 시안 비율의 배경.
- `Story_Paper_1152x810`: 이야기용 종이·테두리·지도 스케치가 함께 있는 이미지.
- `Frame_Transparent_384x440`: 가운데가 투명한 테두리. 기존 내용을 가리지 않고 위에 올릴 수 있습니다.
- `Panel_Paper_384x440`, `Landscape_Paper_384x590`, `Reputation_Paper_384x150`: 개별 종이 패널.
- `Slot_Paper_48x220`: 인벤토리 한 칸. 필요한 만큼 배치하세요.
- `Frame_9Slice_256`: 가운데가 투명한 공용 테두리. **Image Type=Sliced**, Fill Center는 필요에 맞게 설정. Sprite Border는 좌/하/우/상 모두24px로 저장되어 있습니다.
- `Paper_Texture`: 종이 원본 텍스처.

전체 배경 Image는 Canvas의 콘텐츠보다 먼저 그려지도록 Hierarchy에서 앞쪽 형제로 두세요. 이야기 텍스트, 캐릭터·풍경 RenderTexture, 상태 그래프, 코인과 조작판은 배경 위에 기존 오브젝트를 그대로 사용합니다. 이번 배경에는 게임 수치·상태 그래프·얼굴·풍경·3D 조작판을 굽지 않았습니다.

화면 기준1920×1080. Canvas Scaler는 Scale With Screen Size, Reference Resolution1920×1080. UI Transform Scale은1을 유지하고 RectTransform으로 배치하세요. 전체 배경은 Image Type=Simple, RectTransform1920×1080로 사용합니다. 다른 화면 비율에서는 배경을 무조건 늘리기보다 개별 패널과 앵커로 대응하세요.

**1152×810은 이야기 프레임 이미지의 외곽 크기입니다.** 스케치·테두리 여백이 포함되어 있습니다. 기존 이야기 내용1152×810 자체를 그대로 유지하고 싶다면 투명 테두리를 그 위에 올리거나 프레임을 더 크게 배치하세요.

PNG만 담은 ZIP으로 가져올 경우 Texture Type=Sprite (2D and UI), Sprite Mode=Single, Alpha Is Transparency=On, Generate Mip Maps=Off, Max Size=2048을 지정하세요. 9-slice 파일은 Sprite Editor에서 Border24px을 추가해야 합니다. unitypackage에는 이미 설정되어 있습니다.

## 코인 물리 설정 — 별도 패키지

`Journey_Coin_Physics.unitypackage`는 `Assets/JourneyCoinPhysics`에 다음 파일만 넣습니다. 게임 입력·코인 생성 스크립트는 포함하지 않습니다.

1. `CoinSlide.physicMaterial`: 코인 Collider와 경사로 Collider의 Material에 할당.
2. `BoardSurface.physicMaterial`: 평평한 보드와 트레이 Collider의 Material에 할당.
3. `Coin_Rigidbody.preset`: 기존 코인의 Rigidbody Inspector 우측 위 Preset 선택 아이콘에서 적용.
4. `Settings.json`: 같은 설정값을 적은 참조 파일. 자동으로 실행되거나 적용되지는 않습니다.

Preset은 기존 Rigidbody 값을 바꾸므로 현재 설정을 유지해야 하는 항목이 있다면 아래 표에서 필요한 값만 직접 입력하세요. Collider와 Physics Material은 Preset으로 자동 연결되지 않습니다.

| Rigidbody 항목 | 값 |
|---|---|
| Mass | 0.025 |
| Linear Damping | 0.01 |
| Angular Damping | 0.1 |
| Use Gravity | On |
| Is Kinematic | Off |
| Interpolate | Interpolate |
| Collision Detection | Continuous Dynamic |
| Constraints | None |

| Physics Material 항목 | CoinSlide: 코인/경사로 | BoardSurface: 보드/트레이 |
|---|---|---|
| Dynamic Friction | 0.055 | 0.28 |
| Static Friction | 0.065 | 0.35 |
| Bounciness | 0 | 0 |
| Friction Combine | Minimum | Average |
| Bounce Combine | Minimum | Average |

검증에 사용한 코인은 반지름0.22, 두께0.08 Unity 단위이며, MeshCollider는 Convex=On, Is Trigger=Off입니다. 기존 코인 메시에 맞는 Collider를 사용하세요. 경사로와 보드 Collider는 Is Trigger=Off로 둡니다. 테스트 경사로는 약10.78°입니다.

위 설정은 이전 데모에서 코인이 왼쪽 보드로 넘어갈 때 사용했던 값입니다. Friction Combine이Minimum인 코인과 접촉하면 보드에서도 낮은 마찰이 선택될 수 있어 비교적 오래 미끄러집니다. 게임의 코인 크기·경사·끌기 스크립트가 다르면 같은 움직임이 보장되지는 않습니다. 기존 드래그 코드가 Rigidbody의 Is Kinematic 등을 바꾼다면 드래그 종료 시 물리 상태도 맞게 복구해야 합니다.

이 패키지는 프로젝트의 Input Manager 설정을 변경하지 않습니다. 이전 데모/커스텀 UI 프리팹을 사용할 필요 없이 새 PNG 또는 Image-only 프리팹을 기존 Canvas에 적용하면 됩니다. 이전 프레임을 새 Image로 교체한 뒤에는 해당 오브젝트의 JourneyFrameGraphic이 필요하지 않습니다. 다른 기존 오브젝트가 사용 중인 스크립트 파일까지 일괄 삭제할 필요는 없습니다.
