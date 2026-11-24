using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OneWay.Decision.DirectLlm;

public static partial class LocalStoryDialogueQueue
{
    private sealed class LocationDialogueBatch
    {
        internal readonly Dictionary<string, (LocalStoryDialogueJob Job, LocalStoryEmotionLine Line)> Targets = new();
        private readonly string location;
        private readonly JObject speakers = new();
        private readonly Dictionary<string, JObject> sections = new();

        internal LocationDialogueBatch(string location) => this.location = location;

        internal void Add(string key, string branch, string context, string original,
            (LocalStoryDialogueJob Job, LocalStoryEmotionLine Line) target)
        {
            if (!sections.TryGetValue(key, out var section))
            {
                section = new JObject { ["branch"] = branch, ["lines"] = new JArray() };
                if (!string.IsNullOrWhiteSpace(context)) section["context"] = context;
                sections.Add(key, section);
            }
            string id = (Targets.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Targets.Add(id, target);
            var line = target.Line;
            line.originalText = original;
            string speaker = line.speakerId ?? target.Job.characterId;
            if (!speakers.ContainsKey(speaker))
                speakers[speaker] = string.Join(" / ", new[] { line.name, line.speechStyle, line.personality }
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct());
            ((JArray)section["lines"]).Add(new JArray(id, speaker, target.Job.emotion, original));
        }

        internal JObject Payload() => new JObject
        {
            ["location"] = location, ["npcs"] = speakers, ["sections"] = new JArray(sections.Values)
        };
    }

    private static bool NeedsLocationBatch(LocalStoryEmotionLine line) => !line.finished && !line.locationBatchAttempted;

    private static async Task GenerateLocationBatch(SaveIOService store, LocalStoryDialogueJob current,
        DirectStoryGenerator generator, string source, CancellationToken token)
    {
        var queue = Load(store, current.sourceRun);
        string mode = queue.authoredStoryPackMode;
        int seed = 0;
        if (current.authoredPack)
        {
            var progress = store.LoadRunData<GameProgress>(current.sourceRun, "Progress");
            seed = progress?.runSeed ?? 0;
            if (string.IsNullOrEmpty(mode))
                mode = MineAuthoredStory.ModeId(progress?.guideProgress?.authoredMine?.shortStoryPack == true);
        }
        string version = AuthoredMode(current, mode);
        var related = queue.jobs.Where(job => job.jobId != current.jobId && SameLocationVersion(job)).Prepend(current).ToArray();
        var jobs = related.Where(job => (job.status == "Pending" || job.status == "Generating") &&
            CanProcess(store, job) && (job.authoredPack || CanBatchCards(job)));
        var targets = jobs.SelectMany(job => job.emotionLines.Where(NeedsLocationBatch).Select(line => (Job: job, Line: line))).ToArray();
        if (targets.Length == 0) return;

        bool SameLocationVersion(LocalStoryDialogueJob job) =>
            job.authoredPack == current.authoredPack && job.targetRun == current.targetRun &&
            job.revision == current.revision && (current.authoredPack
                ? job.storyPath == current.storyPath && AuthoredMode(job, mode) == version &&
                  (string.IsNullOrEmpty(job.placeId) || string.IsNullOrEmpty(current.placeId) || job.placeId == current.placeId)
                : !string.IsNullOrEmpty(current.placeId) && job.placeId == current.placeId &&
                  job.chapterId == current.chapterId && job.episodeId == current.episodeId);

        bool CanBatchCards(LocalStoryDialogueJob job)
        {
            if (job == current) return true;
            return ChapterContentCatalog.CanRewrite(job.storyPath) &&
                ChapterContentCatalog.ResolveStoryPath(job.storyPath, job.targetRun) == job.storyPath &&
                ChapterContentCatalog.Story(job.storyPath) is string original && Hash(original) == job.sourceHash &&
                store.CanReplaceEmotionDialogue(job);
        }

        LocationDialogueBatch batch;
        if (current.authoredPack) batch = BuildAuthoredBatch(current, targets, seed, version);
        else
        {
            // The first recorded story is the entry point for legacy jobs at the same physical place.
            var entry = related.OrderBy(job => job.sequence).First();
            batch = BuildCardBatch(entry, targets, entry == current ? source : ChapterContentCatalog.Story(entry.storyPath));
            if (batch == null || !batch.Targets.Values.Any(target => target.Job == current))
                batch = BuildCardBatch(current, targets, source);
        }
        if (batch == null || batch.Targets.Count == 0) return;

        DirectLlmResult result = null;
        try { result = await generator.RewriteDialogueBatchAsync(batch.Payload(), token); }
        catch (DirectLlmGenerationException)
        {
            token.ThrowIfCancellationRequested();
            // All rows still need their usual single-line provider retries and fallback.
        }
        var replies = result == null ? Array.Empty<JObject>() :
            ((JArray)DirectLlmProtocol.Parse(result.RawText)["lines"]).OfType<JObject>().ToArray();
        var keyed = replies.Where(row => row["id"]?.Type == JTokenType.String)
            .ToLookup(row => (string)row["id"], StringComparer.Ordinal);
        int accepted = 0;
        foreach (var entry in batch.Targets)
        {
            var line = entry.Value.Line;
            line.locationBatchAttempted = true;
            var rows = keyed[entry.Key].ToArray();
            if (rows.Length != 1 || rows[0].Count != 2 || rows[0]["text"]?.Type != JTokenType.String) continue;
            string text = (string)rows[0]["text"];
            if (LocalStoryDialogueRules.Rejection(line.originalText, text) != null) continue;
            CompleteLine(line, line.originalText, (text, result.Provider.ToString(), result.Model));
            accepted++;
        }
        SaveJobs(store, current.sourceRun, batch.Targets.Values.Select(target => target.Job).Distinct());
        int unexpectedIds = keyed.Count(group => !batch.Targets.ContainsKey(group.Key));
        LocalStoryLog.Write(LocalStoryLogStage.Working,
            $"{current.storyPath} / 장소 묶음 {accepted}/{batch.Targets.Count}줄 통과 / 미등록 ID {unexpectedIds}개",
            activity: "대사 생성");
    }

