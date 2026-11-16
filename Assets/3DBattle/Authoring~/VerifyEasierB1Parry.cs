using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using OneWay.Battle3D;

public static class VerifyEasierB1Parry
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Tick(BossPhaseTwoGimmick g) => typeof(BossPhaseTwoGimmick).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(g, null);
    public static object Run()
    {
        Require(Application.isPlaying, "Play mode required"); EditorApplication.isPaused = true;
        var battle = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var intro = battle.GetComponent<BattleIntroWalkDebug>(); if (intro != null) { intro.Stop(); intro.autoPlayOnStageStart = false; }
        battle.SelectStage(1);
        var boss = battle.CurrentStage.boss; var phase = boss.GetComponent<BossPhaseTwo>();
        var actions = boss.GetComponent<BossActionController>(); var g = boss.GetComponent<BossPhaseTwoGimmick>();
        var player = battle.player; var health = player.GetComponent<BattlePlayerHealth>(); var animator = boss.animator;
        boss.GetComponent<BossApproachController>().automaticApproach = false; health.ignoreDamage = false;
        var results = new List<object>();
        try
        {
            for (int scenario = 0; scenario < 4; scenario++)
            {
                actions.ResetController(); boss.ResetBoss(); health.ResetHealth();
                var motor = player.GetComponent<CharacterController>(); motor.enabled = false;
                player.transform.position = boss.transform.position + Vector3.forward * 3.8f; motor.enabled = true;
                animator.speed = scenario == 1 ? 1.5f : 1;
                phase.ForcePhaseTwo(); phase.Tick(0); animator.Play(phase.transitionState, 0, 1); animator.Update(0);
                actions.Tick(0); phase.Tick(0); phase.Tick(phase.waitAfterSkill + .01f); animator.Update(0);
                Require(g.Locked, "Gimmick did not begin");
                Require(g.lateParryStartRotation == 2 && Mathf.Approximately(g.lateParryWindow, .4f), "Rotations2/3 not configured for 0.4s");
                Action<float> sample = t => { animator.Play(phase.entryAttack.animationState, 0, t / phase.entryAttack.clip.length); animator.Update(0); };
                sample(0); Tick(g); Require(health.Health == 100 && g.Failures == 0, "Damage before first rotation");
                Require(!g.PressParry(0), "Input at attack start was buffered too early");
                int buffered = 0;
                int missedStrike = scenario >= 2 ? scenario - 2 : -1;
                for (int i = 0; i < 7; i++)
                {
                    float beat = g.strikeTimes[i]; float speed = animator.speed;
                    if (i + 2 < g.lateParryStartRotation)
                    {
                        sample(beat - .35f * speed);
                        Require(!g.PressParry(beat - .35f * speed), "Early rotations unexpectedly widened");
                        sample(beat - .1f * speed); Require(g.PressParry(beat - .1f * speed), "Original parry rejected");
                    }
                    else if (i != missedStrike)
                    {
                        float outside = beat - .401f * speed;
                        sample(outside); Require(!g.PressParry(outside), "Input outside 0.4s was accepted");
                        float inside = beat - .399f * speed;
                        sample(inside); Require(g.PressParry(inside), "Buffered parry rejected on rotation " + (i + 2));
                        if (inside < g.rotationStarts[i + 1]) buffered++;
                        sample(beat - .05f * speed); Require(!g.PressParry(beat - .05f * speed), "Duplicate input accepted");
                    }
                    float before = health.Health;
                    sample(beat - .001f); Tick(g); Require(health.Health == before, "Damage occurred before strike");
                    Require(g.Successes + g.Failures == i, "Buffered input resolved before its strike");
                    sample(beat + .001f); Tick(g);
                    if (i == missedStrike) Require(health.Health == before - 20, "No-input failure was removed/delayed");
                    else Require(health.Health == before, "Accepted parry still caused damage");
                }
                Require(g.Successes == (missedStrike >= 0 ? 6 : 7) && g.Failures == (missedStrike >= 0 ? 1 : 0), "Wrong strike results");
                sample(g.rotationStarts[8] + .001f); Tick(g);
                Require(!g.Locked && !phase.IsInvulnerable, "Controls were not released at rotation9");
                results.Add(new { scenario, speed = animator.speed, g.Successes, g.Failures, health = health.Health, bufferedBeforeRotation = buffered });
            }
        }
        finally { g.ResetGimmick(); actions.ResetController(); phase.ResetPhase(); health.ResetHealth(); animator.speed = 1; }
        return results;
    }
}
