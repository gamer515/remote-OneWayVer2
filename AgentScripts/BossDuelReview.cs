using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class BossDuelReview
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    public static string WalkToLastPlace()
    {
        if (!Application.isPlaying || !SaveIOService.Instance.StorageRoot.Contains("MineChapterReview")) throw new Exception("Isolated review required");
        var manager = UnityEngine.Object.FindFirstObjectByType<DecisionManager>();
        var stats = (StatContainer)typeof(DecisionManager).GetField("statContainer", Hidden).GetValue(manager);
        stats.SetStats(new[] {100,100,100,100});
        ((GuideProgress)typeof(DecisionManager).GetField("guideProgress", Hidden).GetValue(manager)).committedStats = stats.stats;
        typeof(DecisionManager).GetMethod("SyncAbilityWallet", Hidden).Invoke(manager, null);
        double next = 0, deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.CallbackFunction step = null;
        step = () =>
        {
            if (manager == null || !Application.isPlaying || EditorApplication.timeSinceStartup > deadline) { EditorApplication.update -= step; return; }
            if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + .3;
            string state = typeof(DecisionManager).GetField("currentState", Hidden).GetValue(manager).ToString();
            string place = (string)typeof(DecisionManager).GetField("activePlaceId", Hidden).GetValue(manager);
            if (place == "nea_sia" && state == "ShowingStory") { EditorApplication.update -= step; EditorApplication.isPaused = true; return; }
            if ((bool)typeof(DecisionManager).GetField("collectingTax", Hidden).GetValue(manager))
            {
                UnityEngine.Object.FindFirstObjectByType<MineChapterActivityView>()?.GetComponentsInChildren<UnityEngine.UI.Button>().FirstOrDefault()?.onClick.Invoke(); return;
            }
            if (state == "WaitingForChoice") manager.OnJourneyGearSelected(0);
            if (state == "ShowingStory" || state == "WaitingForChoice" || state == "Exploring") manager.OnJourneyYellowPressed();
        };
        EditorApplication.update += step; EditorApplication.isPaused = false;
        return "Walk through actual inputs to the fourth place, then pause for map inspection.";
    }
}
