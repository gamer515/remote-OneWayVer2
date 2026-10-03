using UnityEngine;

/// <summary>
/// [역할] 플레이어/NPC Animator 자식의 애니메이션 이벤트를 한 컴포넌트에서 전달합니다. NPC 공격 판정은 없습니다.
/// BeginAttackHit/EndAttackHit=미리보기 잔상 시작/끝, FinishAttack/FinishBlocked=미리보기 Idle 복귀.
/// 피해·방어·패링·부모 회전 보정은 하지 않습니다. 원본 클립의 이벤트를 삭제할 필요가 없습니다.
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
