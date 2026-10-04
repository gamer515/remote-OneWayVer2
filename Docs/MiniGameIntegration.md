# DecisionScene 미니게임 통합 / 수정 안내

## 본 게임에서 시작하기

`MainMenuScene`의 시작 버튼 → `DecisionScene` → NPC의 `Story` 대화 → 마지막 Choice에서 대련 선택 → `duelist/Duel/Start/Story.json`의 시작 안내(`startAction: duel`) → `DecisionManager.StartDuelTutorial` → `Bridge.BeginDuel` 순서입니다. 이때 `StoryState.Duel`로 전환하며 테스트 버튼 없이 현재 대련 게임을 그대로 실행합니다. 손을 클릭해 잡고 왼클릭/드래그로 공격, 오른클릭으로 방어합니다. NPC 원본 베기/3D 칼날 접촉/기사 피격/잔상/`VFX_Classic_03`도 같은 코드입니다.

NPC와 장소 모두 도착 즉시 `Story` 첫 카드부터 표시합니다. 별도 `Interaction` 나레이션/대화 시작 단계는 제거했으며, 미니게임 선택지는 각 NPC의 Story 마지막 Choice에 옮겼습니다. 시작 안내와 승리/패배/오류 지문도 분기 폴더의 `Story.json`으로 관리합니다. 기존 저장의 `prompt`/`tutorial_result`는 해당 조우의 첫 Story로 복원하고, 새 저장은 현재 분기 경로도 보존합니다. JSON 수정 위치와 분기 형식은 [StoryFlow.md](StoryFlow.md)를 참고하세요.

본 게임과 테스트 모두 생성 직후 마우스·방어·NPC를 한 번 초기화하고 기어/노란 버튼 입력을 잠급니다. 종료 시 이전 보드 입력 상태와 커서를 복원합니다. Story의 `duelHitTarget`(기본3)만큼 NPC를 피격시키면 승리, 플레이어가 같은 횟수만큼 피격되거나 Health 코인이 0개면 패배합니다. 본 게임의 기존 승패 콜백은 Win/Lose 결과 Story로 돌아갑니다. 피격당 Health 재고 1개 차감/표시·저장 갱신과 RawImage 붉은 섬광을 적용합니다. 방어 접촉 성공 때만 피로도가 쌓이며 가드 중 느리게, 해제 시 빠르게 회복합니다. 자세한 수치/검증은 `DuelContactTest.md`를 참고하세요.

본 게임 진입 함수의 Play 검증은 스토리/저장을 초기화하지 않은 환경에서 수행했습니다. 본 게임 세션(`IsTestRunning=false`)의 생성 위치 유지, 손 잡기/Z=-2.5, 실제 Update에서 공격 입력 허용, 원본 NPC 베기, 방어 성공/피격 없음/VFX 및 로그 1회, 승패 콜백/생성물·입력 정리, 테스트 경로 유지와 이미 잠긴 입력 상태 복원을 확인했습니다. Main 메뉴부터 조우까지의 전체 진행 테스트는 저장 데이터 보존을 위해 수행하지 않았습니다.

## 테스트하기

1. `Assets/Scenes/DecisionScene.unity`를 열고 Play 합니다. MainMenu를 거치지 않고 직접 실행할 때만 테스트 버튼이 표시됩니다. 이 경로에서는 DecisionManager의 이야기 실행을 끄므로 스토리 진행/저장을 하지 않습니다.
2. 상단의 **Duel**을 누르면 기존 보드에 기사 복제품과 플레이어/NPC 손이 생성됩니다.
3. 플레이어 손을 처음 클릭하면 보드에 누워 있던 검을 들어 앞을 향하도록 준비 자세를 적용합니다. 커서는 숨겨지고 마우스로 X/Y 방향을 조작합니다. 왼클릭/드래그는 공격 제스처, 오른클릭 유지는 방어 자세입니다. 기어/노란 버튼은 잠깁니다.
4. **Escape**로 손을 놓으면 커서가 돌아옵니다. 이후 **도박** 버튼을 누르면 Duel 생성물과 제어를 정리하고 앞/뒤 선택 패널을 엽니다.
5. **앞** 또는 **뒤**를 누르면 기존 TutorialGoldCoin 프리팹으로 동전을 던집니다. 진행 중에는 선택을 잠그며, 결과/승패 표시 후 다시 선택할 수 있습니다. 판정은 동전이 정지하거나 최대 6초 후 기존 앞/뒤 계산을 사용합니다.
6. **Duel**로 다시 전환하면 진행 중인 동전 코루틴/동전도 정리됩니다. 종료는 Play 중지 또는 Launcher의 `StopTest()`입니다. 보드 입력과 커서 상태를 복원합니다.

