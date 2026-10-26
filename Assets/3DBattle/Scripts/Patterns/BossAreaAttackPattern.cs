using System.Collections;
using UnityEngine;

namespace OneWay.Battle3D
{
    [CreateAssetMenu(menuName = "3D Battle/Patterns/Area Attack")]
    public sealed class BossAreaAttackPattern : BossAttackPattern
    {
        [Min(0.1f)] public float radius = 5f;
        [Range(1f, 360f)] public float angle = 120f;

        public override IEnumerator Execute(BossPatternRunner runner)
        {
            runner.FaceTarget();
            // Facing is locked during windup, allowing the player to dodge.
            runner.ShowTelegraph(radius, angle);
            yield return new WaitForSeconds(Mathf.Max(0f, windupSeconds));
            runner.HideTelegraph();
            runner.PlayAnimation(animationState);
            if (runner.Target != null)
            {
                Vector3 direction = runner.Target.transform.position - runner.transform.position;
                direction.y = 0f;
                if (direction.magnitude <= radius &&
                    (angle >= 360f || Vector3.Angle(runner.transform.forward, direction) <= angle * 0.5f))
                    runner.Target.TakeDamage(damage);
            }
            yield return new WaitForSeconds(Mathf.Max(0f, recoverySeconds));
        }
    }
}
