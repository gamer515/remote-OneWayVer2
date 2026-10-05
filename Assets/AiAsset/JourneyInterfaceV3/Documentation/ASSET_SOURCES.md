# V3 에셋 원본과 변경 범위

- Blender 5.2.2에서 코인통 원형 입구와 가이드, 새 천·가죽 배낭, 버클·봉제선·지도 두루마리를 실제 Mesh로 제작했습니다.
- V2의 UI 테두리 원화와 나침반·나무 스케치, 하단 조작판 배치 및 기존 코인 통로를 재사용했습니다.
- 재질 원화는 built-in image_gen으로 만든 MaterialAtlas_Source.png입니다. 그 4개 영역을 Unity 에셋 준비 단계에서 개별 albedo 텍스처로 추출하고, 높이 변화로 노멀맵을 만들었습니다.
- Unity 프리팹, Collider, Rigidbody, 물리 재질과 가방 Animation 클립은 격리된 제작 프로젝트에서 Unity API로 생성했습니다. 제공 폴더에 C# 스크립트는 없습니다.
- 전체 배치 그림 ApprovedConcept.png는 과거 시안입니다. 실제 제작 결과는 Unity_CompleteAssets.png를 기준으로 확인하세요. 확대 렌더는 실제 Blender 모델이며 AI로 덧칠한 그림이 아닙니다.
- 원래 Assets, DecisionScene, 프로젝트 설정, 입력 방식과 기존 게임 스크립트는 수정하지 않았습니다.