NPC는 본 게임/테스트에서 기존 궤적 이동 → 지정한 부모 배치로 이동 → 사용자 원본 세로 베기 → 방어 성공/실패 → 복귀를 반복합니다. 현재 씬은 왼쪽 2→3 공격 하나이며, `_Systems/DuelMiniGame/NpcVerticalAttackPose`의 월드 위치/회전으로 준비 배치를 조절합니다. 공격 중 칼날 위치/각도를 강제로 보정하지 않습니다. Begin~End 사이 실제 방어 모션 중 3D 칼날 박스가 접촉하면 성공(접점 파티클/구역 로그), End까지 성공하지 못하면 기사 피격 모션입니다. 체력/가로/대각선 공격은 아직 없습니다. 사용자 NPC 클립/컨트롤러는 수정하지 않습니다. 자세한 설정은 [DuelContactTest.md](DuelContactTest.md)를 참고하세요.

## 코드 위치와 역할

아래 대련 파일의 폴더: `Assets/Decision_YYS/Scripts/Controller/Decision/Duel/`

| 파일 | 수정할 내용 |
| --- | --- |
| PlayerDuelAction.cs | 왼클릭/드래그 공격, 오른클릭 방어, 방어 클립·상태 이름·타이밍. 이전 PlayerDuelDefense를 합친 파일입니다. |
| DuelMouseHandController.cs | 손 잡기/Escape, 커서 잠금, 마우스 이동 감도·이동 범위·X/Y 위치 제어. |
| NpcDuelStateMachine.cs | NPC 궤적·세로 공격 준비 배치/클립 이벤트/복귀/영역 로그. Begin~End 접촉 판정과 성공 파티클/로그, 실패 기사 피격. Vertical Attack Pose로 부모만 이동하고 자식 원본 Slash를 재생. 원본 Slash/Thrust 미리보기는 판정 제외. |
| DuelAnimationEvents.cs | 플레이어/NPC 공용 Animation Event 수신기. Animator가 붙은 자식에 배치. |
| DuelBladeHitbox.cs | 칼날 시작·끝 마커와 전체 잔상, 칼날 BoxCollider끼리의 3D sweep 접촉 계산. 기존 화면 계산 함수는 재사용용. 손 클릭 박스나 잔상에는 방어 판정 없음. |
| DrawGizmoAreas.cs | 기존 NPC 제스처 영역과 기사 주변 영역. 기사 번호는 우상단 1→좌상단 2→좌하단 3→우하단 4. |
| DuelAuthoringReferences.cs | 손 프리팹, 생성 앵커, 기본 회전, 생성된 손 참조. |
| DuelMiniGameBridge.cs | 시작/종료·생성·입력 연결 및 사용자 확장 UnityEvent. |

별도 위치:

- `Assets/Decision_YYS/Scripts/Controller/Ui/DecisionMiniGameTestLauncher.cs`: Duel/도박 버튼과 모드 전환, 앞/뒤 선택·결과 UI. 게임 로직을 복제하지 않고 아래 공용 코드를 호출합니다.
- `Assets/Decision_YYS/Scripts/Controller/Decision/TutorialMiniGameController.cs`: 본 게임과 테스트가 함께 쓰는 기사/동전 생성, 크기, 동전 물리·결과 계산. 동전이 보드 밖으로 날아가던 Impulse 적용을 작은 VelocityChange로 수정하고 동전용 임시 BoxCollider 바닥을 생성합니다. 바닥은 동전과 함께 정리되며 원본 보드의 충돌체는 변경하지 않습니다.
- `FingerGripController.cs`와 사용자 손/기사 프리팹, 애니메이션, 씬 배치 오브젝트는 삭제하지 않았습니다.

