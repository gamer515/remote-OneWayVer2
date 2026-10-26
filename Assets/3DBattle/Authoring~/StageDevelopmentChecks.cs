using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using OneWay.Battle3D;

public static class StageDevelopmentChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static string EditMode()
    {
        Check(!Application.isPlaying, "Run EditMode checks outside Play.");
        var controller = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        Check(controller != null && controller.stages.Length == 3, "Three stages are connected.");
        int original = controller.SelectedStageIndex;
        try
        {
            foreach (var stage in controller.stages)
            {
                Check(controller.SelectStage(stage.StageIndex), "Stage selection succeeds.");
                Check(controller.stages.Count(s => s.gameObject.activeSelf) == 1, "Only one stage is active.");
                Check(controller.player.boss == stage.boss && controller.feedback.boss == stage.boss, "Boss references switch together.");
                Check(stage.boss.transform.IsChildOf(stage.transform), "Boss belongs to selected stage.");
                Check(stage.settings.patterns.All(p => p != null), "Patterns assigned.");
                var animatorController = stage.boss.animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                Check(animatorController != null && animatorController.layers[0].stateMachine.states.Any(s => s.state.name == "Attack" && s.state.motion != null), "Boss attack animation connected.");
            }
            Check(!controller.SelectStage(999), "Unknown stages are rejected.");
            var data = new Battle3DStartData(8, 2, 3, 4, 5);
            Check(data.WithStage(12).Trait4 == 5, "Stages beyond B4 retain traits.");
            Battle3DEntry.SetNext(data);
            Check(Battle3DEntry.TryTake(out var taken) && taken.StageIndex == 8, "Entry data received.");
            Check(!Battle3DEntry.TryTake(out _), "Entry data consumed only once.");
            return "PASS: stage preview, exclusive activation, references, patterns/animation, B4+ data, one-shot entry.";
        }
        finally { controller.SelectStage(original); }
    }

    public static async Task<string> PlayMode()
    {
        Check(Application.isPlaying, "Run PlayMode checks during Play.");
        bool previousBackground = Application.runInBackground;
        Application.runInBackground = true;
        try
        {
        var controller = UnityEngine.Object.FindFirstObjectByType<Battle3DSceneController>();
        var health = controller.player.GetComponent<BattlePlayerHealth>();
        controller.SelectStage(1);
        var stage = controller.CurrentStage;
        Check(controller.RunPattern(0), "Pattern starts.");
        Check(stage.patternRunner.IsRunning && stage.patternRunner.telegraph.enabled, "Windup is visible.");
        controller.SelectStage(2);
        Check(!stage.patternRunner.IsRunning && !stage.patternRunner.telegraph.enabled, "Switch cancels previous attack.");
        Check(controller.player.boss == controller.CurrentStage.boss, "Runtime boss reference updated.");

        foreach (var next in controller.stages)
        {
            controller.SelectStage(next.StageIndex);
            Check(health.Health == health.maxHealth && next.boss.Health == next.settings.bossHealth, "Health reset.");
            Check(Vector3.Distance(controller.player.transform.position, next.playerSpawn.position) < 0.01f, "Spawn reset.");
        }

        controller.SelectStage(1);
        stage = controller.CurrentStage;
        var motor = controller.player.GetComponent<CharacterController>();
        motor.enabled = false;
        controller.player.transform.position = stage.boss.transform.position + Vector3.forward * 3f;
        motor.enabled = true;
        var attack = (BossAreaAttackPattern)stage.settings.patterns[0];
        Check(controller.RunPattern(0), "Specific attack starts.");
        for (int i = 0; i < 100 && stage.patternRunner.IsRunning; i++) await Task.Delay(100);
        Check(health.Health == health.maxHealth - attack.damage, "Attack deals configured damage once.");
        Check(!stage.patternRunner.IsRunning && !stage.patternRunner.telegraph.enabled, "Pattern finishes and cleans up.");

        controller.SelectStage(1);
        Check(controller.RunPattern(0), "Cancelable attack starts.");
        controller.StopPattern();
        await Task.Delay((int)((attack.windupSeconds + 0.2f) * 1000f));
        Check(health.Health == health.maxHealth && !stage.patternRunner.IsRunning, "Cancel prevents delayed damage.");
        Check(!controller.RunPattern(-1) && !controller.RunPattern(999), "Invalid pattern indexes rejected.");
        controller.SelectStage(1);
        return "PASS: runtime switching, attack cancellation, HP/spawn reset, telegraph, selected attack damage and cleanup.";
        }
        finally { Application.runInBackground = previousBackground; }
    }
}
