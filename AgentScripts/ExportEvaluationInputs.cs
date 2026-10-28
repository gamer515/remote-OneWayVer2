using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Read current game modules and authored sources only; never infer, edit assets or access saves.
public static class ExportEvaluationInputs
{
    public static object Run()
    {
        if (Application.isPlaying || LocalStoryRuntimeService.IsBusy)
            throw new InvalidOperationException("Stop gameplay/generation before exporting evaluation inputs.");
        const string root = "Tests/LocalStoryBenchmark/";
        var baseline = JObject.Parse(File.ReadAllText(root + "TrainingBaseline.json"));
        using (var sha = SHA256.Create())
            foreach (var file in baseline["frozenFiles"])
                if (BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes((string)file["path"]))).Replace("-", "").ToLowerInvariant() != (string)file["sha256"])
                    throw new InvalidDataException("Frozen source changed: " + file["path"]);
        var plan = JObject.Parse(File.ReadAllText(root + "Colab/EvaluationPlan.json"));
        var split = JObject.Parse(File.ReadAllText(root + "DatasetSplitPlan.json"));
        ChapterContentCatalog.Reset();
        var cases = new JArray();
        foreach (var item in plan["cases"])
        {
            string path = (string)item["storyPath"];
            var info = ChapterContentCatalog.Info(path) ?? throw new InvalidDataException("Unknown story: " + path);
            string sourceFile = ChapterContentCatalog.SourceFile(info);
            var group = split["groups"].Single(g => sourceFile.StartsWith((string)g["sourceFolder"], StringComparison.Ordinal));
            if ((string)group["id"] != (string)item["splitGroup"] || !new[] {"validation", "test"}.Contains((string)group["role"]))
                throw new InvalidDataException("Invalid held-out group: " + path);
            var memory = item["memory"].ToObject<LocalStoryRevisionRequest>();
            var source = JsonUtility.FromJson<EncounterCardRoot>(NarrativeInteractions.StorySource(path));
            var explanation = ChapterContentCatalog.Explanation(path);
            var references = new JArray(source.MainStory.Select((card, i) => new JObject
            {
                ["index"] = i, ["eventId"] = LocalStoryPrompt.EventId(path, card, i), ["speakerId"] = card.speakerId,
                ["text"] = card.text, ["options"] = new JArray(card.options ?? Array.Empty<string>()),
                ["meaning"] = explanation?["coreMeaning"]?[ChapterContentCatalog.CardId(path, i).ToString()]?.DeepClone() ?? new JObject()
            }));
            foreach (var range in LocalStoryPrompt.Plan(path, source))
            {
                var accepted = source.MainStory.Take(range.start).ToList();
                var messages = LocalStoryTestModels.Messages(LocalStoryTestModel.Gemma,
                    LocalStoryPrompt.Messages(path, source, range.start, range.count, accepted, memory));
                var part = LocalStorySections.Slice(source, range.start, range.count);
                var limits = LocalStoryPrompt.Limits(path, LocalStoryPrompt.Paragraph(path, source.MainStory[range.start], range.start));
                cases.Add(new JObject
                {
                    ["id"] = (string)item["id"] + "/" + range.start, ["storyPath"] = path,
                    ["splitGroup"] = group["id"].DeepClone(), ["split"] = group["role"].DeepClone(),
                    ["start"] = range.start, ["count"] = range.count,
                    ["messages"] = JArray.Parse(messages), ["maxNewTokens"] = LocalStoryPrompt.MaxTokens(path, source, range.start, range.count),
                    ["runtimeGrammarReferenceOnly"] = LocalStoryRewrite.Grammar(part, true, limits),
                    ["limits"] = JObject.FromObject(limits),
                    ["sourceCards"] = new JArray(references.Skip(range.start).Take(range.count).Select(c => c.DeepClone())),
                    ["explanation"] = explanation?.DeepClone(),
                    ["sourceFile"] = sourceFile, ["explanationFile"] = ChapterContentCatalog.SourceFile(info, true),
                    ["generationPolicyHash"] = LocalStorySections.GenerationPolicyHash(path),
                    ["meaningCoverageComplete"] = references.Skip(range.start).Take(range.count).All(c => c["meaning"] is JObject m && m.HasValues),
                    ["meaningFallback"] = "Where Explanation meaning is absent, show the exact existing Story text/options as provisional reference; do not create missing meaning labels."
                });
            }
        }
        string fileName = root + "Colab/EvaluationInputs.json";
        var output = new JObject { ["plan"] = plan, ["baselineId"] = baseline["baselineId"].DeepClone(),
            ["cases"] = cases, ["gameWrites"] = false, ["saveAccess"] = false, ["inferenceExecuted"] = false };
        File.WriteAllText(fileName, output.ToString(Formatting.Indented), new UTF8Encoding(false));
        return new { exportedSections = cases.Count, sourceMeaningComplete = cases.Count(c => (bool)c["meaningCoverageComplete"]),
            plannedGenerations = cases.Count * plan["seeds"].Count() * 2, gameWrites = false, saveAccess = false, fileName };
    }
}
