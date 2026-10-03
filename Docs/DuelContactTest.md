# 테스트 대련: NPC 공격 제거 / 원본 애니메이션·궤적 유지

현재 상태(2026-10-04): NPC의 자동 공격 기능은 제거했습니다. 이전 화면 접촉/피격 테스트 설명은 더 이상 현재 동작이 아닙니다.

## 유지한 부분

- Duel Test 버튼, 기사 복제품과 두 손 생성, 플레이어 손 클릭/마우스 조작.
- 플레이어 왼클릭 공격 제스처와 오른클릭 방어 자세 입력. NPC 자동 공격이 없으므로 가드/패링 성공 결과는 자동 발생하지 않습니다.
- NPC 대기 궤적의 Screen Path / Path Points / Path Travel Seconds / Path Back Offset Z와 기존 씬 설정.
- 보드 높이 제한(Hand Board Clearance), 기존 궤적 Gizmos, 칼끝 잔상.
- 사용자 원본 NPC Animator, Idle / Slash / Thrust / Blocked 상태와 애니메이션 파일 및 이벤트.
- 플레이어가 기존 NPC 영역에 공격 제스처를 했을 때의 On Npc Hit 콜백. 승패 코드를 직접 연결하는 기존 경로입니다.

## 제거한 부분

- NPC의 자동 공격 타이머/패턴, 예고 → 공격 → 튕김 상태 전환.
- 기사 영역으로 손을 옮기는 공격 경로, 공격 중 칼날 위치·각도 자동 보정.
- NPC 칼날의 접촉/피격/가드/패링 판정, 피해 이벤트.
- 공격 경고 UI, 접촉 이펙트·소리·히트 스톱·NPC 반동 생성.

## 직접 확인하기

1. DecisionScene에서 Play → Duel을 누릅니다.
2. 플레이어 손을 클릭하면 NPC가 기존 궤적을 따라 이동합니다. Wait For Player Capture가 켜져 있으면 손을 잡기 전에는 대기합니다.
3. NPC는 자동으로 기사 영역에 공격하거나 피해를 주지 않습니다.
4. 원본 클립을 확인하려면 Hierarchy의 DuelMiniGame을 선택하고 Npc Duel State Machine 컴포넌트 우측 ⋮ 메뉴에서 아래 명령을 실행합니다(Play 전용).
   - Preview Original Slash (Play Mode): 원본 베기 자세 미리보기.
   - Preview Original Thrust (Play Mode): 원본 찌르기 자세 미리보기.
5. 미리보기 중에도 기존 궤적은 유지합니다. 클립 종료 이벤트 또는 Animation Timeout 후 Idle로 돌아옵니다. 이 과정에 공격/방어 판정은 없습니다.
6. Escape는 손 조작을 해제합니다. 종료는 Play 중지 또는 DuelMiniGameBridge.CleanupDuel()입니다.

## 수정할 코드

대련 스크립트는 모두 `Assets/Decision_YYS/Scripts/Controller/Decision/Duel/`에 있습니다.

- NpcDuelStateMachine.cs: 궤적과 보드 높이 제한, 원본 클립 미리보기. 부모 회전은 변경하지 않습니다.
- DuelAnimationEvents.cs: 플레이어/NPC 공용 이벤트 수신기. NPC의 BeginAttackHit/EndAttackHit는 잔상만, FinishAttack/FinishBlocked는 미리보기 종료만 처리합니다. 플레이어 BeginParry/EndParry/FinishParry도 이 파일이 받습니다.
- PlayerDuelAction.cs: 기존 마우스 공격 입력과 PlayerDuelDefense의 방어 자세/클립/이벤트를 한 컴포넌트로 통합했습니다.
- DuelBladeHitbox.cs: 칼날 마커·잔상과 재사용 가능한 화면 계산 유틸리티. 현재 NPC는 접촉 검사 함수를 호출하지 않습니다.

두 테스트 모드와 통합 참조 구성은 [MiniGameIntegration.md](MiniGameIntegration.md)를 참고하세요.

베기·찌르기 클립의 이전 백업은 UserSettings/NpcAttackClipBackups/20261004-025158에 있습니다. 이번 통합에서는 새 NPC 공격 클립을 만들거나 Animator Controller를 교체하지 않았습니다. Unity의 재직렬화로 파일 해시는 달라질 수 있으므로 해시 동일 여부만으로 모션 변경을 판단하지 마세요.

Unity CLI로 컴파일과 실제 생성된 손의 궤적 진행, 고정 Z/부모 회전 유지, 원본 Slash/Thrust 재생 및 종료 이벤트를 검사했습니다. 자동 공격 상태·NPC 피해 이벤트·각도 보정·공격 경고 생성은 없습니다. 임시 검사 스크립트는 정리했고 Play는 종료했습니다.

Play 중 Inspector 수정은 종료 시 되돌아갑니다. 유지할 궤적 값은 Edit 모드에서 바꿔 씬을 저장하세요.
