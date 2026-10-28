using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Offline data preparation only: no inference, save access, policy changes or game application.
public static class ExportTrainingReview
{
    public static object Run()
    {
        if (Application.isPlaying || LocalStoryRuntimeService.IsBusy)
            throw new InvalidOperationException("Stop gameplay/generation before exporting review inputs.");
        const string folder = "Tests/LocalStoryBenchmark/";
        var review = JObject.Parse(File.ReadAllText(folder + "AnswerReviewDrafts.json"));
        var baseline = JObject.Parse(File.ReadAllText(folder + "TrainingBaseline.json"));
        var split = JObject.Parse(File.ReadAllText(folder + "DatasetSplitPlan.json"));
        using (var sha = SHA256.Create())
            foreach (var item in baseline["frozenFiles"])
            {
                string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes((string)item["path"]))).Replace("-", "").ToLowerInvariant();
                if (hash != (string)item["sha256"])
                    throw new InvalidDataException("Frozen source changed: " + item["path"]);
            }
        ChapterContentCatalog.Reset();
        var suite = JObject.Parse(File.ReadAllText(folder + "CompositionCases.json"));
        var entries = new JArray();
        foreach (var draft in review["drafts"].OfType<JObject>())
        {
            if ((bool?)draft["reviewedByUser"] != true)
                throw new InvalidDataException("Unapproved answer: " + draft["id"]);
            string path = (string)draft["provenance"]["storyPath"];
            int start = (int)draft["provenance"]["sectionStart"];
            var scenario = suite["cases"].Single(c => (string)c["storyPath"] == path);
            var memory = scenario["memory"].ToObject<LocalStoryRevisionRequest>();
            var source = JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
            var ranges = LocalStoryPrompt.Plan(path, source);
            if (!ranges.Any(r => r.start == start && r.count == 1))
                throw new InvalidDataException("Current section no longer matches reviewed answer: " + draft["id"]);
            var accepted = new List<EncounterCard>();
            for (int i = 0; i < start; i++)
            {
                var prior = JsonUtility.FromJson<EncounterCard>(JsonUtility.ToJson(source.MainStory[i]));
                var approved = review["drafts"].FirstOrDefault(d =>
                    (string)d["provenance"]["storyPath"] == path && (int)d["provenance"]["sectionStart"] == i);
                if (approved != null)
                {
                    prior.text = (string)approved["proposedOutput"]["cards"][0]["text"];
                    prior.options = approved["proposedOutput"]["cards"][0]["options"].Values<string>().ToArray();
                }
                accepted.Add(prior);
            }
            string messages = LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,
                LocalStoryPrompt.Messages(path, source, start, 1, accepted, memory));
            string answer = draft["proposedOutput"].ToString(Formatting.None);
            var slice = LocalStorySections.Slice(source, start, 1);
            bool valid = LocalStoryRewrite.TryBuild(answer, slice, memory, out var built, out var error);
            bool bindings = valid && LocalStoryRewrite.SameBindings(slice, built);
            bool situation = valid && LocalStorySections.ValidateSituation(path, built.MainStory[0], built.MainStory[0].text, accepted, out error);
            var limits = LocalStoryPrompt.Limits(path, LocalStoryPrompt.Paragraph(path, source.MainStory[start], start));
            bool lengths = ((string)draft["proposedOutput"]["cards"][0]["text"]).Length <= limits.textMaxChars &&
                draft["proposedOutput"]["cards"][0]["options"].All(o => ((string)o).Length <= limits.optionMaxChars);
            string sourceFile = ChapterContentCatalog.SourceFile(ChapterContentCatalog.Info(path));
            var group = split["groups"].Single(g => sourceFile.StartsWith((string)g["sourceFolder"], StringComparison.Ordinal));
            bool eligible = valid && bindings && situation && lengths && (string)group["role"] == "train" &&
                (string)draft["currentInputSemanticReview"]?["status"] == "consistent_with_current_input_by_agent";
            entries.Add(new JObject
            {
                ["id"] = draft["id"].DeepClone(), ["sceneGroup"] = draft["sceneGroup"].DeepClone(),
                ["leakageGroup"] = "npc:" + string.Join(",", source.MainStory.Select(c => c.speakerId).Distinct().OrderBy(id => id)),
                ["storyPath"] = path, ["eventId"] = source.MainStory[start].eventIdStable,
                ["sectionStart"] = start, ["answerApprovedByUser"] = true,
                ["inputSource"] = "Current LocalStoryPrompt + Gemma adapter; no inference",
                ["previousDialogueSource"] = "Authored source with earlier approved answers substituted; no failed generated dialogue",
                ["messages"] = JArray.Parse(messages), ["expectedOutput"] = draft["proposedOutput"].DeepClone(),
                ["grammar"] = LocalStoryRewrite.Grammar(slice, true, limits),
                ["maxTokens"] = LocalStoryPrompt.MaxTokens(path, source, start, 1),
                ["generationPolicyHash"] = LocalStorySections.GenerationPolicyHash(path),
                ["validation"] = new JObject { ["tryBuild"] = valid, ["sameBindings"] = bindings,
                    ["situation"] = situation, ["lengths"] = lengths, ["error"] = error },
                ["semanticReview"] = draft["currentInputSemanticReview"]?.DeepClone(),
                ["splitGroup"] = group["id"].DeepClone(), ["split"] = group["role"].DeepClone(),
                ["trainingEligible"] = eligible,
                ["status"] = (string)draft["currentInputSemanticReview"]?["status"] == "revision_pending_user_approval"
                    ? "answer_revision_required" : eligible ? "approved_answer_aligned_train_pair" : "excluded_from_training"
            });
        }
        var catalog = new JArray(ChapterContentCatalog.Stories.Select(s => new JObject
        {
            ["path"] = s["path"]?.DeepClone(), ["storyFile"] = s["storyFile"]?.DeepClone(),
            ["explanationFile"] = s["explanationFile"]?.DeepClone()
        }));
        var output = new JObject { ["schemaVersion"] = 1, ["baselineId"] = baseline["baselineId"].DeepClone(),
            ["purpose"] = "Current inputs with explicit training eligibility; unresolved answers excluded", ["inferenceExecuted"] = false,
            ["saveAccess"] = false, ["entries"] = entries, ["existingSceneCatalog"] = catalog };
        File.WriteAllText(folder + "CurrentInputCandidates.json", output.ToString(Formatting.Indented), new UTF8Encoding(false));
        var pairs = entries.Where(e => (bool)e["trainingEligible"]).Select(e =>
        {
            var messages = (JArray)e["messages"].DeepClone();
            messages.Add(new JObject { ["role"] = "assistant", ["content"] = e["expectedOutput"].ToString(Formatting.None) });
            return new JObject { ["id"] = e["id"].DeepClone(), ["splitGroup"] = e["splitGroup"].DeepClone(),
                ["messages"] = messages }.ToString(Formatting.None);
        }).ToArray();
        File.WriteAllLines(folder + "TrainingPairs.jsonl", pairs, new UTF8Encoding(false));
        return new { exported = entries.Count, structuralPass = entries.Count(e => (bool)e["validation"]["tryBuild"] &&
            (bool)e["validation"]["sameBindings"] && (bool)e["validation"]["situation"] && (bool)e["validation"]["lengths"]),
            trainingPairs = pairs.Length, inferenceExecuted = false, saveAccess = false, output = folder + "CurrentInputCandidates.json" };
    }
}