## Inspector 참조 구조

`_Systems/DuelMiniGame`의 기본 컴포넌트:

- DuelMiniGameBridge → TutorialMiniGameController / JourneyBoardInput / DuelAuthoringReferences / DuelMouseHandController / NpcDuelStateMachine.
- PlayerDuelAction → 같은 오브젝트의 Bridge / MouseHandController / NpcStateMachine / DrawGizmoAreas. 방어 클립도 **이 컴포넌트**의 Your Animation Clips에 연결합니다.
- NpcDuelStateMachine → Player Defense에는 위 PlayerDuelAction을 참조합니다.
- DuelAuthoringReferences → 기존 HandRoot / NpcHandRoot 프리팹 및 보드 아래 PlayerHandSpawnPoint / NpcHandSpawnPoint. 기본 회전은 Hand Base Rotation에서 수정합니다.

본 게임은 `DecisionManager.StartDuelTutorial → Bridge.BeginDuel`, 테스트는 `Launcher.StartDuel → Bridge.BeginTestDuel`로 진입합니다. 이후 공통 흐름은 `TutorialMiniGameController.StartDuel → AuthoringReferences.Prepare → MouseHandController.Begin → PlayerDuelAction.BeginDefense / NpcDuelStateMachine.Begin`입니다. 기본 기능을 시작한 다음 `On Duel Started`와 `On Duel Setup Ready`로 사용자 코드를 호출합니다. 이 이벤트에 기본 Begin 함수를 다시 넣으면 중복 초기화되므로 사용자 확장만 연결하세요.

생성된 플레이어 Animator 자식의 DuelAnimationEvents는 PlayerDuelAction에, NPC Animator 자식의 같은 컴포넌트는 NpcDuelStateMachine에 런타임 연결됩니다. `BeginParry / EndParry / FinishParry`와 `BeginAttackHit / EndAttackHit / FinishAttack / FinishBlocked` 함수 이름은 유지했습니다.

### 두 검의 시작 자세 / 플레이어가 검을 드는 자세

`_Systems/DuelMiniGame`의 `DuelAuthoringReferences`에서 생성 자세와 손을 잡은 자세를 분리합니다. Scene Start Poses는 기존 World Anchors / Hand Base Rotation보다 우선하며, 비우면 이전 앵커/기본 회전 방식을 사용합니다.

