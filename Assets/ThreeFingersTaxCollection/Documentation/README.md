# ThreeFingers 세금 회수 클립

기존 ThreeFingers 리그와 원본 나무 통·저울·코인으로 제작. 원본 프리팹/애니메이션 및 게임 씬은 수정하지 않았습니다. 패키지는 현재 게임과 같은 URP 프로젝트용이며 UPM 패키지 소스는 포함하지 않습니다.

## 재생

- Scenes/ThreeFingers_TaxScaleCollection_Preview.unity: 일반세 저울 회수, 60fps, 3초(0–180프레임).
- Scenes/ThreeFingers_DirectCoinCollection_Preview.unity: 직접 집기, 60fps, 1.2초(0–72프레임).
- 각 씬에서 Play. Animator는 한 번 재생하고 마지막 자세에서 멈춥니다. SideCamera를 켜면 옆에서 확인할 수 있습니다.
- 게임 씬에는 Prefabs의 해당 Rig를 배치하거나 복제해 사용합니다. 기존 Hand 프리팹의 Animator에 클립만 넣으면 경로가 맞지 않습니다. 이 클립은 Rig 루트의 Animator에서 Hand/... 본 경로를 제어합니다.

## 연결 프레임(0부터 시작)

|클립|프레임|초|AnimationEvent|연결 작업|
|---|---:|---:|---|---|
|일반세|30|0.50|OnTaxCoinsPicked|플레이어 접시 코인을 Hand/CoinStackGrip에 연결|
|일반세|75|1.25|OnTaxCoinsDeposited|손에서 코인 해제, 통 내부로 낙하 시작|
|일반세|87|1.45|없음|코인 낙하 완료, 통 바닥에 안착|
|일반세|120|2.00|OnTaxBowlPicked|통을 Hand/BowlRimGrip에 연결|
|일반세|180|3.00|OnTaxCollectionComplete|NPC 인계 위치 도착·정지|
|직접|30|0.50|OnDirectCoinsPicked|납부 코인 더미를 Hand/CoinStackGrip에 연결|
|직접|72|1.20|OnTaxCollectionComplete|NPC 인계 위치 도착·정지|

## 실제 게임에 연결

Rig 루트는 월드 위치/회전/균일 스케일을 조절할 수 있습니다. 전체 경로는 예제 저울·통 배치 기준이며 현재 게임의 UI 카메라 배치에 자동으로 맞추는 기능은 포함하지 않습니다. PlayerCoinStart, Bowl, NpcHandoff를 기준으로 실제 배치와 정렬하고 손 경로를 조정하세요. 프리팹의 Hand 자식 및 본 이름은 유지합니다.

미리보기 PaymentCoins와 Bowl에는 ParentConstraint가 설정되어 있습니다. 코인에는 손/통 두 소스가 있고, 75프레임에 손→통으로 전환합니다. 75–87프레임의 통 기준 오프셋 커브가 낙하를 표현합니다. 통은 120프레임까지 고정되고 그 이후 손을 따라가며 수평을 유지합니다. 실제 오브젝트로 교체할 때는 프리팹의 같은 이름 루트/제약을 유지하고 그 아래 시각 모델을 교체하는 방식이 가장 간단합니다.

별도 게임 로직으로 연결하려면 Animator가 있는 Rig 루트의 컴포넌트에 표의 이벤트와 같은 메서드를 구현하세요. intParameter는 프레임 번호입니다. 이벤트는 DontRequireReceiver로 저장되어 수신기가 없더라도 오류가 발생하지 않습니다. 집는 프레임의 월드 위치/회전을 유지한 상태에서 그립 오프셋을 계산해야 위치가 튀지 않습니다. 원본 그릇 프리팹은 별도로 보존되어 있습니다.

## 검증과 한계

분리된 Unity 프로젝트에서 두 클립의 정상 Animator 자동 재생, 매 프레임 네이티브 ParentConstraint 연결, 연결 시 순간이동, 끝 자세 유지, 통 수평, 접근/운반 중 손 메시와 통 벽 관통을 확인했습니다. 검증용 캡처는 Built-in 렌더러로 원본 색상을 보존해 촬영했고 게임용 씬은 URP 원본 재질을 참조합니다. 실제 게임의 세금 처리/코인 개수 차감/NPC 완료 처리는 연결하지 않았습니다.

제작 도구: AssetBuild/ThreeFingersTaxCollection/TaxCollectionAuthoring.cs. 기존 손 리그에 호환되는 Blender 원본이 없어 Unity에서 제작했습니다. 프레임별 결과: output/ThreeFingersTaxCollection/validation.json. 생성 도구와 보고서는 재현을 위해 보존합니다.
