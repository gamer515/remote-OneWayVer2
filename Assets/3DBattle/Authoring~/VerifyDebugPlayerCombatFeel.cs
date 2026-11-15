using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using OneWay.Battle3D;

public static class VerifyDebugPlayerCombatFeel
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task<string> Run()
    {
        Require(Application.isPlaying, "Play mode required.");
        EditorApplication.isPaused = false;
        bool background = Application.runInBackground;
        Application.runInBackground = true;
        // A failed check can leave delayed Destroy calls pending when the Editor pauses.
        foreach (var old in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (old.name == "Temporary feel verification ground" || old.name == "Temporary feel verification boss") UnityEngine.Object.DestroyImmediate(old);
        var player = UnityEngine.Object.FindObjectsByType<BattlePlayerController>(FindObjectsSortMode.None).Single(p => p.name == "New Player - Attack Combo");
        var combo = player.GetComponent<NewPlayerAttackCombo>();
        var feel = player.GetComponent<DebugPlayerCombatFeel>();
        var special = player.GetComponent<DebugPlayerSpecialCombo>();
        bool originalSpecial = special != null && special.enabled;
        var loco = player.GetComponent<NewPlayerLocomotion>();
        var motor = player.GetComponent<CharacterController>();
        var body = player.GetComponent<BattleArenaBody>();
        var vfx = player.GetComponent<BattleSwordSlashEffect>();
        var follow = player.viewCamera.GetComponent<BattleFollowCamera>();
        var originalSet = combo.motions;
        var originalBoss = player.boss;
        var originalPosition = player.transform.position;
        var originalRotation = player.transform.rotation;
        bool originalEnabled = player.enabled;
        var originalSlash = vfx.slashPrefab;
        var originalFlash = vfx.hitFlashPrefab;
        bool originalRead = combo.readMouseInput, originalStop = feel.hitStop;
        float originalVolume = feel.soundVolume;
        var runtimeSet = UnityEngine.Object.Instantiate(originalSet);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Temporary feel verification ground";
        ground.transform.position = new Vector3(1000, -.5f, 1000);
        ground.transform.localScale = new Vector3(200, 1, 200);
        var target = new GameObject("Temporary feel verification boss");
        var collider = target.AddComponent<CapsuleCollider>();
        collider.radius = 1;
        collider.height = 3;
        collider.center = Vector3.up * 1.5f;
        var boss = target.AddComponent<BattleBoss>();
        boss.hitbox = collider;
        boss.maxHealth = 100000;
        boss.ResetBoss();
        var results = new List<object>();
        try
        {
            player.enabled = false;
            if (special != null) special.enabled = false;
            body.enabled = false;
            follow.enabled = false;
            combo.readMouseInput = false;
            feel.hitStop = false;
            feel.soundVolume = 0;
            vfx.slashPrefab = vfx.hitFlashPrefab = null;
            combo.motions = runtimeSet;
            Action reset = () => {
                combo.Cancel(); loco.Stop(); loco.SetDefenseMode(false);
                motor.enabled = false;
                player.transform.SetPositionAndRotation(new Vector3(1000,0,1000), Quaternion.identity);
                motor.enabled = true;
                combo.ResetAirRecovery();
                collider.enabled = true;
                target.transform.position = new Vector3(1000, 0, 1003);
                boss.ResetBoss();
                // ResetBoss restores its original transform; position deliberately set afterwards.
                target.transform.position = new Vector3(1000, 0, 1003);
                player.viewCamera.transform.SetPositionAndRotation(new Vector3(1000, 2, 990), Quaternion.identity);
                Physics.SyncTransforms();
            };
            foreach (float dt in new[] {1f/60, 1f/30, 1f/15})
            {
                reset();
                player.boss = boss;
                runtimeSet.order = (int[])originalSet.order.Clone();
                combo.RequestAttack();
                int before = combo.DamageApplications;
                bool last = false, complete = false;
                float elapsed = 0;
                for (int n = 0; n < 600; n++)
                {
                    combo.RequestAttack();
                    combo.Tick(dt); elapsed += dt;
                    if (combo.SequenceIndex == 6) last = true;
                    if (last && combo.SequenceIndex == 0) { complete = true; break; }
                }
                int hits = combo.DamageApplications - before;
                Require(complete, "Combo stuck at " + dt);
                Require(hits == 8, "Expected eight separate damage applications, got " + hits + " at dt=" + dt);
                Require(player.transform.position.y < .15f, "Jump did not land.");
                Require(elapsed < 3.3f && elapsed > 2.8f, "Unexpected combo duration: " + elapsed);
                results.Add(new { fps = Mathf.RoundToInt(1/dt), seconds = elapsed, hits, finalY = player.transform.position.y });
            }
            reset();
            player.boss = null;
            collider.enabled = false;
            Physics.SyncTransforms();
            runtimeSet.order = new[] { 0 };
            combo.RequestAttack();
            float maxRetreat = 0;
            for (int n = 0; n < 200 && combo.IsActive; n++)
            {
                combo.Tick(.01f);
                maxRetreat = Mathf.Max(maxRetreat, 1000 - player.transform.position.z);
            }
            Require(Mathf.Abs(maxRetreat - 5) < .03f, "Retreat distance changed.");
            Require(Mathf.Abs(combo.ActiveAttackDashDistance - 10) < .06f, "Forward dash is not 2x retreat.");
            Require(Mathf.Abs(player.transform.position.z - 1005) < .06f, "Dash endpoint " + player.transform.position + ", maximum=" + combo.ActiveAttackDashDistance + ", frame=" + combo.Frame + ", active=" + combo.IsActive);
            results.Add(new { retreat = maxRetreat, forward = combo.ActiveAttackDashDistance });

            reset(); player.boss = boss; runtimeSet.order = new[] { 4 };
            combo.RequestAttack(); combo.Tick(.08f);
            Require(combo.ActiveSkillDashDistance == 0, "Skill 03 dashed while boss already in attack range.");
            reset(); target.transform.position = new Vector3(1000,0,1005.5f); Physics.SyncTransforms();
            combo.RequestAttack(); combo.Tick(.08f);
            Require(combo.SkillDashAssisted && combo.ActiveSkillDashDistance > 0 && combo.ActiveSkillDashDistance < 6, "Dash assist did not clamp to boss.");
            results.Add(new { assistedDistance = combo.ActiveSkillDashDistance });

            reset(); player.boss = null; collider.enabled = false; runtimeSet.order = new[] { 5 };
            combo.RequestAttack();
            feel.CameraPivot(player.transform.position + Vector3.up, .016f);
            float peak = 0, cameraPeak = 0;
            int previousLandings = feel.Landings;
            for (int n = 0; n < 300 && combo.IsActive; n++)
            {
                combo.Tick(.004f);
                peak = Mathf.Max(peak, combo.AirborneHeight);
                cameraPeak = Mathf.Max(cameraPeak, feel.CameraPivot(player.transform.position + Vector3.up, .004f).y - 1);
            }
            Require(peak > 5 && peak < 5.5f, "Jump height changed unexpectedly: " + peak);
            Require(cameraPeak > .4f && cameraPeak < peak * .36f, "Camera follows too much of jump: " + cameraPeak);
            Require(feel.Landings - previousLandings == 1, "Jump must land exactly once.");
            results.Add(new { jumpPeak = peak, cameraRise = cameraPeak, landings = feel.Landings - previousLandings });

            reset(); collider.enabled = false; Physics.SyncTransforms(); combo.RequestAttack();
            for (int n = 0; n < 200 && combo.AirborneHeight < 3; n++) combo.Tick(.004f);
            float beforeCancel = player.transform.position.y;
            loco.StartRoll();
            Require(Mathf.Abs(player.transform.position.y - beforeCancel) < .01f, "Air dodge teleports to ground.");
            for (int n = 0; n < 150 && combo.AirborneHeight > .02f; n++) combo.Tick(.01f);
            Require(combo.AirborneHeight <= .02f && player.transform.position.y < .1f, "Air dodge never lands.");
            results.Add(new { airCancelHeight = beforeCancel, groundedAfterRoll = true });

            reset(); collider.enabled = false; Physics.SyncTransforms(); combo.RequestAttack();
            for (int n = 0; n < 200 && combo.AirborneHeight < 3; n++) combo.Tick(.004f);
            player.PlayDefeat();
            for (int n = 0; n < 60 && combo.IsRecoveringFromJump; n++) { EditorApplication.QueuePlayerLoopUpdate(); await Task.Delay(20); }
            Require(!combo.IsRecoveringFromJump && player.transform.position.y < .1f, "Defeat left the player suspended in air.");
            player.ResetForBattle(null);
            results.Add(new { defeatedAirbornePlayerLands = true });

            reset(); runtimeSet.order = new[] { 3 }; combo.RequestAttack();
            while (combo.InPreparation || combo.Frame < 40) combo.Tick(.005f);
            combo.RequestAttack();
            bool restarted = false;
            for (int n = 0; n < 100; n++) { combo.Tick(.005f); if (combo.InPreparation) { restarted = true; break; } }
            Require(restarted, "Late recovery input was lost.");
            loco.SetDefenseMode(true); loco.StartParry();
            Require(!combo.IsActive && loco.Parrying, "Parry failed to cancel attack.");
            results.Add(new { lateInputBuffered = true, parryCancel = true });

            reset(); collider.enabled = false; player.boss = null; combo.RequestAttack();
            while (combo.InPreparation || combo.Frame < 20) combo.Tick(.005f);
            combo.RequestAttack();
            await Task.Delay(230);
            for (int n = 0; n < 200 && combo.IsActive; n++) combo.Tick(.005f);
            combo.Tick(.02f);
            Require(!combo.IsActive, "Expired buffered input restarted the combo.");
            results.Add(new { expiredInputDiscarded = true });

            reset(); collider.enabled = false; feel.enabled = false; player.boss = null;
            combo.RequestAttack(); float legacySeconds = 0;
            while (combo.IsActive && legacySeconds < 3) { combo.Tick(.005f); legacySeconds += .005f; }
            float expectedLegacy = (runtimeSet.entry.Frames + runtimeSet.attacks[3].Frames) / (30 * combo.playbackSpeed);
            Require(Mathf.Abs(legacySeconds - expectedLegacy) < .011f, "Opt-out changed the original animation timing.");
            feel.enabled = true;
            results.Add(new { originalTimingPreserved = true, legacySeconds });

            reset(); feel.hitStop = true;
            float priorScale = Time.timeScale;
            float stopStarted = Time.unscaledTime;
            int stopFrame = Time.frameCount;
            feel.OnHit(0);
            Require(Time.timeScale == 0 && feel.IsHitStopped, "Heavy hit did not pause combat.");
            for (int n = 0; n < 50 && feel.IsHitStopped; n++) { EditorApplication.QueuePlayerLoopUpdate(); await Task.Delay(20); }
            Require(!feel.IsHitStopped && Mathf.Approximately(Time.timeScale, priorScale), "Hit pause did not restore time. unscaled elapsed=" + (Time.unscaledTime-stopStarted) + ", frames=" + (Time.frameCount-stopFrame) + ", active=" + feel.isActiveAndEnabled + ", paused=" + EditorApplication.isPaused);
            feel.OnHit(0); feel.enabled = false;
            Require(Mathf.Approximately(Time.timeScale, priorScale), "Disabling feedback left time paused.");
            feel.enabled = true;
            results.Add(new { hitStopRestored = true, disableRestored = true });

            return JsonConvert.SerializeObject(new { passed = true, results }, Formatting.Indented);
        }
        catch (Exception e) { throw new Exception(e.Message + " Completed checks: " + JsonConvert.SerializeObject(results), e); }
        finally
        {
            feel.ResetFeedback();
            if (special != null) special.enabled = originalSpecial;
            feel.enabled = true;
            combo.Cancel(); loco.Stop();
            combo.motions = originalSet;
            combo.readMouseInput = originalRead;
            feel.hitStop = originalStop; feel.soundVolume = originalVolume;
            vfx.slashPrefab = originalSlash; vfx.hitFlashPrefab = originalFlash;
            player.boss = originalBoss;
            motor.enabled = false;
            player.transform.SetPositionAndRotation(originalPosition, originalRotation);
            motor.enabled = true;
            combo.ResetAirRecovery();
            body.enabled = true; follow.enabled = true; player.enabled = originalEnabled;
            Application.runInBackground = background;
            follow.Snap();
            UnityEngine.Object.DestroyImmediate(ground); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(runtimeSet);
        }
    }
}
