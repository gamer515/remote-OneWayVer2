using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MineChapterRevisionChecks
{
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Edit mode required.");
        var checks = new List<string>();
        void Check(bool ok, string description) { if (!ok) throw new Exception(description); checks.Add(description); }
        ChapterContentCatalog.Reset();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Decision_YYS/Prafabs/Terrain/MineVillageFirstChunk.prefab");
        var config = UnityEngine.Object.FindFirstObjectByType<MineNpcPlacement>();
        Check(config.Places.Length == 4 && config.Places.All(g => g.spawnPoints.Length == 5), "Four Inspector place groups each expose five authored spawn points");
        Check(config.GetComponentsInChildren<Renderer>(true).Length == 0 && config.GetComponentsInChildren<Collider>(true).Length == 0, "Spawn configuration has no visible mesh or collider");
        TerrainData Layout(int seed) { var t = new TerrainRepository(1, seed).Load("Chapter_1/Chapter_1_01/Terrain"); config.Apply(t, seed, config.transform.position); return t; }
        var layouts = Enumerable.Range(1, 20).Select(Layout).ToArray();
        Check(layouts.All(t => t.places.Length == 4 && t.places.All(p => p.spawnPoints.Length == 5 && p.spawnPoints.Contains(p.position))), "Every selected position belongs to its place's five points");
        Check(Layout(12).places.Select(p => p.position).SequenceEqual(layouts[11].places.Select(p => p.position)), "Restoring the same run seed preserves NPC positions");
        Check(layouts.Select(t => t.places[0].position).Distinct().Count() > 1, "Different run seeds select different NPC positions");
        var sockets = prefab.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("ActorSocket_")).ToArray();
        Check(sockets.Length == 8 && sockets.All(t => t.GetComponentsInChildren<Renderer>(true).Length > 0), "All eight supplied NPC models are connected to their place sockets");
        Check(layouts.All(t => t.places.All(p => p.actorPositions.Length == 2 && p.actorPositions[0] != p.actorPositions[1])), "The two NPCs never select the same spawn point");
        Check(layouts.All(t => t.places[3].actorPositions[0].z < config.Places[3].entrance.z && t.places[3].actorPositions[1].z > config.Places[3].entrance.z), "Nea stays outside and Sia stays beside the doorway");
        Vector3 old = config.Places[0].spawnPoints[0];
        try { config.Places[0].spawnPoints[0] += Vector3.right; Check(Layout(12).places[0].spawnPoints[0] == old + Vector3.right, "Inspector coordinate changes feed runtime terrain data"); }
        finally { config.Places[0].spawnPoints[0] = old; }
        var repo = new EncounterContentRepository();
        Check(repo.TryLoadCards("Chapter_1/Chapter_1_01/Encounters/brook_pay", out var pay, out var error), "Payment source loads: " + error);
        Check(pay.MainStory.Last().choiceActions.All(c => c.insufficientStoryPath == "Chapter_1/Chapter_1_01/Encounters/brook_insufficient"), "All four Brook payment options route shortage to the authored branch");
        Check(repo.TryLoadCards("Chapter_1/Chapter_1_01/Encounters/brook_insufficient", out var shortage, out error) && shortage.MainStory.Count == 3 && shortage.MainStory.Last().chapterAction.kind == "duel", "Three exact shortage speeches lead to a duel");
        Check(!ChapterContentCatalog.CanRewrite("Chapter_1/Chapter_1_01/Encounters/brook_insufficient"), "Authored shortage speeches cannot be rewritten by AI");
        repo.TryLoadCards("Chapter_1/Chapter_1_01/Encounters/rumor_1", out var rumor, out error);
        Check(rumor.MainStory[1].text == "켄타우로스라고? 진짜 다리가 네 개라고?", "Dor's fixed rumor correction is exact");
        var source = UnityEngine.Object.FindFirstObjectByType<MineCollectorProcession>();
        Check(source != null && source.RowWaitSeconds == 3, "Inspector exposes a three-second row wait on MineTaxCollectors");
        var holder = new GameObject("ProcessionChecks") { hideFlags = HideFlags.HideAndDontSave }; holder.SetActive(false);
        try
        {
            var ctrl = holder.AddComponent<MineCollectorProcession>(); EditorUtility.CopySerialized(source, ctrl);
            var state = new MineChapterProgress();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AiAsset/MineChunkModularPack/Prefabs/NPCs/NPC_Inspector.prefab");
            var env = UnityEngine.Object.FindFirstObjectByType<EnvController>();
            ctrl.Initialize(model, state, env.TerrainOrigin, 120, 42);
            var members = (Transform[])typeof(MineCollectorProcession).GetField("members", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);
            state.taxPatrolActive = true;
            ctrl.BeginRoute(env.TerrainOrigin.z + 120);
            Check(ctrl.Count >= 3 && ctrl.Count <= 5 && ctrl.CurrentPhase == "RowMarch" && ctrl.CanMeet, "Three to five members start at the loaded boundary and can meet the player while descending");
            float before = state.collectorZ; ctrl.Tick(0.5f);
            Check(state.collectorZ < before, "Start-of-game arrival moves in direction 4 to 1");
            int guard = 0;
            while (ctrl.CurrentPhase != "Wait" && guard++ < 3000) ctrl.Tick(0.1f);
            Check(ctrl.CurrentPhase == "Wait" && members.All(t => Mathf.Abs(t.position.z - state.collectorZ) < 0.05f) && members.Select(t => t.position.x).Distinct().Count() == ctrl.Count, "Collectors form a horizontal row after exiting a doorway");
            ctrl.Tick(2.9f); Check(ctrl.CurrentPhase == "Wait", "Row does not leave before three seconds");
            ctrl.Tick(0.11f); guard = 0;
            Check(ctrl.CurrentPhase == "RowMarch", "Outside movement starts in a horizontal row after waiting");
            ctrl.Tick(0.5f);
            Check(members.All(t => Mathf.Abs(t.position.z - state.collectorZ) < 0.05f), "Moving collectors retain a horizontal row outside the doorway");
            while (ctrl.CurrentPhase != "March" && guard++ < 1000) ctrl.Tick(0.1f);
            Check(ctrl.CurrentPhase == "March" && members.All(t => Mathf.Abs(t.position.x - env.TerrainOrigin.x) < 0.05f), "Members form a single-file line before entering a building");
            before = state.collectorZ; ctrl.Tick(0.5f);
            Check(state.collectorZ < before, "In-game procession continues toward the start");
            guard = 0;
            while (!(ctrl.CurrentPhase == "Wait" && state.collectorStop == 1) && guard++ < 4000) ctrl.Tick(0.1f);
            Check(ctrl.CurrentPhase == "Wait" && state.collectorStop == 1 && members.All(t => Mathf.Abs(t.position.z - state.collectorZ) < 0.05f), "After exiting the first building, all members reform a horizontal row");
            before = state.collectorZ; ctrl.ContinueForward(); ctrl.Tick(0.5f);
            Check(state.collectorZ < before && state.collectorPassedPlayer && !ctrl.CanMeet, "After meeting the player, collectors keep descending without repeat taxation");
            var restored = JsonUtility.FromJson<MineChapterProgress>(JsonUtility.ToJson(state));
            Check(restored.collectorPhase == state.collectorPhase && restored.collectorCount == state.collectorCount && restored.collectorPassedPlayer, "Procession phase and encounter completion survive save serialization");
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); }
        var result = new { passed = true, count = checks.Count, checks };
        Directory.CreateDirectory("output/MineChapterRevision");
        File.WriteAllText("output/MineChapterRevision/checks.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
        return result;
    }
}
