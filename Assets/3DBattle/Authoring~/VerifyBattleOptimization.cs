using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using OneWay.Battle3D;
using Object = UnityEngine.Object;

// Run with run_script in Play mode. Changes only runtime instances, never saved assets.
public static class VerifyBattleOptimization
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Hidden).GetValue(o);
    static void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Hidden).Invoke(o, args);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    public static object HUD()
    {
        Check(!Application.isPlaying, "Edit mode required");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var go = new GameObject("Temporary HUD cache checks", typeof(RectTransform), typeof(CanvasRenderer));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        try
        {
            var graphic = go.AddComponent<BattlePlayerVitalsGraphic>(); graphic.rectTransform.sizeDelta = new Vector2(420,64);
            var method = typeof(BattlePlayerVitalsGraphic).GetMethod("OnPopulateMesh", Hidden | BindingFlags.DeclaredOnly);
            var draw = (Action<UnityEngine.UI.VertexHelper>)Delegate.CreateDelegate(typeof(Action<UnityEngine.UI.VertexHelper>), graphic, method);
            using (var vh = new UnityEngine.UI.VertexHelper())
            {
                for (int pass = 0; pass < 2; pass++) for (int i = 0; i <= 100; i++)
                {
                    graphic.SetValues(i/100f, Mathf.Min(1,i/100f+.1f), i/100f); draw(vh);
                    Check(vh.currentVertCount > 0 && vh.currentIndexCount > 0, "HUD missing at " + i);
                }
                var buffers = Field<IList>(graphic, "pointBuffers"); int warmedCount = buffers.Count;
                var cachedBuffers = buffers.Cast<object>().ToArray();
                for (int i = 100; i >= 0; i--) { graphic.SetValues(i/100f,1,i/100f); draw(vh); }
                Check(buffers.Count == warmedCount && buffers.Cast<object>().SequenceEqual(cachedBuffers), "HUD scratch buffers replaced after warmup");
                graphic.energy = 0; draw(vh);
                var vertex = UnityEngine.UIVertex.simpleVert; vh.PopulateUIVertex(ref vertex,0); var point = vertex.position;
                graphic.rectTransform.sizeDelta *= 2; draw(vh); vh.PopulateUIVertex(ref vertex,0);
                Check(Vector3.Distance(vertex.position, point*2) < .001f, "Cached frame did not resize");
                var frames = Field<IList>(graphic,"frames");
                var vertices = (List<UnityEngine.UIVertex>)frames[0].GetType().GetField("vertices").GetValue(frames[0]);
                var colors = vertices.Select(v=>v.color).ToArray(); graphic.copperMetal = Color.magenta; draw(vh);
                Check(!colors.SequenceEqual(vertices.Select(v=>v.color)), "Cached frame ignored colour change");
                Check(graphic.EnergyColour(.75f)==graphic.energySky && graphic.EnergyColour(1)==graphic.energyWhite, "Global energy gradient stops changed");
                return new { valuesZeroToFull=true, reusableBuffers=warmedCount, resizeInvalidation=true, colourInvalidation=true, gradientStops=true };
            }
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static async Task<object> Run()
    {
        Check(Application.isPlaying, "Play mode required");
        Application.runInBackground = true; Time.timeScale = 0;
        var battle = Object.FindFirstObjectByType<Battle3DSceneController>();
        var intro = battle.GetComponent<BattleIntroWalkDebug>();
        if (intro) { intro.autoPlayOnStageStart = false; intro.Stop(); }
        battle.SelectStage(2); battle.enabled = false;
        var player = battle.player; player.CancelInput(); player.enabled = false;
        var loco = player.GetComponent<NewPlayerLocomotion>(); loco.Stop(); loco.enabled = false;
        var combo = player.GetComponent<NewPlayerAttackCombo>(); combo.Cancel(); combo.enabled = false;
        var health = player.GetComponent<BattlePlayerHealth>(); health.ResetHealth(); health.ignoreDamage = true;
        var boss = battle.CurrentStage.boss;
        var ai = boss.GetComponent<B2CombatController>(); ai.ResetCombat(); ai.reactAtHealthThresholds = false;
        var phase = ai.smokePhase;
        var result = new List<string>();
        await Task.Delay(100); // Allow Start to prewarm pools while game time is paused.

        var playerFX = player.GetComponent<BattleSwordSlashEffect>();
        Call(playerFX, "PreparePools");
        var playerPool = Field<BattleEffectPool>(playerFX, "slashPool");
        var flashPool = Field<BattleEffectPool>(playerFX, "flashPool");
        int playerCreated = playerPool.CreatedCount, flashCreated = flashPool.CreatedCount;
        for (int i = 0; i < 20; i++)
        {
            playerFX.PlayAuthoredSwing(.2f); playerFX.PlayHitFlash(boss);
            Check(playerPool.ActiveCount == 1 && flashPool.ActiveCount == 1, "Player effects did not rent");
            playerPool.ReleaseAll(); flashPool.ReleaseAll();
        }
        Check(playerPool.CreatedCount == playerCreated && flashPool.CreatedCount == flashCreated, "Player effects allocated new instances after prewarm");
        var oldLease = playerPool.Rent(Vector3.zero, Quaternion.identity); oldLease.Effect.Activate(); oldLease.Release();
        var nextLease = playerPool.Rent(Vector3.one, Quaternion.identity); nextLease.Effect.Activate();
        Check(oldLease.Effect == nextLease.Effect, "Expected same recycled instance");
        oldLease.Release(); Check(nextLease.IsAlive, "Stale lease released new effect");
        nextLease.Effect.ReleaseAfter(0); Call(nextLease.Effect, "Update"); Check(!nextLease.IsAlive, "Timed expiry failed");
        result.Add("Player: 20 slash/hit pairs reuse prewarmed objects; stale handles and timed return checked");

        var sword = phase.swordEffect; sword.enabled = true; Call(sword, "PreparePools");
        var stationaryPool = Field<BattleEffectPool>(sword, "stationaryPool");
        stationaryPool.ReleaseAll();
        int stationaryCreated = stationaryPool.CreatedCount;
        sword.useStationarySlash = true;
        for (int i = 0; i < 20; i++) { sword.EmitB2SwordSlash(); Check(stationaryPool.ActiveCount == 1, "B2 stationary slash missing: active="+stationaryPool.ActiveCount+", enabled="+sword.isActiveAndEnabled+", alive="+boss.IsAlive); stationaryPool.ReleaseAll(); }
        Check(stationaryPool.CreatedCount == stationaryCreated, "B2 stationary clones grew");
        var flyingPool = Field<BattleEffectPool>(sword, "flyingPool");
        for (int i = 0; i < 2; i++)
        {
            var flying = flyingPool.Rent(Vector3.zero, Quaternion.identity);
            flying.Effect.Activate(); flying.Effect.Flight.Initialize(Vector3.forward, 10, .4f, .2f);
            Call(flying.Effect.Flight, "Advance", .19f); Check(flying.Effect.transform.position == Vector3.zero, "Flying slash moved before delay");
            Call(flying.Effect.Flight, "Advance", .11f); Check(Mathf.Abs(flying.Effect.transform.position.z - 1) < .001f, "Flying slash delay/speed changed");
            Call(flying.Effect.Flight, "Advance", 1f); Check(Mathf.Abs(flying.Effect.transform.position.z - 4) < .001f, "Flying slash exceeded lifetime");
            flying.Release();
        }
        result.Add("B2 stationary/flying effects reuse objects; 0.2 s delay, speed and travel lifetime survive reuse");

        Call(phase, "PreparePools");
        var smokePool = Field<BattleEffectPool>(phase, "smokePool");
        var phasePool = Field<BattleEffectPool>(phase, "projectilePool");
        int smokeCreated = smokePool.CreatedCount, phaseCreated = phasePool.CreatedCount;
        var phaseRounds = new List<object>();
        for (int round = 0; round < 2; round++)
        {
            boss.ResetBoss(); health.ResetHealth(); health.ignoreDamage = true;
            Check(phase.Begin(health), "Cannot begin phase");
            var launches = new List<float>(); var seen = new HashSet<int>();
            for (int tick = 0; tick < 900 && !phase.Completed; tick++)
            {
                phase.Tick(.05f); ai.animator.Update(.05f); Physics.SyncTransforms();
                if (phase.CurrentPhase == B2PhaseSmokeAttack.Phase.Volley)
                {
                    Check(smokePool.ActiveCount == 8, "Volley must retain eight clouds");
                    foreach (var shot in Field<List<B2PhaseSlashProjectile>>(phase, "projectiles"))
                        if (shot.Launched && seen.Add(shot.ShotIndex)) launches.Add(Field<float>(phase, "phaseTime"));
                }
            }
            Check(phase.Completed && phase.ShotsEmitted == 8 && phase.ActiveProjectileCount == 0, "Phase did not finish eight shots");
            Check(launches.Count == 8 && Mathf.Abs(launches[0] - 3.6f) < .075f, "First launch timing changed");
            for (int i = 1; i < launches.Count; i++) Check(Mathf.Abs(launches[i] - launches[i-1] - 1) < .075f, "Volley interval changed");
            Check(smokePool.ActiveCount == 0 && phasePool.ActiveCount == 0 && boss.hitbox.enabled, "Phase cleanup incomplete");
            Check(ai.animator.GetComponentsInChildren<SkinnedMeshRenderer>().All(r => r.enabled), "Boss body not restored");
            Check(ai.animator.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m).All(m=>!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m))), "Fade material not restored");
            phaseRounds.Add(new { round, launches, locations=phase.ShotLocations.ToArray(), styles=phase.ShotStyles.Select(s=>s.ToString()).ToArray() });
        }
        Check(smokePool.CreatedCount == smokeCreated && phasePool.CreatedCount == phaseCreated, "Phase clones grew across repeated phases");
        result.Add("Two full phases: eight clouds/shots, 3.6 s first launch, 1 s intervals, all shots expire before smoke clears, boss/material/collider restoration");

        Check(phase.Begin(health), "Contact test phase cannot begin");
        var bodyFrom = player.transform.position;
        loco.enabled = true; loco.Stop(); loco.SetDefenseMode(false); health.ignoreDamage = false; health.ResetHealth();
        Action fire = () => {
            var shotLease = phasePool.Rent(health.transform.position + Vector3.back * 18 + Vector3.up * phase.slashHeight, Quaternion.identity);
            var effect = shotLease.Effect; effect.transform.localScale = Vector3.one * phase.slashScale; effect.Activate();
            var shot = effect.Projectile; shot.Initialize(phase, health, 99);
            try {
                Check(!shot.Complete && !shot.Launched, "Projectile retained completion/launch state");
                shot.Advance(.19f); Check(!shot.Launched, "Projectile launched early");
                shot.Advance(.01f); shot.Advance(.6f); float after = health.Health;
                shot.Advance(.1f); Check(health.Health == after, "Projectile hit twice");
            } finally { shotLease.Release(); }
        };
        Physics.SyncTransforms(); fire(); Check(health.Health == health.maxHealth - phase.Damage, "First recycled hit damage failed");
        fire(); Check(health.Health == health.maxHealth - phase.Damage * 2, "Second recycled hit damage failed");
        health.ResetHealth(); loco.StartParry(); int parries = phase.Parries; fire();
        Check(health.Health == health.maxHealth && phase.Parries == parries + 1, "Recycled projectile parry failed");
        loco.Stop(); loco.SetDefenseMode(false); loco.StartRoll(); fire(); Check(health.Health == health.maxHealth, "Recycled projectile roll avoidance failed");
        loco.Stop(); health.ignoreDamage = true;
        var aimed = phasePool.Rent(bodyFrom + Vector3.back * 30, Quaternion.identity); aimed.Effect.Activate();
        try {
            var shot = aimed.Effect.Projectile; shot.Initialize(phase, health, 100); shot.Advance(.1f);
            player.transform.position = bodyFrom + Vector3.right * 5; shot.Advance(.1f);
            Check(Vector3.Distance(shot.AimPosition, player.transform.position) < .001f, "Launch-time aim changed");
            var direction = shot.Direction; player.transform.position = bodyFrom - Vector3.right * 5; shot.Advance(.3f);
            Check(Vector3.Distance(direction, shot.Direction) < .001f, "Projectile started homing");
        } finally { aimed.Release(); player.transform.position = bodyFrom; Physics.SyncTransforms(); }
        phase.Cancel();
        Check(!phase.IsRunning && phasePool.ActiveCount == 0 && smokePool.ActiveCount == 0 && boss.hitbox.enabled, "Cancellation did not restore state");
        result.Add("Reused projectile: one hit only, independent next hit, parry, roll, launch-time aim, no homing, cancellation");
        var report = new { result, phaseRounds, playerCreated, flashCreated, smokeCreated, phaseCreated, errors=0 };
        File.WriteAllText("Temp/BattleOptimizationRuntime.json", JsonConvert.SerializeObject(report, Formatting.Indented));
        return report;
    }
}
