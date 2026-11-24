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
    private const string ComboColors = "빨파노청";
    private Dictionary<string, string> colorCombos = new Dictionary<string, string>();
    private JObject comboRewards;
    private bool readingDeferred;
    internal JObject ReadingPreview { get; private set; }
    internal int ReadingPreviewNumber { get; private set; }
    private const int MaxBookNumber = 13;
    private static readonly string[] AbilityComboPrefixes = { "CM_STAT_STR_", "CM_STAT_SPD_", "CM_STAT_INT_", "CM_STAT_CHA_" };

    // Match the original title to its original textbook template. Only the vending lane changes.
    private static int ProductionAbility(BookDefinition book) => book.id switch
    {
        "BK002" or "BK018" => 1,
        "BK007" => 0,
        "BK009" => 3,
        _ => book.ability
    };

    private bool AssignBookNumber(MineLearningJob job)
    {
        if (job == null || job.bookNumber is >= 1 and <= MaxBookNumber) return false;
        job.bookNumber = 1 + (int)(MineChapterSettings.Roll(seed, ++progress.bookNumberSequence, "book-number") * MaxBookNumber);
        return true;
    }

    internal static bool IsAbilityCombo(JObject combo) => (string)combo["type"] == "mainStat";

    private JObject NumberComboReward(MineLearningJob[] batch)
    {
        if (batch.Length < 3 || batch[0].stackOrder <= 0) return null;
        int ability = batch[0].ability;
        int start = batch[0].bookNumber;
        if ((uint)ability >= AbilityComboPrefixes.Length || start < 1 || start + batch.Length - 1 > MaxBookNumber)
            return null;
        for (int i = 0; i < batch.Length; i++)
        {
            var job = batch[i];
            if (job.ability != ability || job.bookNumber != start + i ||
                job.bookId == StrengthTutorialBook || !readingBooks.ContainsKey(job.bookId ?? "")) return null;
        }
        string prefix = AbilityComboPrefixes[ability];
        return readingCombos.Where(combo => IsAbilityCombo(combo) &&
                ((string)combo["id"]).StartsWith(prefix, StringComparison.Ordinal) &&
                (int)combo["condition"]["minCompleted"] >= 3 &&
                (int)combo["condition"]["minCompleted"] <= batch.Length)
            .OrderByDescending(combo => (int)combo["condition"]["minCompleted"]).FirstOrDefault();
    }

    private JObject CompleteNumberComboChoice()
    {
        if (progress.numberComboChoicesRemaining <= 0 || --progress.numberComboChoicesRemaining != 0) return null;
        string rewardId = progress.numberComboRewardId;
        progress.numberComboRewardId = null;
        var combo = readingCombos.FirstOrDefault(entry => (string)entry["id"] == rewardId);
        if (combo == null) return null;
        RecordComboPayout(rewardId);
        return combo;
    }

    private void RecordComboPayout(string id)
    {
        var record = FindReadingComboProgress(id);
        if (record == null)
        {
            record = new ReadingComboProgress { id = id, payouts = ReadingComboPayouts(id) };
            progress.readingComboProgress.Add(record);
        }
        record.payouts++;
    }

    private string ColorComboPattern(MineLearningJob[] batch)
    {
        if (batch.Any(job => (uint)job.ability >= ComboColors.Length ||
            !readingBooks.ContainsKey(job.bookId ?? ""))) return null;
        string pattern = new string(batch.Select(job => ComboColors[job.ability]).ToArray());
        return colorCombos.ContainsKey(pattern) ? pattern : null;
    }

    private static string ColorComboId(string pattern) => "CM_COLOR_" + pattern;
    internal IEnumerable<LearningComboReward> DiscoveredColorCombos => colorCombos.Keys
        .Where(pattern => ReadingComboPayouts(ColorComboId(pattern)) > 0).Select(ColorComboReward);

    private LearningComboReward ColorComboReward(string pattern)
    {
        var groups = pattern.Select(color => ComboColors.IndexOf(color)).GroupBy(ability => ability).ToArray();
        int main = groups.OrderByDescending(group => group.Count()).First().Key;
        var others = groups.Select(group => group.Key).Where(ability => ability != main).ToArray();
        var config = comboRewards["colorRewards"][pattern.Length.ToString()];
        var coins = new int[AbilityKeys.Length];
        coins[main] = (int)config["main"];
        int otherCoins = (int)config["others"];
        for (int i = 0; i < others.Length; i++)
            coins[others[i]] = otherCoins / others.Length + (i < otherCoins % others.Length ? 1 : 0);
        return new LearningComboReward($"{colorCombos[pattern]} · {pattern}", coins);
    }

    private LearningComboReward? CompleteColorComboChoice()
    {
        if (progress.colorComboChoicesRemaining <= 0 || --progress.colorComboChoicesRemaining != 0) return null;
        string pattern = progress.colorComboPattern;
        progress.colorComboPattern = null;
        if (pattern == null || !colorCombos.ContainsKey(pattern)) return null;
        RecordComboPayout(ColorComboId(pattern));
        return ColorComboReward(pattern);
    }

    private int[] BoostComboCoins(int[] coins, int bookCount)
    {
        int[] reward = (int[])coins.Clone();
        int[] byReward = Enumerable.Range(0, reward.Length).OrderByDescending(ability => reward[ability]).ToArray();
        int main = byReward[0];
        int other = reward[byReward[1]] > 0 ? byReward[1] : (main + 1) % reward.Length;
        var config = comboRewards["existingBonus"];
        reward[main] += (int)config[bookCount >= (int)config["longFrom"] ? "longMain" : "shortMain"];
        reward[other] += (int)config["other"];
        return reward;
    }

    internal int[] ExistingComboCoins(JObject combo, int bookCount = 0) =>
        BoostComboCoins(ReadCoinReward(combo["reward"]), bookCount > 0 ? bookCount : (int)combo["condition"]["minCompleted"]);

    internal static string FormatCoinReward(int[] coins) =>
        string.Join(" / ", coins.Select((quantity, i) => quantity > 0 ? AbilityName(i) + " +" + quantity : null)
            .Where(label => label != null));

    private static string ComboResult(LearningComboReward combo) => $"{combo.Name}  {FormatCoinReward(combo.Coins)}";

    internal bool TryOpenReadingPreview(MineLearningJob job)
    {
        if (!CanMoveBooks || job == null || job == progress.producing || job.started || job.held ||
            job.comboStudy || job.transit > 0 || PendingReading != null || ReadingResult != null ||
            (!progress.active.Contains(job) && !progress.queued.Contains(job)) ||
            !readingBooks.TryGetValue(job.bookId ?? "", out var book))
            return false;
        ReadingPreview = book;
        ReadingPreviewNumber = job.bookNumber;
        return true;
    }

    internal void CloseReadingPreview()
    {
        ReadingPreview = null;
        ReadingPreviewNumber = 0;
    }
    internal IReadOnlyList<JObject> ReadingCombos => readingCombos;
    internal IEnumerable<JObject> DiscoveredBooks => progress.discoveredBookIds
        .Where(readingBooks.ContainsKey).Select(id => readingBooks[id])
        .GroupBy(book => (string)book["parodyTitle"]).Select(titles => titles.First());

    private bool DiscoverBook(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (previousBookIds.TryGetValue(id, out string current)) id = current;
        if (!readingBooks.ContainsKey(id) || progress.discoveredBookIds.Contains(id)) return false;
        progress.discoveredBookIds.Add(id);
        return true;
    }
    internal string ReadingTitle(string id) => (string)readingBooks[id]["parodyTitle"];
    internal int ReadingAbility(string id)
    {
        var definition = authoredBooks?.FirstOrDefault(book => book.id == id);
        return definition != null ? ProductionAbility(definition) : Array.IndexOf(AbilityKeys, (string)readingBooks[id]["mainStat"]);
    }
    internal IEnumerable<int[]> ReadingChoiceRewards(string id) =>
        readingBooks[id]["choices"].Select(choice => ReadChoiceCoins(id, choice["reward"]));

    private static int[] ReadChoiceCoins(string bookId, JToken reward) =>
        ReadCoinReward(reward, bookId == StrengthTutorialBook ? 1 : 2);

    internal static int[] ReadCoinReward(JToken reward, int divisor = 1) =>
        AbilityKeys.Select(key => ((int?)reward?[key] ?? 0) / divisor).ToArray();

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

    public string GetBookHoverText(MineLearningJob job)
    {
        string title = GetBookDisplayName(job);
        if (job.bookNumber > 0) title += $" · {job.bookNumber}번";
        if (IsStudying(job)) return $"{title}\n학습 중 {job.remaining:0.0}초";
        if (job.started) return $"{title}\n학습 일시 정지 · {job.remaining:0.0}초 남음";
        if (job.onBoard && job.boardIndex is 1 or 2)
        {
            if (!CanRead(job)) return $"{title}\n읽기 능력치 부족";
            if (TodayRemaining == 0) return $"{title}\n오늘 학습량 소진";
        }
        return IsStrengthTutorial(job) ? title + "\n초록 보드에 두면 기사가 펼쳐 볼지도." : title;
    }

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
        comboRewards = ReadContent("Reading/ComboRewards");
        colorCombos = comboRewards["colorCombos"].SelectMany(group => group["patterns"].Values<string>()
            .Select(pattern => (pattern, name: (string)group["name"]))).ToDictionary(combo => combo.pattern, combo => combo.name);
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
        string id = (string)book["bookId"];
        var amounts = ReadChoiceCoins(id, choice["reward"]);
        var combos = new List<LearningComboReward>();
        void AddCombo(LearningComboReward combo)
        {
            for (int i = 0; i < amounts.Length; i++)
                amounts[i] += combo.Coins[i];
            combos.Add(combo);
        }

        progress.pendingReading.RemoveAt(0);
        if (!progress.completedReading.Contains(id)) progress.completedReading.Add(id);
        if (!progress.mastered.Contains(id)) progress.mastered.Add(id);
        progress.successfulStudies++;
        foreach (var combo in id == StrengthTutorialBook ? Array.Empty<JObject>() : readingCombos)
        {
            if (IsAbilityCombo(combo) || !CreditReadingCombo(combo, id)) continue;
            AddCombo(new LearningComboReward((string)combo["name"], ExistingComboCoins(combo)));
        }
        if (CompleteNumberComboChoice() is JObject numberCombo)
        {
            AddCombo(new LearningComboReward((string)numberCombo["name"], ExistingComboCoins(numberCombo, progress.numberComboBookCount)));
            progress.numberComboBookCount = 0;
        }
        if (CompleteColorComboChoice() is LearningComboReward colorCombo)
            AddCombo(colorCombo);
        AwardCoins(amounts, combos);
        string coins = FormatCoinReward(amounts);
        progress.readingResult = (string)choice["result"] + "\n\n" +
            (coins.Length > 0 ? coins : "코인 획득 없음");
        var definition = id == StrengthTutorialBook ? null : authoredBooks.FirstOrDefault(entry => entry.id == id);
        if (!string.IsNullOrEmpty(definition?.skill) && definition.skillReward > 0)
        {
            AwardSkill(definition.skill, definition.skillReward);
            var skill = study.skills.FirstOrDefault(entry => entry.id == definition.skill);
            progress.readingResult += $"\n세부 능력  {skill?.name ?? definition.skill} +{definition.skillReward}";
        }
        if (combos.Count > 0) progress.readingResult += "\n콤보 달성\n" + string.Join("\n", combos.Select(ComboResult));
        save?.Invoke();
    }

    internal void CloseReadingResult()
    {
        progress.readingResult = null;
        save?.Invoke();
    }
}
