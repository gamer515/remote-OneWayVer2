using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;

public static class VerifySeparatePickaxeParry
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static object Apply()
    {
        Require(!Application.isPlaying, "Edit mode required");
        var hit = UnityEngine.Object.FindObjectsByType<BossWeaponHit>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(h => h.gameObject.scene.path == "Assets/Scenes/3DBattleScene.unity" && h.weapon != null && h.weapon.name == "pickaxe");
        Undo.RecordObject(hit, "Separate pickaxe parry range"); hit.parrySizeMultiplier = 2;
        EditorUtility.SetDirty(hit); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hit.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(hit.gameObject.scene);
        return new { hit.name, damageCenter = hit.localCenter.ToString("F4"), damageSize = hit.localSize.ToString("F4"), hit.parrySizeMultiplier };
    }
    public static object Run()
    {
        Require(Application.isPlaying, "Play mode required"); EditorApplication.isPaused = true;
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var intro = battle.GetComponent<BattleIntroWalkDebug>(); if (intro) { intro.Stop(); intro.autoPlayOnStageStart = false; }
        battle.SelectStage(1);
        var boss = battle.CurrentStage.boss; boss.GetComponent<BossApproachController>().automaticApproach = false;
        boss.GetComponent<BossActionController>().ResetController(); boss.GetComponent<BossPhaseTwo>().ResetPhase();
        var hit = boss.GetComponent<BossWeaponHit>(); var animator = boss.animator;
        var patterns = AssetDatabase.FindAssets("t:BossWeaponAttackPattern", new[] { "Assets/3DBattle/Settings" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(BattleCharacterAssetPaths.Load<BossWeaponAttackPattern>).ToArray();
        var combo = patterns.Single(p => p.name == "B1_Attack7_Smooth10_20");
        var dummy = new GameObject("Temporary parry range target");
        var health = dummy.AddComponent<BattlePlayerHealth>(); health.maxHealth = 1000; health.ResetHealth();
        var player = dummy.AddComponent<BattlePlayerController>(); player.enabled = false;
        dummy.GetComponent<CharacterController>().enabled = false;
        dummy.AddComponent<BattleParryEffect>(); // Effects omitted; actual parry/health paths still run.
        dummy.AddComponent<BoxCollider>().size = Vector3.one * .02f;
        var extra = new GameObject("Second body collider"); extra.transform.SetParent(dummy.transform, false); extra.AddComponent<BoxCollider>().size = Vector3.one * .02f;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Action<bool> guard = value => {
            typeof(BattlePlayerController).GetField("legacyDefenseMode", flags).SetValue(player, value);
            typeof(BattlePlayerController).GetField("blockRemaining", flags).SetValue(player, value ? 1f : 0f);
        };
        Action<float> dodge = value => typeof(BattlePlayerController).GetField("dashRemaining", flags).SetValue(player, value);
        Action<BossWeaponAttackPattern, float> pose = (p, time) => { animator.Play(p.animationState, 0, time / p.clip.length); animator.Update(0); };
        Action<Vector3> sample = point => { dummy.transform.position = point; Physics.SyncTransforms(); hit.SampleContact(); };
        Action reset = () => { health.ResetHealth(); hit.Begin(combo, health); guard(false); dodge(0); };
        int outsideChecks = 0;
        var extensions = new List<object>();
        try
        {
            pose(combo, combo.GetHitWindow(0).startSeconds + .01f);
            hit.GetWorldBox(out var center, out var rotation, out var half);
            hit.GetParryWorldBox(out var parryCenter, out var parryRotation, out var parryHalf);
            Require(Vector3.Distance(half * 2, parryHalf) < .0001f && Vector3.Distance(center, parryCenter) < .0001f && Quaternion.Angle(rotation, parryRotation) < .01f, "Parry volume is not double damage volume");
            dummy.transform.rotation = rotation;
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward }) foreach (int sign in new[] { -1, 1 })
            {
                reset(); var outsideDamage = center + rotation * Vector3.Scale(half, axis) * (1.5f * sign);
                sample(outsideDamage); Require(health.Health == 1000 && hit.HitCount == 0 && hit.ParryCount == 0, "Enlarged parry area dealt damage");
                guard(true); sample(outsideDamage); Require(hit.ParryCount == 1 && hit.HitCount == 0 && health.Health == 1000, "Parry-only area missed");
                sample(outsideDamage); Require(hit.ParryCount == 1, "Duplicate colliders repeated parry");
                guard(false); sample(center); Require(health.Health == 1000 && hit.HitCount == 0, "Parried swing dealt later damage"); outsideChecks++;
            }
            reset(); sample(center); Require(hit.HitCount == 1 && health.Health == 1000 - combo.damage, "Original damage volume changed");
            reset(); guard(true); sample(center); Require(hit.ParryCount == 1 && hit.HitCount == 0 && health.Health == 1000, "Damage beat parry in shared area");
            reset(); guard(true); sample(center + rotation * Vector3.right * (parryHalf.x + .1f)); Require(hit.ParryCount == 0 && health.Health == 1000, "Parry exceeded its configured range");
            reset(); guard(true); dodge(1); sample(center); Require(hit.ParryCount == 0 && hit.HitCount == 0, "Roll immunity changed"); dodge(0);
            reset(); guard(true); pose(combo, combo.GetHitWindow(0).startSeconds - .01f);
            hit.GetWorldBox(out var windupCenter, out _, out _); sample(windupCenter); Require(!hit.WindowOpen && hit.ParryCount == 0, "Parry during windup");
            guard(false); sample(windupCenter); Require(health.Health == 1000, "Damage during windup");
            foreach (var p in patterns.Where(p => p.name.StartsWith("B1_Attack6_") || p.name.StartsWith("B1_Attack10_") || p.name.StartsWith("B1_Attack14_") || p == combo))
            {
                int strike = p == combo ? 1 : 0;
                health.ResetHealth(); hit.Begin(p, health); guard(false); pose(p, p.GetHitWindow(strike).startSeconds + .01f);
                hit.GetWorldBox(out center, out rotation, out half); dummy.transform.rotation = rotation;
                sample(center + Vector3.one * 1000);
                float down = hit.CurrentDownwardExtension;
                Require(down > 0 && Mathf.Approximately(hit.CurrentParryDownwardExtension, down * 2), "Downward parry extension not doubled: " + p.name);
                float vertical = Mathf.Abs((rotation * Vector3.right).y) * half.x + Mathf.Abs((rotation * Vector3.up).y) * half.y + Mathf.Abs((rotation * Vector3.forward).y) * half.z;
                var point = center + Vector3.down * (down + vertical + .05f);
                // Start a fresh sample so the test does not sweep from the deliberately distant setup point.
                hit.Begin(p, health); sample(point); Require(health.Health == 1000 && hit.HitCount == 0, "Downward damage was enlarged: " + p.name);
                guard(true); sample(point); Require(hit.ParryCount == 1 && health.Health == 1000, "Downward parry missed: " + p.name);
                extensions.Add(new { p.name, damageDown = down, parryDown = hit.CurrentParryDownwardExtension });
            }
            // Each of attack7's three swings must still have independent outcomes.
            health.ResetHealth(); hit.Begin(combo, health); guard(true);
            for (int i = 0; i < combo.StrikeCount; i++)
            {
                pose(combo, combo.GetHitWindow(i).startSeconds + .01f); hit.GetWorldBox(out center, out _, out _); sample(center);
                Require(hit.ParryCount == i + 1 && health.Health == 1000, "Combo strike not independent");
            }
            return new { passed = true, outsideChecks, parryMultiplier = hit.parrySizeMultiplier, damageUnchanged = true, parryPriority = true,
                duplicateContactsSuppressed = true, windupAndRollSafe = true, independentParries = hit.ParryCount, extensions };
        }
        finally { hit.End(); UnityEngine.Object.DestroyImmediate(dummy); boss.RefreshAnimation(); }
    }
}
