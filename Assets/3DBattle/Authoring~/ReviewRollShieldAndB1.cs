using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;

public static class ReviewRollShieldAndB1
{
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(o, args);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static object VerifyB1()
    {
        Require(Application.isPlaying, "Play mode required"); EditorApplication.isPaused = true;
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>(); battle.SelectStage(1);
        var boss = battle.CurrentStage.boss; var phase = boss.GetComponent<BossPhaseTwo>();
        var gimmick = boss.GetComponent<BossPhaseTwoGimmick>(); var actions = boss.GetComponent<BossActionController>();
        var player = battle.player; var health = player.GetComponent<BattlePlayerHealth>();
        health.ignoreDamage = false; health.ResetHealth(); actions.ResetController(); gimmick.ResetGimmick();
        boss.GetComponent<BossApproachController>().automaticApproach = false;
        var motor = player.GetComponent<CharacterController>(); motor.enabled = false;
        player.transform.position = boss.transform.position + Vector3.forward * 3.8f; motor.enabled = true;
        var animator = boss.animator;
        Require(phase.ForcePhaseTwo(), "Phase2 trigger rejected"); phase.Tick(0);
        Require(actions.Current == BossActionKind.PhaseChange, "Missing phase entry");
        animator.Play(phase.transitionState, 0, 1); animator.Update(0); actions.Tick(0); phase.Tick(0); phase.Tick(2.1f);
        Require(gimmick.Locked, "Gimmick did not start");
        float initialHp = health.Health;
        for (int i = 0; i <= 30; i++)
        {
            float time = (gimmick.rotationStarts[1] - .001f) * i / 30;
            animator.Play(phase.entryAttack.animationState, 0, time / phase.entryAttack.clip.length); animator.Update(0);
            Call(gimmick, "Update");
            foreach (var hit in boss.GetComponentsInChildren<BossWeaponHit>(true)) hit.SampleContact();
            Require(health.Health == initialHp && gimmick.Failures == 0, "First rotation unexpectedly damaged player");
        }
        float firstRotationHp = health.Health;
        animator.Play(phase.entryAttack.animationState, 0, (gimmick.strikeTimes[0] + .001f) / phase.entryAttack.clip.length); animator.Update(0); Call(gimmick, "Update");
        float firstHitHp = health.Health;
        Require(firstHitHp == initialHp - gimmick.failureDamage && gimmick.Failures == 1, "First active strike did not damage player");
        animator.Play(phase.entryAttack.animationState, 0, (gimmick.strikeTimes[1] + .001f) / phase.entryAttack.clip.length); animator.Update(0); Call(gimmick, "Update");
        float nextHitHp = health.Health;
        Require(nextHitHp == initialHp - gimmick.failureDamage * 2 && gimmick.Failures == 2, "Second active strike was suppressed");
        gimmick.ResetGimmick(); actions.ResetController();
        return new { passed = true, initialHp, firstRotationHp, firstHitHp, nextHitHp, firstStrikeClipTime = gimmick.strikeTimes[0], damagePerStrike = gimmick.failureDamage };
    }

