using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;

public static class VerifyB2SlashVariants
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Require(bool value, string error) { if (!value) throw new Exception(error); }
    static void Call(B2PhaseSmokeAttack phase, string method, params object[] args) =>
        typeof(B2PhaseSmokeAttack).GetMethod(method, Hidden).Invoke(phase, args);

    public static object Run()
    {
        Require(Application.isPlaying, "Play mode required. Run VerifyB2PhaseSmokeAttack.Setup first.");
        Time.timeScale = 0;
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var player = battle.player;
        var health = player.GetComponent<BattlePlayerHealth>();
        var loco = player.GetComponent<NewPlayerLocomotion>();
        var boss = battle.CurrentStage.boss;
        var combat = boss.GetComponent<B2CombatController>();
        var phase = combat.smokePhase;
        var random = UnityEngine.Random.state;
        var playerPosition = player.transform.position;
        var reports = new List<object>();
        var seen = new HashSet<int>();
        var launched = new HashSet<int>();
        var launchTimes = new List<float>();
        var shotsField = typeof(B2PhaseSmokeAttack).GetField("projectiles", Hidden);
        try
        {
            combat.ResetCombat(); boss.ResetBoss(); health.ignoreDamage = true; health.ResetHealth();
            Require(phase.useAngledSlashes, "Angled shots disabled.");
            Require(Mathf.Abs(phase.horizontalSlashHeight - 3.4f) < .001f && Mathf.Abs(phase.diagonalSlashHeight - 6f) < .001f, "Authored heights lost.");
            Require(Quaternion.Angle(phase.GetSlashRotation(B2PhaseSmokeAttack.SlashStyle.Horizontal), Quaternion.Euler(0, 20, 90)) < .01f, "Horizontal rotation changed.");
            Require(Quaternion.Angle(phase.GetSlashRotation(B2PhaseSmokeAttack.SlashStyle.Diagonal), Quaternion.Euler(0, 20, 45)) < .01f, "User diagonal rotation changed.");
            UnityEngine.Random.InitState(73151);
            Require(phase.Begin(health), "Phase start failed.");
            Call(phase, "ClearClouds"); // Avoid expensive smoke simulation; exercise the actual volley clock.
            Call(phase, "Change", B2PhaseSmokeAttack.Phase.Volley);
            typeof(B2PhaseSmokeAttack).GetField("nextShotTime", Hidden).SetValue(phase, phase.smokeSpreadSeconds);
            for (int frame = 0; frame < 680; frame++)
            {
                phase.Tick(1f / 60f);
                var shots = (List<B2PhaseSlashProjectile>)shotsField.GetValue(phase);
                foreach (var shot in shots)
                {
                    if (seen.Add(shot.GetInstanceID()))
                    {
                        Vector3 point = phase.emissionPoints[phase.ShotLocations[shot.ShotIndex]].position;
                        Require(Mathf.Abs(shot.transform.position.y - point.y - phase.GetSlashHeight(shot.Style)) < .001f, "Spawn height mismatch.");
                        Require(Quaternion.Angle(shot.transform.rotation, Quaternion.LookRotation(shot.Direction) * phase.GetSlashRotation(shot.Style)) < .02f, "Spawn orientation mismatch.");
                        reports.Add(new { shot = shot.ShotIndex + 1, style = shot.Style.ToString(), height = shot.transform.position.y, point = phase.ShotLocations[shot.ShotIndex] });
                    }
                    if (shot.Launched && launched.Add(shot.GetInstanceID()))
                    {
                        launchTimes.Add((float)typeof(B2PhaseSmokeAttack).GetField("phaseTime", Hidden).GetValue(phase));
                        Require(Quaternion.Angle(shot.transform.rotation, Quaternion.LookRotation(shot.Direction) * phase.GetSlashRotation(shot.Style)) < .02f, "Launch reset a slash to vertical.");
                    }
                }
            }
            Require(phase.ShotsEmitted == 8 && seen.Count == 8 && launched.Count == 8, "Expected eight spawned and launched shots.");
            Require(phase.ShotStyles.Distinct().Count() == 3, "Not all three shapes appeared.");
            foreach (var style in (B2PhaseSmokeAttack.SlashStyle[])Enum.GetValues(typeof(B2PhaseSmokeAttack.SlashStyle)))
                Require(phase.ShotStyles.Count(s => s == style) >= 2, "A style was underrepresented: " + style);
            Require(Mathf.Abs(launchTimes[0] - 3.6f) < .05f, "First launch timing changed.");
            for (int i = 1; i < launchTimes.Count; i++) Require(Mathf.Abs(launchTimes[i] - launchTimes[i-1] - 1f) < .05f, "Shot interval changed.");

            // All eight source directions must use the authored local orientation.
            phase.Cancel(); Require(phase.Begin(health), "Contact test phase start failed."); Call(phase, "ClearClouds");
            int directions = 0, hits = 0, parries = 0, rolls = 0;
            loco.enabled = true;
            foreach (var style in (B2PhaseSmokeAttack.SlashStyle[])Enum.GetValues(typeof(B2PhaseSmokeAttack.SlashStyle)))
            {
                foreach (var point in phase.emissionPoints)
                {
                    var shot = Spawn(phase, health, style, point.position + Vector3.up * phase.GetSlashHeight(style));
                    try
                    {
                        var start = shot.transform.position;
                        shot.Advance(.1f); Require(!shot.Launched && shot.transform.position == start, "Moved during preparation.");
                        player.transform.position = playerPosition + Vector3.right * 3; Physics.SyncTransforms(); shot.Advance(.1f);
                        Require(shot.Launched && Vector3.Distance(shot.AimPosition, player.transform.position) < .001f, "Did not aim at launch-time target.");
                        var direction = shot.Direction; var rotation = shot.transform.rotation;
                        Require(Quaternion.Angle(rotation, Quaternion.LookRotation(direction) * phase.GetSlashRotation(style)) < .02f, "Incorrect aimed orientation.");
                        player.transform.position = playerPosition - Vector3.right * 3; shot.Advance(.1f);
                        Require(shot.Direction == direction && Quaternion.Angle(shot.transform.rotation, rotation) < .01f, "Shot homed after launch.");
                        Require(Mathf.Abs(shot.transform.position.y - start.y) < .001f, "Visual tilt changed travel height.");
                        directions++;
                    }
                    finally { player.transform.position = playerPosition; UnityEngine.Object.DestroyImmediate(shot.gameObject); }
                }
                loco.Stop(); loco.SetDefenseMode(false); health.ignoreDamage = false; health.ResetHealth(); Physics.SyncTransforms();
                ShootAtPlayer(phase, health, style);
                Require(Mathf.Abs(health.Health - (health.maxHealth - phase.Damage)) < .001f, style + " did not damage standing player."); hits++;
                health.ResetHealth(); loco.StartParry(); int before = phase.Parries;
                ShootAtPlayer(phase, health, style);
                Require(health.Health == health.maxHealth && phase.Parries == before + 1, style + " parry failed."); parries++;
                loco.Stop(); loco.SetDefenseMode(false); loco.StartRoll();
                ShootAtPlayer(phase, health, style);
                Require(health.Health == health.maxHealth, style + " roll dodge failed."); rolls++;
                loco.Stop(); health.ignoreDamage = true;
            }
            return new { passed = true, shots = reports, launchTimes, aimedDirections = directions, damageStyles = hits, parryStyles = parries, rollStyles = rolls };
        }
        finally
        {
            phase.Cancel(); combat.ResetCombat(); health.ignoreDamage = true; health.ResetHealth();
            loco.Stop(); loco.SetDefenseMode(false); loco.enabled = false;
            player.transform.position = playerPosition; Physics.SyncTransforms();
            UnityEngine.Random.state = random;
        }
    }

    static B2PhaseSlashProjectile Spawn(B2PhaseSmokeAttack phase, BattlePlayerHealth health, B2PhaseSmokeAttack.SlashStyle style, Vector3 origin)
    {
        var go = UnityEngine.Object.Instantiate(phase.slashPrefab, origin, Quaternion.identity);
        go.name = "Temporary oriented slash verification"; go.transform.localScale = Vector3.one * phase.slashScale;
        var shot = go.AddComponent<B2PhaseSlashProjectile>(); shot.Initialize(phase, health, 99, style); return shot;
    }
    static void ShootAtPlayer(B2PhaseSmokeAttack phase, BattlePlayerHealth health, B2PhaseSmokeAttack.SlashStyle style)
    {
        var shot = Spawn(phase, health, style, health.transform.position + Vector3.back * 18 + Vector3.up * phase.GetSlashHeight(style));
        try
        {
            shot.Advance(.2f); shot.Advance(.6f);
            float once = health.Health; shot.Advance(.1f);
            Require(health.Health == once, "Same projectile hit twice.");
        }
        finally { UnityEngine.Object.DestroyImmediate(shot.gameObject); }
    }
}
