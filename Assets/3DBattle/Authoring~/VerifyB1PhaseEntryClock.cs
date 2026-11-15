using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;

// Exercises Update before Animator evaluation, including the frame in which Play is requested.
public static class VerifyB1PhaseEntryClock
{
    static void Call(object o, string method) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(o, null);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static object Diagnose() => Run(false);
    public static object Verify() => Run(true);
    public static async Task<object> VerifyLiveTransition()
    {
        Require(Application.isPlaying, "Play mode required"); EditorApplication.isPaused = true;
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var intro = battle.GetComponent<BattleIntroWalkDebug>(); if (intro != null) { intro.Stop(); intro.autoPlayOnStageStart = false; }
        battle.SelectStage(1);
        var boss = battle.CurrentStage.boss; var phase = boss.GetComponent<BossPhaseTwo>();
        var actions = boss.GetComponent<BossActionController>(); var gimmick = boss.GetComponent<BossPhaseTwoGimmick>();
        var player = battle.player; var health = player.GetComponent<BattlePlayerHealth>(); var animator = boss.animator;
        float oldMaximum = health.maxHealth; bool oldBackground = Application.runInBackground;
        var events = new List<object>(); int frames = Time.frameCount; float lastHp = 1000; string previous = "";
        try
        {
            actions.ResetController(); boss.ResetBoss();
            boss.GetComponent<BossApproachController>().automaticApproach = false;
            health.maxHealth = 1000; health.ignoreDamage = false; health.ResetHealth();
            player.CancelInput(); player.enabled = false;
            var motor = player.GetComponent<CharacterController>(); motor.enabled = false;
            player.transform.position = boss.transform.position + Vector3.forward * 3.8f; motor.enabled = true;
            animator.speed = 1; animator.Play(boss.idleState, 0, .4f); animator.Update(0);
            Application.runInBackground = true;
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            EditorWindow.GetWindow(gameViewType).Focus();
            boss.TakeDamage(boss.maxHealth * .51f);
            EditorApplication.isPaused = false;
            var deadline = DateTime.UtcNow.AddSeconds(24);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(16);
                var state = animator.GetCurrentAnimatorStateInfo(0); float clipTime = state.normalizedTime * phase.entryAttack.clip.length;
                string marker = actions.Current + "/" + gimmick.Rotation + "/" + health.Health;
                if (marker != previous)
                {
                    events.Add(new { frame = Time.frameCount - frames, action = actions.Current.ToString(), phase = phase.Phase, rotation = gimmick.Rotation,
                        health = health.Health, attackState = state.fullPathHash == Animator.StringToHash(phase.entryAttack.animationState), clipTime, gimmick.Status }); previous = marker;
                }
                if (health.Health < lastHp)
                {
                    Require(state.fullPathHash == Animator.StringToHash(phase.entryAttack.animationState), "Live damage before attack state");
                    Require(clipTime >= gimmick.strikeTimes[0] && gimmick.Rotation >= 2, "Live damage before first valid strike");
                    lastHp = health.Health;
                }
                if (gimmick.OwnsAttack && gimmick.Successes + gimmick.Failures >= 2)
                {
                    Require(health.Health == 1000 - gimmick.Failures * gimmick.failureDamage, "Unexpected damage source");
                    return new { passed = true, frames = Time.frameCount - frames, health = health.Health, gimmick.Failures, gimmick.Successes, events };
                }
            }
            throw new Exception("Live phase transition did not reach two beats: " + previous + ", frames=" + (Time.frameCount - frames));
        }
        finally
        {
            EditorApplication.isPaused = true; gimmick.ResetGimmick(); actions.ResetController(); phase.ResetPhase();
            health.maxHealth = oldMaximum; health.ResetHealth(); Application.runInBackground = oldBackground;
        }
    }
    static object Run(bool requireFixed)
    {
        Require(Application.isPlaying, "Play mode required"); EditorApplication.isPaused = true;
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>(); battle.SelectStage(1);
        var boss = battle.CurrentStage.boss; var phase = boss.GetComponent<BossPhaseTwo>();
        var actions = boss.GetComponent<BossActionController>(); var gimmick = boss.GetComponent<BossPhaseTwoGimmick>();
        var player = battle.player; var health = player.GetComponent<BattlePlayerHealth>(); var animator = boss.animator;
        boss.GetComponent<BossApproachController>().automaticApproach = false;
        health.ignoreDamage = false;
        var results = new List<object>();
        try
        {
            foreach (float previousTime in new[] { .2f, .4f, .65f, 1.25f })
            {
                actions.ResetController(); boss.ResetBoss(); health.ResetHealth();
                var motor = player.GetComponent<CharacterController>(); motor.enabled = false;
                player.transform.position = boss.transform.position + Vector3.forward * 3.8f; motor.enabled = true;
                animator.speed = 1;
                Require(phase.ForcePhaseTwo(), "Could not request phase2"); phase.Tick(0);
                Require(actions.Current == BossActionKind.PhaseChange, "Missing transition");
                animator.Play(phase.transitionState, 0, 1); animator.Update(0); actions.Tick(0); phase.Tick(0);
                // This is the still-current idle/approach state on the frame that the phase requests the attack.
                animator.Play(boss.idleState, 0, previousTime); animator.Update(0);
                phase.Tick(phase.waitAfterSkill + .01f);
                Require(gimmick.Locked && actions.Current == BossActionKind.Attack, "Entry not started");
                var state = animator.GetCurrentAnimatorStateInfo(0);
                float before = health.Health;
                Call(gimmick, "Update"); // Intentionally do NOT evaluate the Animator first.
                float after = health.Health; bool locked = gimmick.Locked; int failures = gimmick.Failures;
                bool invulnerable = phase.IsInvulnerable;
                if (requireFixed)
                {
                    Require(after == before && failures == 0, "Damage used stale state at " + previousTime);
                    Require(locked && invulnerable && gimmick.Rotation == 1, "Controls/invulnerability ended before attack state at " + previousTime);
                    Require(!gimmick.PressParry(gimmick.strikeTimes[0]), "Input accepted before attack became active");
                    animator.Update(0);
                    Require(animator.GetCurrentAnimatorStateInfo(0).fullPathHash == Animator.StringToHash(phase.entryAttack.animationState), "Attack animation not entered");
                    Call(gimmick, "Update"); Require(health.Health == before && gimmick.Failures == 0, "Damage at attack frame zero");
                    for (int i = 1; i <= 40; i++)
                    {
                        float time = (gimmick.strikeTimes[0] - .001f) * i / 40;
                        animator.Play(phase.entryAttack.animationState, 0, time / phase.entryAttack.clip.length); animator.Update(0);
                        Call(gimmick, "Update"); boss.GetComponent<BossWeaponHit>().SampleContact();
                        Require(health.Health == before && gimmick.Failures == 0, "Damage before first valid beat");
                    }
                    animator.Play(phase.entryAttack.animationState, 0, (gimmick.strikeTimes[0] + .001f) / phase.entryAttack.clip.length); animator.Update(0); Call(gimmick, "Update");
                    Require(health.Health == before - gimmick.failureDamage && gimmick.Failures == 1, "First real strike suppressed");
                    Call(gimmick, "Update"); Require(gimmick.Failures == 1, "Same strike applied twice");
                    animator.Play(phase.entryAttack.animationState, 0, (gimmick.strikeTimes[1] - .05f) / phase.entryAttack.clip.length); animator.Update(0);
                    Require(gimmick.PressParry(gimmick.strikeTimes[1] - .05f), "Valid parry rejected");
                    animator.Play(phase.entryAttack.animationState, 0, (gimmick.strikeTimes[1] + .001f) / phase.entryAttack.clip.length); animator.Update(0); Call(gimmick, "Update");
                    Require(gimmick.Successes == 1 && health.Health == before - gimmick.failureDamage, "Successful parry caused damage");
                    for (int i = 2; i < 7; i++)
                    {
                        animator.Play(phase.entryAttack.animationState, 0, (gimmick.strikeTimes[i] - .05f) / phase.entryAttack.clip.length); animator.Update(0);
                        Require(gimmick.PressParry(gimmick.strikeTimes[i] - .05f), "Later parry rejected");
                        animator.Play(phase.entryAttack.animationState, 0, (gimmick.strikeTimes[i] + .001f) / phase.entryAttack.clip.length); animator.Update(0); Call(gimmick, "Update");
                    }
                    Require(gimmick.Successes == 6 && gimmick.Failures == 1, "Seven independent strikes changed");
                    animator.Play(phase.entryAttack.animationState, 0, (gimmick.rotationStarts[8] + .001f) / phase.entryAttack.clip.length); animator.Update(0); Call(gimmick, "Update");
                    Require(!gimmick.Locked && !phase.IsInvulnerable, "Rotation9 failed to release controls/invulnerability");
                    animator.Play(phase.entryAttack.animationState, 0, .999f); animator.Update(0); Call(gimmick, "Update");
                    Require(health.Health == before - gimmick.failureDamage, "Last two rotations caused damage");
                }
                results.Add(new { previousTime, previousState = state.fullPathHash, expectedAttackState = Animator.StringToHash(phase.entryAttack.animationState), staleClockSeconds = state.normalizedTime * phase.entryAttack.clip.length,
                    before, after, failures, locked, invulnerable, validated = requireFixed });
            }
        }
        finally { gimmick.ResetGimmick(); actions.ResetController(); phase.ResetPhase(); health.ResetHealth(); }
        return results;
    }
}
