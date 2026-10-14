using UnityEngine;

namespace OneWay.Battle3D
{
    public sealed class BattleCombatFeedback : MonoBehaviour
    {
        public BattlePlayerController player;
        public BattleBoss boss;
        public LineRenderer rangeRing;
        public LineRenderer attackLine;
        public LineRenderer chargeRing;
        float effectRemaining;
        Vector3 attackTarget;
        readonly Vector3[] circle = new Vector3[97];
        static readonly Rect Panel = new Rect(18f, 18f, 330f, 222f);

        public bool IsPointerOverPanel(Vector2 screen) => Panel.Contains(new Vector2(screen.x, Screen.height - screen.y));

        void LateUpdate()
        {
            if (player == null) return;
            DrawCircle(rangeRing, player.attackRange, 1f);
            Color rangeColor = player.BossInRange ? new Color(0.3f, 1f, 0.35f) : new Color(0.1f, 0.65f, 0.25f);
            rangeRing.startColor = rangeRing.endColor = rangeColor;
            chargeRing.enabled = player.State == BattlePlayerController.PressState.Charging;
            if (chargeRing.enabled) DrawCircle(chargeRing, 0.8f, player.Charge01);
            effectRemaining = Mathf.Max(0f, effectRemaining - Time.deltaTime);
            attackLine.enabled = effectRemaining > 0f;
            if (attackLine.enabled)
            {
                attackLine.SetPosition(0, player.transform.position + Vector3.up);
                attackLine.SetPosition(1, attackTarget);
            }
        }

        void DrawCircle(LineRenderer line, float radius, float portion)
        {
            for (int i = 0; i < circle.Length; i++)
            {
                float angle = i / 96f * Mathf.PI * 2f * portion;
                circle[i] = player.transform.position + new Vector3(Mathf.Cos(angle) * radius, 0.055f, Mathf.Sin(angle) * radius);
            }
            line.positionCount = circle.Length;
            line.SetPositions(circle);
        }

        public void ShowAttack(Vector3 target, bool special)
        {
            attackTarget = target;
            effectRemaining = special ? 0.3f : 0.13f;
            attackLine.startWidth = special ? 0.45f : 0.12f;
            attackLine.endWidth = special ? 0.2f : 0.04f;
            attackLine.startColor = attackLine.endColor = special ? new Color(1f, 0.65f, 0.05f) : Color.white;
        }

        void OnGUI()
        {
            if (player == null || boss == null) return;
            GUI.Box(Panel, GUIContent.none);
            GUILayout.BeginArea(new Rect(32f, 28f, 300f, 200f));
            GUILayout.Label("3D BATTLE  /  MOUSE ONLY");
            GUILayout.Label("RMB: Move   |   LMB on ground: Dash");
            GUILayout.Label("LMB on boss in range: Tap / Hold + release");
            GUILayout.Label($"BOSS  {boss.Health:0} / {boss.maxHealth:0}" + (boss.IsAlive ? "" : "  DEFEATED"));
            GUILayout.HorizontalSlider(boss.Health, 0f, boss.maxHealth);
            GUILayout.Label(player.Status);
            GUILayout.Label($"CHARGE  {player.Charge01 * 100f:0}%   |   {(player.BossInRange ? "IN RANGE" : "OUT OF RANGE")}");
            boss.patrolEnabled = GUILayout.Toggle(boss.patrolEnabled, "Boss patrol (test charge cancellation)");
            if (GUILayout.Button("Reset boss")) { player.CancelInput(); boss.ResetBoss(); }
            GUILayout.EndArea();
        }
    }
}