- **Player Hand Start Pose** → `_Systems/DuelMiniGame/PlayerHandRestPose`: Duel 시작 시 보드에 누워 있는 플레이어 손의 월드 위치/회전/크기입니다.
- **Player Hand Ready Pose**: 처음 손을 잡을 때 위치/크기를 가져오는 선택적인 배치 참조입니다. 현재 씬에서는 비어 있습니다. 최초 생성 배치는 유지하고, 잡을 때 부모 기본 회전 `(0, -74, -90)`을 적용한 뒤 이동 평면을 월드 Z `-2.5`로 맞춥니다. 화면 X/Y는 투영을 통해 유지하므로 깊이 변경 때문에 화면에서 손이 튀지 않습니다. 이후 마우스는 이 평면에서 움직이며 재잡기 때 위치를 초기화하지 않습니다. 원본 Animator/클립과 생성 배치는 변경하지 않습니다. 신규 Inspector 옵션은 추가하지 않았습니다.
- **Npc Hand Start Pose** → Hierarchy 루트 `NpcHandRoot`: NPC 검의 시작 배치입니다. 기존 궤적/클립은 유지합니다.
- 손 잡기 흐름은 `DuelMouseHandController.CaptureHand → DuelAuthoringReferences.ApplyPlayerReadyPose`입니다. 위치/회전은 이동 부모에만 적용합니다. Z 이동 평면과 가상 마우스 좌표도 갱신해 첫 이동의 위치 튐을 막습니다.
- 잡기 수정 Play 확인: 최초 생성 위치/회전 유지, 잡은 뒤 `(0, -74, -90)`/RestRotation 유지, 위치·크기 유지, 방어 해제/재잡기 회전 오차 0입니다. Unity가 `(0, 286, 270)`으로 표시해도 같은 회전입니다.
- 잡은 손 이동 평면은 기사 위치와 독립적인 월드 Z `-2.5`입니다. Play 279샘플 확인: 최초 생성 위치/회전 오차 0, 잡기 전후 화면 위치 오차 약 0.0007픽셀, 깊이 오차 0, 재잡기 위치 오차 0입니다. 기존 가드 검증에서는 부모 위치/회전 오차 0, 모델 위치 밀림 0, 원본 가드 회전과 오차 0을 확인했습니다.
- 손을 잡은 뒤에는 플레이어/NPC 칼날 전체의 최근 이동 궤적이 Game 화면에도 계속 표시됩니다. `DuelBladeHitbox`가 매 시점의 BladeBase~BladeTip 전체 선분을 연결해 반투명 면과 칼날 선들을 만들고, 기존 칼끝 선은 바깥 경계로 유지합니다. 기존 Trail 설정을 재사용하며 공격 이벤트가 끝나도 실시간 표시는 끊기지 않습니다. 잔상 자체에는 공격 판정이 없고 대련 종료 시 기록/출력을 정리합니다. 실제 Game 화면에서 부채꼴/이동 궤적, 정지 후 사라짐, 종료 시 초기화 및 전체 칼날 길이 반영을 확인했습니다. 추가 Inspector 항목/애니메이션 변경은 없습니다.
- 가드 클립이 있는 경우 `PlayerDuelAction`은 자식 Animator의 Position 키가 만든 오프셋만 실행 중 고정하고, 회전/손가락 모션은 원본대로 재생합니다. Guard→Idle 블렌드 중에도 위치 밀림을 방지합니다. 부모 화면 수평 정렬은 클립이 없을 때만 쓰는 기존 대체 동작입니다. 원본 `.anim` 파일의 Position/Rotation 키, Controller, 프리팹은 수정하지 않습니다.
- 방어 해제 시 `PlayerDuelAction`은 `MouseHandController.RestRotation`으로 돌아갑니다. 누워 있던 생성 회전으로 되돌리지 않습니다. Escape 후 다시 잡을 때 현재 이동 위치는 유지하며, 새 Duel 시작 때만 준비 자세를 다시 적용합니다.
- 제작용 손은 Play 동안 숨겨 중복 표시를 막으며, Play 종료 시 원래 활성 상태로 복원됩니다. 원본/프리팹과 애니메이션을 수정하거나 삭제하지 않습니다.
- 이 시작 자세를 사용하는 런타임 손은 `_Systems/DuelMiniGame/DuelPlayerHand`, `DuelNpcHand` 아래에 생성합니다. 비균일 크기의 보드 부모 아래에서 회전할 때 생기는 검/손 변형을 피하기 위한 배치입니다. Animator는 계속 손 모델 자식에 있습니다.
- NPC는 플레이어가 손을 클릭해 잡기 전까지 씬 시작 자세를 유지합니다(Wait For Player Capture가 켜진 경우). 이후 궤적 이동을 시작하며 Enable Vertical Attacks가 켜져 있으면 세로 공격을 반복합니다. Escape로 손을 놓으면 NPC 이동/공격 Animator도 대기합니다.
- **Npc Hand Patrol Pose**가 연결돼 있으면 이 Transform의 월드 Z와 회전으로 이동합니다. 아래 자동 Upright/Facing/Palm Tilt 및 Path Back Offset Z는 적용하지 않습니다. 현재 화면 궤적의 X/Y는 `Screen Path` 9개 점에서 조절하며, 이 Transform의 X/Y를 더하지 않습니다. 직접 지정한 월드 `Path Points`에만 최저점 위치 보정을 적용합니다.
- NPC의 **Keep Patrol Blade Upright**(`NpcDuelStateMachine`)가 켜져 있으면 궤적 이동 중 실제 BladeBase→BladeTip이 월드 +Y를 향하도록 손 부모만 회전합니다. 생성 대기 자세에는 적용하지 않으며, 원본 Slash/Thrust 미리보기 중에도 적용하지 않습니다. 미리보기 종료 후에는 다시 검을 세우고 이동합니다. 기존 궤적·Z 오프셋·애니메이션 클립은 변경하지 않습니다.
- **Face Player During Patrol**도 켜져 있으면 검을 세운 뒤 NPC 정면을 플레이어 준비 자세의 정면과 반대로 맞춥니다. 수직축 회전만 추가하므로 칼끝은 계속 위를 향합니다. 현재 플레이어 가드 각도가 아닌 RestRotation을 기준으로 해서 방어 회전을 따라 돌지 않습니다. 생성 대기/원본 공격 미리보기에는 개입하지 않습니다.
- **Patrol Palm Tilt Degrees = 10**: 서로 마주보게 정렬한 뒤 실제 손바닥 쪽으로 검을 10° 기울입니다. 플레이어 방향을 기울기 기준으로 사용하지 않습니다. 현재 왼손 리그의 손목/검지/새끼 뿌리로 손바닥 법선을 구합니다. `0`은 수직, 양수는 손바닥 쪽, 음수는 손등 쪽이며 매 프레임 기준을 재정렬해 기울기가 누적되지 않습니다. 원본 공격 미리보기에는 적용하지 않습니다.

