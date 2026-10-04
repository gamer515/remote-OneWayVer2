using UnityEngine;

/// <summary>
/// [역할] 플레이어/NPC Animator 자식의 애니메이션 이벤트를 각 컨트롤러로 전달합니다.
/// BeginAttackHit/EndAttackHit=NPC 베기 접촉 구간 시작·끝. StateMachine이 평가 후 방어 성공/실패를 확정합니다.
/// FinishAttack=궤적 복귀(미리보기는 Idle 복귀).
/// 이 스크립트는 이벤트 전달만 합니다. 원본 클립의 이벤트를 수정할 필요가 없습니다.
/// </summary>
public sealed class DuelAnimationEvents : MonoBehaviour
{
    private NpcDuelStateMachine owner;
    private PlayerDuelAction player;
    public void Initialize(NpcDuelStateMachine machine) { owner = machine; player = null; }
    public void Initialize(PlayerDuelAction action) { player = action; owner = null; }
    public void BeginAttackHit() => owner?.BeginAttackHit();
    public void EndAttackHit() => owner?.EndAttackHit();
    public void FinishAttack() => owner?.FinishAttack();
    public void FinishBlocked() => owner?.FinishBlocked();
    public void BeginParry() => player?.BeginParry();
    public void EndParry() => player?.EndParry();
    public void FinishParry() => player?.FinishParry();
}
