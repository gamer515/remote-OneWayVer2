# JourneyInterfaceV4Baked 외형 교체

DecisionScene의 기존 V3 런타임 인스턴스에서 메시와 머티리얼만 V4Baked로 교체했다. 참조 안정성을 위해 `JourneyInterfaceV3_Runtime` 이름은 유지한다.

- 72개 MeshFilter의 외형 교체. 코인통 재고 표시를 유지하기 위해 V4의 베이크 UV를 보존한 분리 메시 60개를 `Assets/Decision_YYS/JourneyV4Integration`에 저장했다.
- 공급 코인 프리팹 4종은 V4Baked로 연결했다. 원본 V3/V4 코인의 Transform, Collider, Rigidbody 및 스크립트 설정이 동일함을 비교했다.
- 기존 1,172개 비외형 컴포넌트 설정을 교체 전후 비교했다. 공급 코인 프리팹 참조 외에 변경 없음.
- 배치, UI 프레임, 카메라, 콜라이더, 이벤트, 가방 클립, 대련 설정 및 NPC 만남 거리 4유닛 유지. V4의 조명 리그/렌더 파이프라인/Volume은 적용하지 않았다.
- Assets 아래 C# 및 애니메이션 파일 161개의 작업 전후 SHA256이 동일하다. 기존 사용자 수정도 유지했다.

## 검증 (2026-10-05)

- 저장된 씬을 PreviewScene으로 다시 읽어 메시/머티리얼 72개와 코인 프리팹 4개 참조 유지 확인.
- 버튼 6개 Raycast 검출, 기존 노란 버튼/기어 이벤트 각 1개, EventSystem 1개, 누락 스크립트 0개.
- 저장을 격리한 Play 테스트에서 4종 재고/통 표시 10→9, 코인 출구 위치 일치, 가방 클릭 Collider, 대련 시작/기사 복제 위치 및 0.6 크기/입력 잠금과 복원 확인.
- 가방 열림 클립의 재생 명령은 확인했지만 열린 각도와 패널 표시 완료는 이번 테스트에서 검증하지 못했다. 전체 Main 스토리 플레이 및 가방의 완료 동작은 별도 확인 항목이다.
- 테스트 중 Editor Inspector의 SerializedObjectNotCreatableException 3건을 캡처했다. 스택은 RectTransformEditor/GraphicEditor/GameObjectInspector이며 게임 스크립트 오류는 새로 캡처되지 않았다.
- 테스트 이후 Play 종료, 스토리 매니저 활성 상태 복원 및 DecisionScene 저장 완료. 실제 사용자 저장 데이터는 변경하지 않았다.

교체 전 백업: `Temp/JourneyV4Migration/DecisionBefore-20261005-121406.unity` (Temp는 Git 대상이 아님).

적용 화면: `Temp/JourneyV4Migration/after.png`.

재현 도구: `AgentScripts/JourneyV4BakedMigration.cs`. 이미 생성된 V4Integration 폴더가 있으면 Migrate는 중복 적용/덮어쓰기를 거부한다. Validate는 검증, SaveAndVerify는 DecisionScene 저장 및 외형 참조의 재로딩 검증용이다.

## 가변 재고 코인 음영 보정 (2026-10-05)

- V4 조립체는 14개 동전이 서로 가리는 그림자까지 베이크되어 있어, 게임의 초기 10개 표시에서 노출된 윗면이 검게 남았다. 베이크 텍스처 샘플 기준 10번째 동전 윗면 밝기는 약 0.035~0.043, 14번째는 약 0.70~0.80이다.
- 재고 동전 Renderer 56개만 `StockCoinGold_Realtime.mat`으로 교체했다. V4 단독 코인의 URP/Lit 금색을 복제하고, 재고 메시와 UV 배치가 다른 단독 코인의 AO 텍스처는 제거했다. 현재 씬의 실제 조명과 그림자는 유지한다.
- 코인통 외장 및 조작판의 V4 베이크 재질, 메시, 콜라이더, 재고 규칙은 변경하지 않았다. 재고 Renderer 이외 컴포넌트 1,578개의 직렬화 설정이 작업 전후 동일함을 확인했다.
- 네 종류 각각 14/10/9/1/0개 표시와 노출된 윗면의 실시간 재질 연결을 검증한 뒤 원래 재고/활성 상태로 복원했다. 저장된 씬을 다시 읽어 재질 참조가 유지되는 것도 확인했다.
- Assets 아래 C# 및 애니메이션 161개 파일 해시가 동일하다. 기존 씬의 사용자 변경은 백업 및 저장 시 유지했다. 새로운 Inspector 조절 항목이나 게임 코드는 추가하지 않았다.
- 보정 전 백업: `Temp/JourneyV4Migration/BeforeStockLighting-20261005-123314.unity`.
- 보정 화면: `Temp/JourneyV4Migration/stock-after.png`. 기존 베이크 그림자는 제거되지만 실제 씬 조명에 따른 자연스러운 명암은 남는다.
- 도구: `AgentScripts/JourneyStockCoinLighting.cs`. 이후 전체 표면이 베이크 재질이라는 기존 `JourneyV4BakedMigration.Validate`의 allBaked 값은 재고 동전 때문에 false가 정상이다. 재고용 검증은 새 도구 Validate를 사용한다.
