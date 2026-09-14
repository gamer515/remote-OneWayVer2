# 베팅판 에셋 초안 v1

현재 프로젝트의 코인 트레이, 세 가지 색 버튼, 코인 카운터, 기어 레버 구성을 참고해 만든 별도 디자인 초안입니다. 짙은 본체, 황동 테두리, 청록색 트레이, 파랑·빨강·노랑 버튼을 사용했습니다.

## 파일

- `Concept_Draft.png`: 내장 imagegen으로 생성한 목표 디자인 이미지. 실제 모델을 촬영한 이미지는 아닙니다.
- `Actual_Model_Preview.png`: 전달하는 3D 메시를 조명 아래 렌더링한 미리보기. Unity에서 캡처한 화면은 아닙니다.
- `Top_View.png`: 같은 모델의 윗면 렌더링.
- `BettingBoard_Concept_v1.unitypackage`: URP용 재질 10개, 네이티브 메시 14개, 조립된 프리팹 1개. 런타임 코드나 자동 실행 코드는 없습니다.
- `UnitySource/Assets/BettingBoard_Concept_v1/`: 패키지에 들어 있는 원본 에셋과 `.meta` 파일.
- `BettingBoard.glb`: 계층 구조와 PBR 재질을 포함한 범용 3D 모델.
- `BettingBoard.obj`, `BettingBoard.mtl`: 다른 3D 편집 도구에서 열 수 있는 조립 모델과 재질. 두 파일은 같은 폴더에 두세요.
- `asset-stats.json`: 메시 수와 검증 범위.
- `PROMPT.txt`: 이미지 시안에 사용한 프롬프트.

## Unity에 나중에 가져올 때

현재 작업에서는 Unity를 열거나 프로젝트를 수정하지 않았습니다. 파일은 모두 프로젝트 바깥에 저장했습니다.

사용할 때는 `.unitypackage`를 가져온 뒤 `Assets/BettingBoard_Concept_v1/Prefabs/BettingBoard_Concept_v1.prefab`을 원하는 씬에 배치할 수 있습니다. 대상은 현재 프로젝트에서 확인한 Unity 6000.3.12f1 및 URP 17.3.0입니다. URP Lit 재질을 사용합니다.

이 프리팹은 시각 에셋 초안입니다. 기존 베팅 로직과 입력 컨트롤러를 포함하거나 자동으로 연결하지 않습니다. 기존 프리팹을 그대로 교체하는 패치가 아니며, 새 배치에 맞게 위치·카메라·코인 물리 영역을 조정해야 합니다.

## 움직이는 부품

- `Button_Blue`, `Button_Red`, `Button_Yellow`: 버튼 뿌리마다 별도 메시와 BoxCollider. 이름은 현재 BettingButtonController에서 찾는 이름에 맞췄습니다. 로컬 Y축으로 누를 수 있게 피벗을 분리했습니다.
- `Gear_Stick`: 기어 밑동이 회전 중심입니다. 손잡이에 `Gear_Handle_Collider`가 있으므로 기존 JoystickLikeGear의 손잡이 Collider 슬롯에 명시적으로 연결할 수 있습니다.
- `Display_024`: 예시 숫자 024의 별도 메시입니다. 실제 카운터로 쓰려면 이 노드를 숨기고 동적 텍스트 또는 숫자 표시기를 연결하세요.
- `Demo_Coins`: 시각 확인용 코인 3개입니다. 실제 게임 코인과 물리 로직은 포함하지 않습니다. 필요 없으면 이 노드를 숨기세요.
- `Coin_Collider_Boundary`: 바닥과 벽의 단순 충돌 영역.
- `Coin_Exit`, `Exit_Point`: 코인 위치 연결을 위한 기준점입니다. 동작은 기존 게임 시스템에서 연결해야 합니다.

## 규모와 검증 범위

외곽 본체는 가로 9, 깊이 5 Unity 단위입니다. 세부 치수는 메시와 `asset-stats.json`을 참고하세요. 총 37,396 삼각형, 27,466 정점입니다. 고정 장식은 재질별로 합치고 버튼과 레버는 분리했습니다. 별도 텍스처 없이 URP Lit 재질로 색, 금속성, 매끄러움을 조절합니다.

메시 인덱스, 정점·노멀 데이터, 패키지 경로, 프리팹 참조를 파일 수준에서 검사했습니다. Unity 에디터 안에서 임포트·렌더·충돌·입력 실행 검증은 하지 않았습니다. 프로젝트를 수정하지 말라는 요청을 지켰습니다.

시안 이미지의 미세한 표면 질감과 코인 각인은 목표 표현입니다. 3D 에셋의 실제 구현 범위는 `Actual_Model_Preview.png`와 `Top_View.png`에서 확인할 수 있습니다. 최종 게임 화면의 광택과 색감은 조명 및 후처리에 따라 달라집니다.