    public static object VerifyShield() => CheckShieldMotion(false);
    public static object VerifyImmediateStop() => CheckShieldMotion(true);
    static object CheckShieldMotion(bool immediateStop)
    {
        Require(Application.isPlaying, "Play mode required"); EditorApplication.isPaused = true;
        var loco = Live; var player = loco.GetComponent<BattlePlayerController>(); var animator = player.animator;
        var motor = player.GetComponent<CharacterController>(); bool motorWasEnabled = motor.enabled; motor.enabled = false;
        var skin = animator.GetComponentInChildren<SkinnedMeshRenderer>(); var shield = player.shield.transform;
        var rightHand = skin.bones.First(b => b.name == "RightHand"); var weights = skin.sharedMesh.boneWeights;
        Func<int, float> grip = i => { var w = weights[i]; float sum = 0;
            int[] ids = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 }; float[] ws = { w.weight0, w.weight1, w.weight2, w.weight3 };
            for (int j = 0; j < 4; j++) if (skin.bones[ids[j]].name == "LeftHand") sum += ws[j]; return sum; };
        var ts = skin.sharedMesh.triangles; var triangles = new List<int>();
        for (int i = 0; i < ts.Length; i += 3) if ((grip(ts[i]) + grip(ts[i + 1]) + grip(ts[i + 2])) / 3 < .5f)
            triangles.AddRange(new[] { ts[i], ts[i + 1], ts[i + 2] });
        var bodyGo = new GameObject("Temporary body probe"); var body = bodyGo.AddComponent<MeshCollider>(); var mesh = new Mesh();
        var shieldGo = new GameObject("Temporary shield probe"); shieldGo.transform.localScale = shield.lossyScale;
        var probe = shieldGo.AddComponent<MeshCollider>(); probe.sharedMesh = shield.GetComponent<MeshFilter>().sharedMesh; probe.convex = true;
        var failures = new List<object>(); int total = 0; float maxDepth = 0, maxRightHandChange = 0;
        Action<string, float, float> check = (stage, time, dt) => {
            var before = rightHand.localRotation; Call(loco, "ApplyShieldPose", dt);
            maxRightHandChange = Mathf.Max(maxRightHandChange, Quaternion.Angle(before, rightHand.localRotation));
            var vertices = SkinVertices(skin).ToList(); var tris = new List<int>(triangles);
            foreach (var mf in animator.transform.parent.GetComponentsInChildren<MeshFilter>(true))
                if (!mf.transform.IsChildOf(shield) && !mf.name.Contains("Sword") && mf.GetComponent<Renderer>()?.enabled == true)
                { int start = vertices.Count; vertices.AddRange(mf.sharedMesh.vertices.Select(v => mf.transform.TransformPoint(v))); tris.AddRange(mf.sharedMesh.triangles.Select(t => t + start)); }
            body.sharedMesh = null; mesh.Clear(); mesh.vertices = vertices.ToArray(); mesh.triangles = tris.ToArray(); mesh.RecalculateBounds(); body.sharedMesh = mesh;
            Physics.SyncTransforms(); total++;
            if (Physics.ComputePenetration(probe, shield.position, shield.rotation, body, Vector3.zero, Quaternion.identity, out var dir, out var depth))
            { maxDepth = Mathf.Max(maxDepth, depth); if (depth > .001f) failures.Add(new { stage, time, depth }); }
        };
        try
        {
            loco.Stop(); loco.SetDefenseMode(true); loco.StartRoll(); check("roll", 0, 0);
            const float dt = 1f / 120;
            for (int i = 1; i < 500 && loco.Rolling; i++) { loco.Tick(dt); check("roll", i * dt, dt); }
            for (int i = 1; i <= (immediateStop ? 0 : 60); i++) { animator.Update(dt); loco.Tick(dt); check("run transition", i * dt, dt); }
            loco.Stop();
            for (int i = 1; i <= 45; i++) { animator.Update(dt); check("idle transition", i * dt, dt); }
            Require(maxRightHandChange < .05f, "Right hand changed by shield correction");
            loco.StartParry(); Call(loco, "ApplyShieldPose", dt);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var attachment = (Quaternion)typeof(NewPlayerLocomotion).GetField("shieldAttachmentRotation", flags).GetValue(loco);
            var mount = (Vector3)typeof(NewPlayerLocomotion).GetField("shieldAttachmentPosition", flags).GetValue(loco);
            Require(Quaternion.Angle(shield.localRotation, attachment) < .05f && Vector3.Distance(shield.localPosition, mount) < .001f, "Parry attachment changed");
            loco.Stop(); loco.SetDefenseMode(false); Call(loco, "ApplyShieldPose", dt);
            return new { samples = total, intersections = failures.Count, maxDepth, maxRightHandChange, parryAttachmentPreserved = true, failures = failures.Take(25).ToArray(), last = failures.LastOrDefault(), loco.rollingShieldClearance };
        }
        finally
        {
            motor.enabled = motorWasEnabled;
            foreach (var o in new UnityEngine.Object[] { bodyGo, shieldGo, mesh }) UnityEngine.Object.DestroyImmediate(o);
        }
    }
    static NewPlayerLocomotion Live => UnityEngine.Object.FindFirstObjectByType<NewPlayerLocomotion>();
    public static object InspectIntersection()
    {
        var player = Live.GetComponent<BattlePlayerController>(); var shield = player.shield.transform;
        var skin = player.animator.GetComponentInChildren<SkinnedMeshRenderer>(); var weights = skin.sharedMesh.boneWeights;
        var groups = new Dictionary<string, List<int>>(); var ts = skin.sharedMesh.triangles;
        for (int i = 0; i < ts.Length; i += 3)
        {
            var sums = new Dictionary<int, float>();
            for (int j = 0; j < 3; j++) { var w = weights[ts[i + j]]; int[] ids = {w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3}; float[] ws={w.weight0,w.weight1,w.weight2,w.weight3};
                for (int k=0;k<4;k++) { if (!sums.ContainsKey(ids[k])) sums[ids[k]]=0; sums[ids[k]]+=ws[k]; } }
            var name = skin.bones[sums.OrderByDescending(x=>x.Value).First().Key].name;
            if (!groups.ContainsKey(name)) groups[name] = new List<int>(); groups[name].AddRange(new[]{ts[i],ts[i+1],ts[i+2]});
        }
        var bodyGo=new GameObject("Temporary body");var body=bodyGo.AddComponent<MeshCollider>();var mesh=new Mesh();
        var shieldGo=new GameObject("Temporary shield");shieldGo.transform.localScale=shield.lossyScale;var probe=shieldGo.AddComponent<MeshCollider>();probe.sharedMesh=shield.GetComponent<MeshFilter>().sharedMesh;probe.convex=true;
        try {
            var hits=new List<object>();
            foreach(var pair in groups) {body.sharedMesh=null;mesh.Clear();mesh.vertices=SkinVertices(skin);mesh.triangles=pair.Value.ToArray();body.sharedMesh=mesh;Physics.SyncTransforms();
                if(Physics.ComputePenetration(probe,shield.position,shield.rotation,body,Vector3.zero,Quaternion.identity,out var dir,out var depth))hits.Add(new{part=pair.Key,depth,dir=dir.ToString()});}
            return hits;
        } finally {UnityEngine.Object.DestroyImmediate(bodyGo);UnityEngine.Object.DestroyImmediate(shieldGo);UnityEngine.Object.DestroyImmediate(mesh);}
    }
    public static object Inspect()
    {
        var locomotion = UnityEngine.Object.FindFirstObjectByType<NewPlayerLocomotion>();
        var player = locomotion.GetComponent<BattlePlayerController>();
        var shield = player.shield.transform;
        var bosses = UnityEngine.Object.FindObjectsByType<BossPhaseTwo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return new {
            player = player.name, visual = player.animator.transform.parent.name,
            roll = AssetDatabase.GetAssetPath(locomotion.rollClip), rollFrames = locomotion.rollClip.length * locomotion.rollClip.frameRate,
            shield = shield.name, position = shield.localPosition.ToString("F5"), rotation = shield.localEulerAngles.ToString("F3"), scale = shield.localScale.ToString(), worldScale = shield.lossyScale.ToString(),
            mountVector = (Quaternion.Inverse(shield.localRotation) * shield.localPosition * shield.parent.lossyScale.x).ToString("F4"),
            meshBounds = shield.GetComponent<MeshFilter>().sharedMesh.bounds.ToString("F4"),
            bosses = bosses.Select(p => { var g = p.GetComponent<BossPhaseTwoGimmick>(); return new {
                p.name, active = p.isActiveAndEnabled, pattern = AssetDatabase.GetAssetPath(p.entryAttack),
                state = p.entryAttack.animationState, length = p.entryAttack.clip.length,
                windows = p.entryAttack.hitWindows.Select(w => new { w.startSeconds, w.endSeconds, w.downwardExtension }).ToArray(),
                gimmick = g != null, gimmickEnabled = g != null && g.enabled,
                starts = g != null ? g.rotationStarts : null, strikes = g != null ? g.strikeTimes : null,
                damage = g != null ? g.failureDamage : p.entryAttack.damage
            }; }).ToArray()
        };
    }

    static Vector3[] SkinVertices(SkinnedMeshRenderer skin)
    {
        var mesh = skin.sharedMesh; var vertices = mesh.vertices; var weights = mesh.boneWeights;
        var matrices = skin.bones.Select((b, i) => b.localToWorldMatrix * mesh.bindposes[i]).ToArray();
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i]; var w = weights[i];
            vertices[i] = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
        }
        return vertices;
    }

    static Quaternion RollRotation(Transform hand, Transform chest, int mode, float turn)
    {
        var longAxis = mode == 1 ? Vector3.up : (hand.parent.position - hand.position).normalized;
        if (mode == 3) longAxis = -longAxis;
        var inward = Vector3.ProjectOnPlane(chest.position - hand.position, longAxis).normalized;
        if (inward.sqrMagnitude < .001f) inward = Vector3.ProjectOnPlane(chest.right, longAxis).normalized;
        return Quaternion.LookRotation(longAxis, Vector3.Cross(longAxis, inward)) * Quaternion.AngleAxis(turn, Vector3.right);
    }

    public static object RenderBefore() => Render(0, 0, 0, "RollShieldBefore");
    public static object RenderCandidate() => Render(2, 0, .09f, "RollShieldCandidate");
    public static object RenderIdle() => Render(4, 0, 0, "RollShieldIdleClearance");
    static object Render(int mode, float turn, float clearance, string filename)
    {
        var loco = Live; var player = loco.GetComponent<BattlePlayerController>();
        var visual = UnityEngine.Object.Instantiate(player.animator.transform.parent.gameObject); visual.hideFlags = HideFlags.HideAndDontSave;
        var cameraGo = new GameObject("Temporary roll shield camera"); var cam = cameraGo.AddComponent<Camera>(); cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.24f, .26f, .29f); cam.fieldOfView = 36;
        var rt = new RenderTexture(340, 420, 24); var tex = new Texture2D(2040, 840, TextureFormat.RGB24, false); var active = RenderTexture.active;
        var draw = new GameObject("Temporary roll mesh"); var mesh = new Mesh();
        try
        {
            visual.transform.SetPositionAndRotation(new Vector3(1000, 0, 1000), Quaternion.identity);
            var animator = visual.GetComponentInChildren<Animator>(); animator.enabled = false;
            var skin = visual.GetComponentInChildren<SkinnedMeshRenderer>();
            var shield = visual.GetComponentsInChildren<Transform>(true).First(t => t.name == player.shield.name); shield.gameObject.SetActive(true);
            var attachment = shield.localRotation; var position = shield.localPosition;
            var chest = skin.bones.First(b => b.name == "UpperChest");
            draw.AddComponent<MeshFilter>().sharedMesh = mesh; draw.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            cam.targetTexture = rt;
            for (int col = 0; col < 6; col++)
            {
                if (mode != 4) loco.rollClip.SampleAnimation(animator.gameObject, loco.rollClip.length * col / 5f);
                shield.localRotation = attachment; shield.localPosition = position;
                if (mode == 4) shield.position -= shield.right * (-.06f + col * .03f);
                else if (mode > 0)
                {
                    shield.parent.rotation = RollRotation(shield.parent, chest, mode, turn) * Quaternion.Inverse(attachment);
                    shield.position -= shield.right * clearance;
                }
                mesh.Clear(); mesh.vertices = SkinVertices(skin); mesh.subMeshCount = skin.sharedMesh.subMeshCount;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) mesh.SetTriangles(skin.sharedMesh.GetTriangles(sub), sub);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); skin.enabled = false;
                var focus = mesh.bounds.center;
                for (int row = 0; row < 2; row++)
                {
                    cam.transform.position = focus + (row == 0 ? new Vector3(-3.3f, 1f, 3.6f) : new Vector3(-3.8f, .8f, -3.1f));
                    cam.transform.LookAt(focus); cam.Render(); RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 340, 420), col * 340, (1 - row) * 420);
                }
            }
            tex.Apply(); string path = "Assets/3DBattle/Authoring~/" + filename + ".png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); return new { path, mode, turn, clearance };
        }
        finally
        {
            RenderTexture.active = active; cam.targetTexture = null;
            foreach (var o in new UnityEngine.Object[] { visual, cameraGo, rt, tex, draw, mesh }) if (o) UnityEngine.Object.DestroyImmediate(o);
        }
    }

    public static object SearchRoll()
    {
        var loco = Live; var player = loco.GetComponent<BattlePlayerController>();
        var visual = UnityEngine.Object.Instantiate(player.animator.transform.parent.gameObject); visual.hideFlags = HideFlags.HideAndDontSave;
        var owned = new List<UnityEngine.Object> { visual };
        try
        {
            visual.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = visual.GetComponentInChildren<Animator>(); animator.enabled = false;
            var shield = visual.GetComponentsInChildren<Transform>(true).First(t => t.name == player.shield.name);
            var hand = shield.parent; var attachment = shield.localRotation; var mount = Quaternion.Inverse(attachment) * shield.localPosition * hand.lossyScale.x;
            var skin = visual.GetComponentInChildren<SkinnedMeshRenderer>(); var chest = skin.bones.First(b => b.name == "UpperChest");
            var weights = skin.sharedMesh.boneWeights;
            Func<int, float> grip = i => { var w = weights[i]; float total = 0;
                int[] ids = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 }; float[] ws = { w.weight0, w.weight1, w.weight2, w.weight3 };
                for (int j = 0; j < 4; j++) if (skin.bones[ids[j]].name == "LeftHand") total += ws[j]; return total; };
            var ts = skin.sharedMesh.triangles; var triangles = new List<int>();
            for (int i = 0; i < ts.Length; i += 3) if ((grip(ts[i]) + grip(ts[i + 1]) + grip(ts[i + 2])) / 3 < .5f)
                triangles.AddRange(new[] { ts[i], ts[i + 1], ts[i + 2] });
            var bodies = new List<MeshCollider>(); var positions = new List<Vector3>(); var rotations = new List<Quaternion[]>();
            for (int f = 0; f <= 31; f++)
            {
                loco.rollClip.SampleAnimation(animator.gameObject, loco.rollClip.length * f / 31f);
                positions.Add(hand.position);
                rotations.Add(new[] { shield.rotation, RollRotation(hand, chest, 1, 0), RollRotation(hand, chest, 2, 0), RollRotation(hand, chest, 3, 0) });
                var vertices = SkinVertices(skin).ToList(); var tris = new List<int>(triangles);
                foreach (var mf in visual.GetComponentsInChildren<MeshFilter>(true))
                    if (!mf.transform.IsChildOf(shield) && !mf.name.Contains("Sword") && mf.GetComponent<Renderer>()?.enabled == true)
                    { int start = vertices.Count; vertices.AddRange(mf.sharedMesh.vertices.Select(v => mf.transform.TransformPoint(v))); tris.AddRange(mf.sharedMesh.triangles.Select(t => t + start)); }
                var mesh = new Mesh(); mesh.vertices = vertices.ToArray(); mesh.triangles = tris.ToArray(); mesh.RecalculateBounds(); owned.Add(mesh);
                var go = new GameObject("Temporary roll body collider"); owned.Add(go);
                var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; bodies.Add(collider);
            }
            var probeGo = new GameObject("Temporary roll shield collider"); owned.Add(probeGo);
            probeGo.transform.localScale = shield.lossyScale; var probe = probeGo.AddComponent<MeshCollider>();
            probe.sharedMesh = shield.GetComponent<MeshFilter>().sharedMesh; probe.convex = true; Physics.SyncTransforms();
            Func<int, float, float, float[]> score = (mode, turn, clearance) => {
                float max = 0, sum = 0, count = 0;
                for (int i = 0; i < bodies.Count; i++)
                {
                    var rotation = rotations[i][mode] * Quaternion.AngleAxis(turn, Vector3.right);
                    var position = positions[i] + rotation * (mount - Vector3.right * clearance);
                    if (Physics.ComputePenetration(probe, position, rotation, bodies[i], Vector3.zero, Quaternion.identity, out var dir, out var depth))
                    { max = Mathf.Max(max, depth); sum += depth; count++; }
                }
                return new[] { max, sum, count };
            };
            var candidates = new List<(int mode, float turn, float clearance, float[] score, float rank)>();
            for (int mode = 1; mode <= 3; mode++) for (int angle = -60; angle <= 60; angle += 15) for (int d = 0; d <= 10; d++)
            {
                var s = score(mode, angle, d * .02f);
                candidates.Add((mode, angle, d * .02f, s, s[0] * 1000 + s[1] * 10 + s[2] + d * .02f + Mathf.Abs(angle) * .0001f));
            }
            return new { baseline = score(0, 0, 0), results = candidates.OrderBy(c => c.rank).Take(12).Select(c => new { c.mode, c.turn, c.clearance, c.score }).ToArray() };
        }
        finally { foreach (var o in owned) if (o) UnityEngine.Object.DestroyImmediate(o); }
    }
}
