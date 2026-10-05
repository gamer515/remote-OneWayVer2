# Journey Interface V4 — 표면 음영 베이크 / URP

고정된 실제 3D 조작판의 부드러운 조명을 Blender Cycles에서 베이크하고, 움직이는 부분은 Unity 실시간 조명을 받도록 만든 별도 버전입니다. URP를 사용하며 기존 게임 프로젝트에는 자동 적용하지 않습니다.

## 확인 이미지

- `Preview/Unity_CompleteAssets_Baked.png`: 1920×1080 실제 Unity 렌더. UI 프레임의 가운데는 계속 투명합니다.
- `Preview/Unity_ControlPanel_Baked.png`: 실제 조작판 확대 렌더.
- `Preview/Blender_Reference.png`: 같은 실제 Mesh·카메라·재질 팔레트의 Blender 참고 렌더.

## 제공 에셋

`JourneyInterfaceV4_Baked_URP.unitypackage`를 Import하면 `Assets/JourneyInterfaceV4Baked`에 베이크 버전이 생깁니다. 부모 프리팹 및 기존 물리 설정을 해석할 수 있도록 기본 `Assets/JourneyInterfaceV3` 에셋도 포함합니다.

- `Prefabs/JourneyBoardAssembly_Baked.prefab`: 실제 Mesh 조작판 완성 조립체.
- `Prefabs/JourneyBoardShell_Baked.prefab`: 코인통·기어·가방을 제외한 외장.
- `Prefabs/JourneyBackpack_Baked.prefab`: 여닫는 덮개 Animation을 유지한 새 배낭.
- `Prefabs/JourneyCoin_*_Baked.prefab`: 원래 Rigidbody·Collider·물리 재질을 유지한 각 색상 동전.
- `Prefabs/JourneyLightingRig.prefab`: 참고용 두 조명과 Volume. 별도 확인용 장면에 같은 설정이 저장되어 있습니다.
- `Rendering/JourneyPipeline.asset`, `JourneyRenderer.asset`, `JourneyVolumeProfile.asset`: 베이크 표면과 접촉 음영을 확인한 URP 설정. 기존 게임의 렌더 설정을 자동 변경하지 않습니다.
- `Shaders/BakedSurfaceURP.shader`: 고정된 3D 표면에서 베이크한 조명을 읽고 약한 실시간 그림자·반사를 보태는 셰이더.
- `Textures/*.exr`: 18개 표면의 선형 HDR 베이크 텍스처와 스튜디오 반사용 HDR 큐브맵.
- `Blender/JourneyInterfaceV4_BakeSource.blend`: 실제 Unity Mesh에서 만든 수정 가능한 베이킹 원본. 별도 전체 ZIP에 있습니다.

## 적용과 확인

먼저 별도 검증 장면 `Scenes/InterfacePreview_Baked.unity`에서 확인하세요. 장면을 여는 것만으로 프로젝트의 Render Pipeline Asset이 변경되지는 않습니다. 제공한 `JourneyPipeline.asset`으로 확인하려면 Project Settings의 Graphics와 현재 Quality에서 사용하는 URP Asset을 직접 선택해야 합니다. 기존 프로젝트에서 시험할 때는 현재 렌더 설정을 보존한 상태로 비교하세요.

기존 URP Asset을 유지하는 경우에도 고정된 표면의 베이크 음영은 동작합니다. 다만 움직이는 동전·기어·가방의 밝기와 반사, 접촉 음영, 최종 색감은 게임의 조명·Volume·Renderer 설정에 영향을 받습니다. 참고 Renderer의 SSAO는 Intensity 0.45, Radius 0.18, Depth Normals, High Samples입니다. HDR Color Grading과 실제 URP PostProcessData가 연결되어 있습니다. Volume은 Neutral Tonemapping, Exposure 0, Contrast 3, Saturation -3입니다. UI까지 어둡게 하지 않도록 -0.55 스톱에 해당하는 밝기 조정은 3D 베이크 표면 Gain 0.68302와 조명에서 적용했습니다.

기존 장면에 같은 느낌을 적용하려면 메인 Camera의 Post Processing도 켜야 합니다. UI가 Screen Space - Camera라면 색 보정의 영향을 받을 수 있습니다. 검증 장면의 빈 화면은 투명한 프레임 뒤에 보이는 HDR 종이색 배경이며, 프레임의 가운데에 종이 이미지를 채운 것이 아닙니다. 실제 게임 화면은 기존 콘텐츠가 그대로 비칩니다.

이 버전은 `*_Baked` 프리팹 또는 포함된 베이크 좌표 Mesh를 함께 사용해야 합니다. 새 베이크 Material만 기존 V3 Mesh에 넣으면 UV2가 맞지 않아 표면이 잘못 보일 수 있습니다. 프리팹 내부의 연결 지점은 V3와 같으므로 기존 게임 컴포넌트에서 해당 오브젝트를 참조하는 방식은 그대로입니다.

3D 오브젝트의 참조 지점과 부모/자식 경로는 V3와 같습니다. 기존 컴포넌트의 기어, 노란 버튼, 코인 생성 지점과 가방 참조는 해당 오브젝트를 연결하세요. 패키지가 게임 입력이나 코인 배출 규칙을 새로 구현하지 않습니다.

## 실제 변경 범위

고정된 외장·보드·코인통·트레이·큰 연결 경사로 8개 Renderer에는 넓은 면광원 3개와 간접광으로 계산한 표면 조명을 저장했습니다. 화면 전체 이미지를 3D판 대신 붙이는 방식이 아닙니다. Mesh에 개별 베이크 좌표를 추가하여 각 표면에서 텍스처를 읽습니다.

고정 표면은 GPU 128 샘플로 다시 베이크했습니다. 움직이는 부분의 자체 AO는 16 샘플이며, 실시간 SSAO와 조명을 함께 사용합니다. 정적인 베이크 재질에는 원래 그림자를 다시 강하게 씌우지 않도록 약한 실시간 그림자 20%와 접촉 AO 30%만 추가합니다.

가방 몸체와 덮개, 공급 버튼, 기어, 노란 버튼은 자기 표면의 약한 AO를 색상 텍스처에 저장하고 실시간 조명을 받습니다. 움직이는 덮개의 닫힌 그림자를 주변 판에 베이크하지 않았습니다. 단독 동전은 자기 음영 AO와 실시간 PBR 조명을 사용합니다.

기하 형상·삼각형 위치·물리 Collider·경사로 크기·Rigidbody 설정·가방 Animation은 유지했습니다. 베이크 좌표의 이음새를 위해 렌더 Mesh의 정점만 삼각형별로 분리했고 물리 Collider는 원래 Mesh를 사용합니다. 원래 Mesh의 중복 또는 면적 0인 일부 가방 삼각형은 베이킹 원본에서만 정리했으며, Unity Mesh에는 그대로 보존했습니다.

3D 조립체에는 Camera와 C# 런타임 스크립트가 없습니다. RenderTexture 에셋이나 코인판용 별도 렌더 카메라도 없습니다. 메인 카메라가 실제 Mesh를 직접 봅니다. 미리보기 저장에는 임시 렌더 버퍼만 사용했습니다. UI 테두리 PNG와 상단 16px 정렬은 V3와 같습니다.

Blender의 AgX와 Unity의 Neutral 색 변환은 동일하지 않으므로 두 참고 렌더가 모든 픽셀에서 일치하는 버전은 아닙니다. 베이크는 고정된 조명의 인상을 유지하는 방식이며, 게임에서 해가 이동하는 등 조명 상황이 크게 바뀌어도 고정 판의 베이크 명암은 그대로 남습니다.