### 기사 복제품의 크기 / 생성 위치

`_Systems/Decision_Manager`의 `TutorialMiniGameController`에서 수정합니다.

- **Knight Scale = 0.6**: 생성 기사 루트의 X/Y/Z Scale에 그대로 적용합니다. 이전 Knight Size는 Renderer 전체 길이를 기준으로 다시 크기를 계산하므로 Transform Scale 0.6과 달랐습니다. 기사에는 이 정규화 계산을 제거했으며 동전 크기 계산은 유지했습니다. Play 중 필드 변경은 다음 LateUpdate에서 반영되고, 영구 변경은 Edit에서 저장합니다.
- **Knight Spawn Pose** → Hierarchy 루트 `KnightCharacter_Copy`: 현재 보드 배치를 월드 위치/회전 기준으로 사용합니다. 현재 Position `(-2.87, -7.895, -3.32)`입니다. 이 참조가 있으면 중앙 자동 배치/바닥 높이 보정으로 수동 위치를 덮어쓰지 않습니다. 비우면 보드 중앙 바닥에 맞춥니다.
- 씬 기사 원본은 Play 동안 숨겨 두고 `TutorialKnightTarget` 복제품만 표시합니다. 원본을 삭제하거나 프리팹에 덮어쓰지 않습니다. 보드 Bounds 계산에서도 제작용 기사를 제외합니다.

이전 통합에서는 시작/잡기 자세 분리, 준비 회전 유지, Escape 재잡기, 기사 크기/위치 및 Duel→도박→Duel 정리를 확인했습니다. 이전 화면 보정 방식에서는 2→3 / 1→4 칼날 중심 통과도 확인했지만 현재 지정 자세 공격에는 그 보정을 적용하지 않습니다. 현재 방식은 사진 위치/회전 유지, 원본 Slash 곡선 일치, 로그 1회, 궤적 복귀를 Play에서 확인했습니다. 추가로 실제 방어 클립+3D 칼날 접촉 성공, 무방어/다른 모션/Z 분리/구간 밖 접촉 실패, End 직전 방어 성공, 접점 불꽃과 로그 1회 및 실패 피격을 확인했습니다. 현재는 위 공통 대련 종료/Health 코인/방어 피로도 규칙도 연결했습니다.

모드 UI는 기존 `Walking_View_Back/MiniGame_TestBar`에 Duel/도박 두 버튼, 형제 `GambleTestPanel/FaceChoices`에 앞/뒤 버튼이 있습니다. Launcher에는 이 버튼 네 개와 결과 TMP 텍스트를 참조했습니다.

