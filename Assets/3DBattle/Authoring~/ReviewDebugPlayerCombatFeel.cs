using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using OneWay.Battle3D;

public static class ReviewDebugPlayerCombatFeel
{
    public static string Run()
    {
        if (!Application.isPlaying) throw new Exception("Play mode required.");
        var player = UnityEngine.Object.FindObjectsByType<BattlePlayerController>(FindObjectsSortMode.None).Single(p => p.name == "New Player - Attack Combo");
        var combo = player.GetComponent<NewPlayerAttackCombo>();
        var feel = player.GetComponent<DebugPlayerCombatFeel>();
        var follow = player.viewCamera.GetComponent<BattleFollowCamera>();
        follow.lookOffset = new Vector3(follow.lookOffset.x, 1.6f, follow.lookOffset.z);
        var motor = player.GetComponent<CharacterController>();
        player.enabled = false;
        player.ResetForBattle(null);
        player.GetComponent<BattlePlayerHealth>().ignoreDamage = true;
        player.boss.ResetBoss();
        player.GetComponent<BattleArenaBody>().enabled = false;
        follow.enabled = false;
        combo.readMouseInput = false;
        feel.hitStop = false;
        feel.soundVolume = 0;
        var bounds = player.boss.hitbox.bounds;
        motor.enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(bounds.center.x, 0, bounds.min.z - 1.8f), Quaternion.identity);
        motor.enabled = true;
        combo.ResetAirRecovery();
        typeof(BattleFollowCamera).GetField("initialized", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(follow, false);
        follow.Snap();
        Physics.SyncTransforms();
        combo.RequestAttack();
        int[] indices = {4,1,2,5,5,5};
        float[] frames = {9,17,37,13,17,21};
        string[] labels = {"Skill 03", "Upward", "Double hit 2", "Jump rise", "Jump apex", "Landing"};
        var report = new List<object>();
        const int w=640,h=420;
        var sheet = new Texture2D(w*3,h*2,TextureFormat.RGB24,false);
        int captured = 0;
        float elapsed = 0;
        var effects = new Dictionary<GameObject,float>();
        for (int n = 0; n < 1600 && captured < 6; n++)
        {
            const float dt = .004f;
            combo.RequestAttack(); combo.Tick(dt); elapsed += dt;
            feel.CameraPivot(player.transform.position + follow.lookOffset, dt);
            follow.Snap();
            foreach (var root in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.parent == null && (t.name == "NewPlayer Sword Slash" || t.name == "Player Sword Hit Flash")))
                if (!effects.ContainsKey(root.gameObject)) effects.Add(root.gameObject, elapsed);
            foreach (var pair in effects.ToArray())
            {
                if (pair.Key == null) continue;
                float age = elapsed - pair.Value;
                if (age > (pair.Key.name == "Player Sword Hit Flash" ? .3f : .16f))
                { effects.Remove(pair.Key); UnityEngine.Object.DestroyImmediate(pair.Key); continue; }
                foreach (var ps in pair.Key.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(dt,false,false,true);
            }
            if (combo.InPreparation || combo.AttackIndex != indices[captured] || combo.Frame < frames[captured]) continue;
            var rt = RenderTexture.GetTemporary(w,h,24);
            var camera = player.viewCamera;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(w,h,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,w,h),0,0); texture.Apply();
            sheet.SetPixels((captured%3)*w, (1-captured/3)*h, w,h,texture.GetPixels());
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(texture);
            report.Add(new { labels = labels[captured], frame=combo.Frame, seconds=elapsed, airborne=combo.AirborneHeight });
            captured++;
        }
        sheet.Apply();
        var path = "Assets/3DBattle/Authoring~/DebugCombatFeelReview.png";
        File.WriteAllBytes(path,sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
        if (captured != 6) throw new Exception("Incomplete review: " + captured + ", active=" + combo.IsActive + ", sequence=" + combo.SequenceIndex + ", frame=" + combo.Frame + ", defeated=" + player.IsDefeated);
        return JsonConvert.SerializeObject(new { path, report },Formatting.Indented);
    }
}
