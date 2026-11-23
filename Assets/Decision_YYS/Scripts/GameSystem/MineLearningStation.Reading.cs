using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed partial class MineLearningStation
{
    internal static readonly string[] AbilityKeys = { "strength", "speed", "intelligence", "charm" };
    internal const string AuthoredContent = "Chapters/Chapter_01/AuthoredNarrative/";
    private Dictionary<string, JObject> readingBooks = new Dictionary<string, JObject>();
    private readonly Dictionary<string, string> previousBookIds = new Dictionary<string, string>(StringComparer.Ordinal);
    private const string FirstComboBook = "BK001", SecondComboBook = "BK002";
    private const string StrengthTutorialBook = "strength_basic";
    private JObject[] readingCombos = Array.Empty<JObject>();
    private bool readingDeferred;
    internal JObject ReadingPreview { get; private set; }

    internal bool TryOpenReadingPreview(MineLearningJob job)
    {
        if (!CanMoveBooks || job == null || job == progress.producing || job.started || job.held ||
            job.comboStudy || job.transit > 0 || PendingReading != null || ReadingResult != null ||
            (!progress.active.Contains(job) && !progress.queued.Contains(job)) ||
            !readingBooks.TryGetValue(job.bookId ?? "", out var book))
            return false;
        ReadingPreview = book;
        return true;
    }

    internal void CloseReadingPreview() => ReadingPreview = null;
    internal IReadOnlyList<JObject> ReadingCombos => readingCombos;
    internal string ReadingTitle(string id) => (string)readingBooks[id]["parodyTitle"];
    internal int ReadingAbility(string id) => Array.IndexOf(AbilityKeys, (string)readingBooks[id]["mainStat"]);
    internal IEnumerable<JToken> ReadingChoiceRewards(string id) =>
        readingBooks[id]["choices"].Select(choice => choice["reward"]);

    internal ReadingComboProgress FindReadingComboProgress(string id) =>
        progress.readingComboProgress.Find(entry => entry.id == id);

    internal int ReadingComboPayouts(string id) =>
        FindReadingComboProgress(id)?.payouts ?? (progress.claimedReadingCombos.Contains(id) ? 1 : 0);

    private bool CanCompleteReadingCombo(JObject combo, MineLearningJob[] batch)
    {
        var ids = combo["condition"]["bookIds"].Values<string>().Distinct().ToArray();
        var credits = FindReadingComboProgress((string)combo["id"])?.unusedBooks;
        return batch.Any(job => ids.Contains(job.bookId)) &&
            ids.Count(id => credits?.Contains(id) == true || batch.Any(job => job.bookId == id)) >=
            (int)combo["condition"]["minCompleted"];
    }

    private bool CreditReadingCombo(JObject combo, string bookId)
    {
        var ids = combo["condition"]["bookIds"].Values<string>().Distinct().ToArray();
        if (!ids.Contains(bookId)) return false;
        string id = (string)combo["id"];
        var entry = FindReadingComboProgress(id);
        if (entry == null)
        {
            entry = new ReadingComboProgress { id = id, payouts = ReadingComboPayouts(id) };
            progress.readingComboProgress.Add(entry);
        }
        entry.unusedBooks.Add(bookId);
        int required = (int)combo["condition"]["minCompleted"];
        var used = ids.Where(entry.unusedBooks.Contains).Take(required).ToArray();
        if (used.Length < required) return false;

        foreach (string usedId in used) entry.unusedBooks.Remove(usedId);
        entry.payouts++;
        return true;
    }

    internal static JObject ReadContent(string path) =>
        JObject.Parse(Resources.Load<TextAsset>(AuthoredContent + path).text);

    private static bool IsStrengthTutorial(MineLearningJob job) => job?.ability == 0 &&
        (job.bookId == StrengthTutorialBook ||
            string.IsNullOrEmpty(job.bookId) && (string.IsNullOrEmpty(job.bookName) || job.bookName == "힘 책"));

    public string GetBookHoverText(MineLearningJob job) => IsStrengthTutorial(job)
        ? "힘 책\n초록 보드에 두면 기사가 펼쳐 볼지도."
        : GetBookDisplayName(job);

    private void PrepareStrengthTutorial(MineLearningJob job)
    {
        if (!IsStrengthTutorial(job)) return;
        job.bookId = StrengthTutorialBook;
        job.bookName = "힘 책";
        if (job.duration <= 0) job.duration = job.remaining = Mathf.Max(.1f, minimumSeconds);
    }

    private bool NeedsStrengthTutorial => !progress.completedReading.Contains(StrengthTutorialBook) &&
        !progress.pendingReading.Contains(StrengthTutorialBook) &&
        !progress.active.Concat(progress.queued).Concat(progress.stored).Append(progress.producing).Any(IsStrengthTutorial);

    private bool OpenCompletedReading(string id)
    {
        if (!readingBooks.ContainsKey(id ?? "")) return false;
        if (id != StrengthTutorialBook ||
            !progress.completedReading.Contains(id) && !progress.pendingReading.Contains(id))
        {
            progress.pendingReading.Add(id);
            readingDeferred = false;
        }
        else ShowStudyResult(true, "이미 보상을 받은 책이거나 선택을 기다리고 있습니다.");
        return true;
    }

    private void ConfigureReading()
    {
        readingBooks = Resources.LoadAll<TextAsset>(AuthoredContent + "Reading/books")
            .Select(asset => JObject.Parse(asset.text)).ToDictionary(book => (string)book["bookId"]);
        readingCombos = Resources.LoadAll<TextAsset>(AuthoredContent + "Reading/combos")
            .Select(asset => JObject.Parse(asset.text)).OrderBy(combo => (string)combo["id"]).ToArray();
        var previous = authoredBooks;
        previousBookIds.Clear();
        authoredBooks = readingBooks.Values.OrderBy(book => (string)book["bookId"]).GroupBy(book =>
            Array.IndexOf(AbilityKeys, (string)book["mainStat"])).SelectMany(group =>
        {
            var templates = previous.Where(book => book.ability == group.Key).ToArray();
            return group.Select((entry, index) =>
            {
                string id = (string)entry["bookId"];
                var exact = templates.FirstOrDefault(book => book.id == id ||
                    book.name == (string)entry["originalTitle"] || book.name == (string)entry["parodyTitle"]);
                if (!string.IsNullOrEmpty(exact?.id) && exact.id != id)
                    previousBookIds[exact.id] = id;
                var template = exact
                    ?? templates.ElementAtOrDefault(Math.Min(index, templates.Length - 1));
                return new BookDefinition
                {
                    id = id, name = (string)entry["parodyTitle"], ability = group.Key,
                    author = (string)entry["author"], seconds = template?.seconds ?? minimumSeconds,
                    generationSeconds = template?.generationSeconds ?? deliverySeconds,
                    requiredKnowledge = template?.requiredKnowledge ?? 0,
                    requiredAbility = template?.requiredAbility ?? 0,
                    skill = exact?.skill,
                    skillReward = exact?.skillReward ?? 0
                };
            });
        }).ToArray();
    }

    private string CurrentReadingBookId(MineLearningJob job)
    {
        if (IsStrengthTutorial(job)) return StrengthTutorialBook;
        if (!string.IsNullOrEmpty(job.bookId))
        {
            if (readingBooks.ContainsKey(job.bookId)) return job.bookId;
            if (previousBookIds.TryGetValue(job.bookId, out string id)) return id;
        }
        if (string.IsNullOrEmpty(job.bookName)) return job.bookId;
        foreach (var book in readingBooks)
            if ((string)book.Value["originalTitle"] == job.bookName || (string)book.Value["parodyTitle"] == job.bookName)
                return book.Key;
        return job.bookId;
    }

    internal JObject PendingReading => !readingDeferred && progress?.pendingReading?.Count > 0 &&
        readingBooks.TryGetValue(progress.pendingReading[0], out var book) ? book : null;
    internal string ReadingResult => string.IsNullOrWhiteSpace(progress?.readingResult) ? null : progress.readingResult;

    internal bool ResumeDeferredReading()
    {
        if (!readingDeferred) return false;
        readingDeferred = false;
        return PendingReading != null;
    }

    internal void SelectReadingChoice(int index)
    {
        var book = PendingReading;
        if (book == null || ReadingResult != null || index < 0 || index >= book["choices"].Count()) return;
        var choice = book["choices"][index];
        var amounts = new int[4];
        int[] AddRewards(JToken reward)
        {
            var added = new int[amounts.Length];
            for (int i = 0; i < amounts.Length; i++)
            {
                added[i] = (int?)reward?[AbilityKeys[i]] ?? 0;
                amounts[i] += added[i];
            }
            return added;
        }

        string id = (string)book["bookId"];
        progress.pendingReading.RemoveAt(0);
        if (!progress.completedReading.Contains(id)) progress.completedReading.Add(id);
        if (!progress.mastered.Contains(id)) progress.mastered.Add(id);
        progress.successfulStudies++;
        AddRewards(choice["reward"]);
        var combos = new List<LearningComboReward>();
        foreach (var combo in id == StrengthTutorialBook ? Array.Empty<JObject>() : readingCombos)
        {
            if (!CreditReadingCombo(combo, id)) continue;
            combos.Add(new LearningComboReward((string)combo["name"], AddRewards(combo["reward"])));
        }
        AwardCoins(amounts, combos);
        progress.readingResult = (string)choice["result"] + "\n\n" +
            string.Join(" / ", amounts.Select((quantity, i) => quantity > 0 ? AbilityName(i) + " +" + quantity : null)
                .Where(label => label != null));
        var definition = id == StrengthTutorialBook ? null : authoredBooks.FirstOrDefault(entry => entry.id == id);
        if (!string.IsNullOrEmpty(definition?.skill) && definition.skillReward > 0)
        {
            AwardSkill(definition.skill, definition.skillReward);
            var skill = study.skills.FirstOrDefault(entry => entry.id == definition.skill);
            progress.readingResult += $"\n세부 능력  {skill?.name ?? definition.skill} +{definition.skillReward}";
        }
        if (combos.Count > 0) progress.readingResult += "\n콤보 달성\n" + string.Join("\n", combos.Select(combo => combo.Name));
        save?.Invoke();
    }

    internal void CloseReadingResult()
    {
        progress.readingResult = null;
        save?.Invoke();
    }
}