기본 승패는 피격 횟수로 자동 판정하며 확장 코드용 `Bridge.CompleteDuel(bool)`과 `CompleteWin / CompleteLoss`도 유지합니다. 결과 없이 정리하려면 `CleanupDuel()`을 호출합니다. 정상 스토리는 `BeginDuel(completion, hitTarget, healthCoins, consumeHealth)`로 목표와 현재 재고/저장 콜백을 전달합니다. 테스트는 `BeginTestDuel(completion)`으로 동일 규칙을 실행하되 실제 저장은 쓰지 않으며 결과를 로그로 표시합니다. 마우스 조작은 본 게임/테스트 공용이고 도박 콜백은 유지합니다.

## 기즈모 표시 유지

`_Systems/DuelMiniGame → DrawGizmoAreas → Gizmo Visibility → Show Gizmos`에서 직접 켜고 끕니다. 기존 `Draw Gizmo On Set` 필드를 유지한 채 Inspector 표시 이름만 명확하게 바꿨습니다. 체크 상태는 대련 시작/종료나 도박 전환이 변경하지 않습니다. 복제 기사가 없으면 `TutorialMiniGameController.KnightSpawnPose`의 `Knight_Center`(없으면 배치 루트)를 참조하므로 Edit에서도 고정 기사 위치에 표시됩니다. 화면 영역 계산은 표시를 꺼도 유지합니다.

계속 유지할 설정은 Edit 모드에서 체크하고 씬을 저장하세요. Scene/Game 창 상단의 Unity **Gizmos** 스위치도 켜져 있어야 표시됩니다. 실제 게임 빌드용 UI는 추가하지 않았습니다.

## 합치거나 삭제한 파일

- PlayerDuelDefense.cs → PlayerDuelAction.cs로 합친 뒤 기존 컴포넌트 참조를 교체하고 삭제.
- PlayerDuelAnimationEvents.cs → DuelAnimationEvents.cs로 합친 뒤 Hand 프리팹/씬 참조를 교체하고 삭제.
- 예전 세 버튼을 만들던 `AgentScripts/BuildDecisionMiniGameTestUi.cs`는 현재 구조와 맞지 않아 삭제. 일회성 이동/검사 스크립트도 실행 후 제거합니다.
- Assets/Temp의 PlayerDuelAction은 기능을 지운 것이 아니라 위 프로젝트 폴더로 **GUID를 보존하여 이동**했습니다. 기타 실제 사용 중인 대련 스크립트도 동일합니다.

삭제한 두 스크립트·메타, 이전 UI 생성기, 변경 전 DecisionScene/Hand 프리팹은 로컬 `UserSettings/MiniGameIntegrationBackup/20261004-041013/`에 보관했습니다. 파일 한 개만 복원하면 현재 통합 컴포넌트와 충돌할 수 있으므로 참조를 함께 확인해야 합니다.

Inspector 값/애니메이션 수정은 Play를 종료한 Edit 모드에서 하고 씬/프리팹을 저장하세요. Play 중 수정한 값은 종료 시 되돌아갑니다.

## 이번 확인 범위

Unity CLI로 연결된 Editor에서 컴파일, 씬/손 프리팹 Missing Script 0개, 두 모드 버튼과 앞/뒤 버튼의 UI Raycast를 확인했습니다. DecisionScene 직접 Play에서 기사·두 손 생성/손 잡기/통합 방어 자세, Duel→도박→Duel 전환, 동전 생성·결과·재시도 및 던지는 중 모드 전환 정리, 종료 시 보드 입력 복원을 검사했습니다. 동전이 보드 위에 남는 것과 결과 UI도 Game View로 확인했습니다. 테스트 후 Play는 종료했습니다.

MainMenu부터 스토리 전체를 끝까지 진행하는 검사는 이번 범위에 포함하지 않습니다. 정상 스토리에서는 테스트 UI를 숨기는 분기와 기존 게임 콜백 경로를 유지했습니다.