    private static LocationDialogueBatch BuildAuthoredBatch(LocalStoryDialogueJob current,
        (LocalStoryDialogueJob Job, LocalStoryEmotionLine Line)[] targets, int seed, string mode)
    {
        if (current.storyPath?.StartsWith(MineAuthoredStory.Prefix, StringComparison.Ordinal) != true) return null;
        string id = current.storyPath.Substring(MineAuthoredStory.Prefix.Length);
        var remaining = targets.ToLookup(target => (target.Line.authoredLinePath, target.Line.originalText));
        var added = new HashSet<LocalStoryEmotionLine>();
        LocationDialogueBatch batch = null;
        foreach (var (variant, script) in MineAuthoredStory.ReadDialogueVersion(id, seed, mode == "Short"))
        {
            batch ??= new LocationDialogueBatch((string)script["title"] ?? id);
            foreach (var row in script.Descendants().OfType<JObject>().Where(row => row["text"]?.Type == JTokenType.String))
            {
                var target = FindTarget(row);
                if (target.Line == null) continue;
                added.Add(target.Line);
                bool opening = row.Path.StartsWith("opening[", StringComparison.Ordinal);
                string key = row.Parent.Path;
                var choices = row.Ancestors().OfType<JObject>()
                    .Where(node => node["id"] != null && node["text"]?.Type == JTokenType.String)
                    .Reverse().Select(node => (string)node["text"]).Distinct().ToArray();
                string branch = opening ? "공통 도입" : choices.LastOrDefault() ?? key;
                if (!opening)
                {
                    var outcome = row.Ancestors().OfType<JProperty>()
                        .Select(property => property.Name).FirstOrDefault(name => name is "success" or "failure" or "win" or "lose");
                    if (outcome != null) branch += outcome is "success" or "win" ? " · 성공" : " · 실패";
                }
                if (variant != "base") branch += " · " + variant;
                string context = AuthoredPaymentContext(row) ?? (choices.Length > 1
                    ? string.Join(" → ", choices.Take(choices.Length - 1))
                    : (string)(row.Previous as JObject)?["text"] ?? (opening ? (string)script["context"] : null));
                batch.Add(variant + "/" + key, branch, context, (string)row["text"], target);
            }
            if (added.Count == targets.Length) break;
        }
        return batch;

        (LocalStoryDialogueJob Job, LocalStoryEmotionLine Line) FindTarget(JObject row)
        {
            if (row["speaker_id"]?.Type != JTokenType.String) return default;
            string speaker = (string)row["speaker_id"];
            return remaining[(row.Path, (string)row["text"])].FirstOrDefault(target => !added.Contains(target.Line) &&
                (!id.StartsWith("MAIN-", StringComparison.Ordinal) || target.Line.speakerId == speaker));
        }
    }

    private static string AuthoredPaymentContext(JObject row)
    {
        var paid = row.Ancestors().OfType<JObject>().FirstOrDefault(node => node.Parent?.Path.EndsWith(".coin_options", StringComparison.Ordinal) == true);
        string id = (string)paid?["id"];
        string ability = id switch
        {
            "strength" => "힘", "speed" => "속도", "intelligence" => "지능", "charm" => "매력", _ => null
        };
        int? delta = ability == null ? null : (int?)paid["result"]?["stats"]?[id];
        return delta is < 0 ? $"플레이어가 {ability} 코인 {-(long)delta.Value}개를 낸 직후." : null;
    }

    private static LocationDialogueBatch BuildCardBatch(LocalStoryDialogueJob current,
        (LocalStoryDialogueJob Job, LocalStoryEmotionLine Line)[] targets, string source)
    {
        if (string.IsNullOrEmpty(source)) return null;
        var root = JObject.Parse(source);
        var info = ChapterContentCatalog.Info(current.storyPath);
        var batch = new LocationDialogueBatch((string)info?["sceneName"] ??
            (string)info?["placeName"] ?? (string)info?["characterId"] ?? current.placeId ?? current.storyPath);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(string Path, JObject Script, string Context)>();
        pending.Enqueue((current.storyPath, root, (string)root["storyFrame"]?["purpose"]));
        visited.Add(current.storyPath);
        string encounterFolder = current.storyPath.Substring(0, current.storyPath.LastIndexOf('/') + 1);
        while (pending.Count > 0)
        {
            var (path, script, context) = pending.Dequeue();
            string branch = path == current.storyPath ? "공통 도입" : path.Substring(encounterFolder.Length);
            int index = 0;
            string previous = null;
            foreach (var card in ((JArray)script["MainStory"]).OfType<JObject>())
            {
                int cardIndex = index++;
                string original = (string)card["text"];
                var target = targets.FirstOrDefault(entry => entry.Job.storyPath == path && entry.Line.cardIndex == cardIndex &&
                    (string.IsNullOrEmpty(entry.Line.originalText) || entry.Line.originalText == original));
                if (target.Line != null && original != null)
                    batch.Add(path, branch, context ?? previous, original, target);
                EnqueueLinks(card, original);
                previous = original;
            }
        }

        void EnqueueLinks(JObject card, string cardText)
        {
            var choices = card["choiceActions"] as JArray;
            var options = card["options"] as JArray;
            if (choices != null)
                for (int i = 0; i < choices.Count; i++)
                    if (choices[i] is JObject action)
                        EnqueuePaths(action, options != null && i < options.Count ? (string)options[i] : cardText);
            foreach (var property in card.Properties())
                if (property.Name != "choiceActions") EnqueuePaths(property, cardText);
        }

        void EnqueuePaths(JToken node, string context)
        {
            var properties = node is JProperty direct
                ? new[] { direct }.Concat((direct.Value as JContainer)?.Descendants().OfType<JProperty>() ?? Enumerable.Empty<JProperty>())
                : ((JObject)node).Descendants().OfType<JProperty>();
            foreach (string linked in properties
                .Where(property => property.Name.EndsWith("StoryPath", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.Type == JTokenType.String).Select(property => (string)property.Value))
                if (linked.StartsWith(encounterFolder, StringComparison.Ordinal) && visited.Add(linked) &&
                    ChapterContentCatalog.Story(linked) is string linkedSource)
                    pending.Enqueue((linked, JObject.Parse(linkedSource), context));
        }
        return batch;
    }
}
